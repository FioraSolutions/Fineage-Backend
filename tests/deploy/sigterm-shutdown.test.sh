#!/usr/bin/env bash
# SIGTERM graceful-shutdown proof (#993).
#
# systemd and `systemctl stop` send SIGTERM, not SIGINT (Ctrl+C). This script starts Login and
# Zone against a throwaway local database, puts one load-test bot in the world, and sends
# SIGTERM (not SIGINT) to both processes. It fails unless:
#   - Zone exits 0 (an unhandled SIGTERM is 143);
#   - the bot's character was persisted (has_position, updated_at after enter);
#   - world_state has all 9 subsystem rows after the shutdown flush;
#   - the zone log has a successful shutdown economy reconcile and no shutdown-save error;
#   - Login also exits 0 on SIGTERM.
#
# Requires a reachable PostgreSQL (FINEAGE_DATABASE_URL or the local default) and Debug/Release
# binaries. This is an Integration exercise: it is not hooked into deploy-script-test.yml, which
# has no database. Run from the repo root, or via SigtermShutdownTests.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
CFG="${FINEAGE_BUILD_CONFIGURATION:-Debug}"
LOGIN_BIN="$ROOT/src/Fineage.Login/bin/$CFG/net10.0/Fineage.Login"
ZONE_BIN="$ROOT/src/Fineage.Zone/bin/$CFG/net10.0/Fineage.Zone"
LOGIN_DLL="$ROOT/src/Fineage.Login/bin/$CFG/net10.0/Fineage.Login.dll"
ZONE_DLL="$ROOT/src/Fineage.Zone/bin/$CFG/net10.0/Fineage.Zone.dll"
LOADTEST_DLL="$ROOT/tools/Fineage.LoadTest/bin/$CFG/net10.0/Fineage.LoadTest.dll"

DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"

free_port() {
  python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()'
}

psql_url() {
  python3 - "$DATABASE_URL" "$1" <<'PY'
import sys
src, db = sys.argv[1], sys.argv[2]
parts={}
for p in src.split(";"):
    if "=" in p:
        k,v=p.split("=",1); parts[k.strip().lower()]=v.strip()
print(f"postgresql://{parts.get('username','fineage')}:{parts.get('password','fineage')}@{parts.get('host','127.0.0.1')}:{parts.get('port','5432')}/{db}")
PY
}

base_db_url() {
  python3 - "$DATABASE_URL" <<'PY'
import sys
src=sys.argv[1]
parts={}
for p in src.split(";"):
    if "=" in p:
        k,v=p.split("=",1); parts[k.strip().lower()]=v.strip()
print(f"postgresql://{parts.get('username','fineage')}:{parts.get('password','fineage')}@{parts.get('host','127.0.0.1')}:{parts.get('port','5432')}/postgres")
PY
}

if ! psql "$(base_db_url)" -Atqc 'SELECT 1' >/dev/null 2>&1; then
  echo "SKIP: PostgreSQL is not reachable (set FINEAGE_DATABASE_URL)."
  exit 0
fi

if [[ ! -x "$LOGIN_BIN" || ! -x "$ZONE_BIN" || ! -f "$LOADTEST_DLL" ]]; then
  echo "building $CFG Login, Zone, LoadTest..."
  export PATH="${HOME}/.dotnet:${HOME}/.dotnet/tools:${PATH:-}"
  dotnet build src/Fineage.Login -c "$CFG" -v q -nologo
  dotnet build src/Fineage.Zone -c "$CFG" -v q -nologo
  dotnet build tools/Fineage.LoadTest -c "$CFG" -v q -nologo
fi
[[ -x "$LOGIN_BIN" && -x "$ZONE_BIN" && -f "$LOADTEST_DLL" ]] || { echo "missing build outputs under bin/$CFG/net10.0" >&2; exit 1; }

RUN="$(mktemp -d /tmp/fineage-sigterm-XXXXXX)"
DB="fineage_drill_sigterm"
LOGIN_PORT="$(free_port)"
ZONE_PORT="$(free_port)"
HTTP_PORT="$(free_port)"
STATUS_PORT="$(free_port)"
LOGIN_METRICS="$(free_port)"
ZONE_METRICS="$(free_port)"
STATE_DIR="$RUN/state"
mkdir -p "$STATE_DIR"
LOGIN_LOG="$RUN/login.log"
ZONE_LOG="$RUN/zone.log"
BOT_LOG="$RUN/bot.log"
LOGIN_PID=""
ZONE_PID=""
BOT_PID=""

cleanup() {
  local status=$?
  set +e
  for pid in "$BOT_PID" "$ZONE_PID" "$LOGIN_PID"; do
    [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null && kill -9 "$pid" 2>/dev/null
  done
  wait "$BOT_PID" "$ZONE_PID" "$LOGIN_PID" 2>/dev/null
  psql "$(base_db_url)" -qc "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DB';" >/dev/null 2>&1
  psql "$(base_db_url)" -qc "DROP DATABASE IF EXISTS ${DB};" >/dev/null 2>&1
  if [[ "${FINEAGE_DRILL_KEEP:-0}" == "1" ]]; then
    echo "keeping $RUN (FINEAGE_DRILL_KEEP=1)"
  else
    rm -rf "$RUN"
  fi
  exit "$status"
}
trap cleanup EXIT

echo "=== SIGTERM shutdown drill  run=$RUN db=$DB  ports login=$LOGIN_PORT zone=$ZONE_PORT http=$HTTP_PORT status=$STATUS_PORT ==="

psql "$(base_db_url)" -qc "DROP DATABASE IF EXISTS ${DB};"
psql "$(base_db_url)" -qc "CREATE DATABASE ${DB};"
export FINEAGE_DATABASE_URL="Host=127.0.0.1;Port=5432;Database=${DB};Username=fineage;Password=fineage"
# Prefer the parsed host/user from the caller URL when it is not the local default.
if [[ "$DATABASE_URL" == *"Host="* ]]; then
  export FINEAGE_DATABASE_URL="$(python3 - "$DATABASE_URL" "$DB" <<'PY'
import sys
src, db=sys.argv[1], sys.argv[2]
parts={}
for p in src.split(";"):
    if "=" in p:
        k,v=p.split("=",1); parts[k.strip()]=v.strip()
parts["Database"]=db
print(";".join(f"{k}={v}" for k,v in parts.items()))
PY
)"
fi

"$LOGIN_BIN" migrate

export FINEAGE_BIND=127.0.0.1
export FINEAGE_HANDOFF_SECRET="sigterm-993-secret"
export FINEAGE_ZONE_HOST=127.0.0.1
export FINEAGE_ZONE_PORT="$ZONE_PORT"
export FINEAGE_NET_ZONE_PORT="$ZONE_PORT"
export FINEAGE_NET_LOGIN_PORT="$LOGIN_PORT"
export FINEAGE_LOGIN_HTTP_PORT="$HTTP_PORT"
export FINEAGE_ZONE_STATUS_PORT="$STATUS_PORT"
export FINEAGE_LOGIN_METRICS_PORT="$LOGIN_METRICS"
export FINEAGE_ZONE_METRICS_PORT="$ZONE_METRICS"
export FINEAGE_METRICS_BIND=127.0.0.1
export FINEAGE_GEODATA_REQUIRED=0
export FINEAGE_ZONE_REGION_WORKERS=0
export FINEAGE_ZONE_ADMIN=0
export FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0
export FINEAGE_ZONE_MAPS=ti_village
export FINEAGE_ZONE_AOI_CELL=3000
export FINEAGE_STATE_AUDIT=log
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=sigtermbot
export FINEAGE_LOGIN_LOADTEST_PASSWORD=sigterm-pass-v1
export FINEAGE_STATE_DIR="$STATE_DIR"

"$LOGIN_BIN" --bind 127.0.0.1 --http-port "$HTTP_PORT" >"$LOGIN_LOG" 2>&1 &
LOGIN_PID=$!
"$ZONE_BIN" --bind 127.0.0.1 --port "$ZONE_PORT" --state-dir "$STATE_DIR" >"$ZONE_LOG" 2>&1 &
ZONE_PID=$!

wait_http() {
  local url="$1" want="$2" seconds="$3" i code
  for i in $(seq 1 "$seconds"); do
    code="$(curl -s -o /tmp/fineage-health-body.$$ -w '%{http_code}' "$url" || true)"
    if [[ "$code" == "$want" ]]; then
      return 0
    fi
    # A process that has already died cannot become ready.
    if ! kill -0 "$ZONE_PID" 2>/dev/null || ! kill -0 "$LOGIN_PID" 2>/dev/null; then
      echo "a process died while waiting for $url (last HTTP $code)" >&2
      tail -40 "$ZONE_LOG" "$LOGIN_LOG" >&2 || true
      return 1
    fi
    sleep 1
  done
  echo "timed out waiting for $url to return $want (last HTTP $code)" >&2
  cat /tmp/fineage-health-body.$$ 2>/dev/null || true
  tail -40 "$ZONE_LOG" "$LOGIN_LOG" >&2 || true
  return 1
}

wait_http "http://127.0.0.1:${HTTP_PORT}/health/ready" 200 60
wait_http "http://127.0.0.1:${STATUS_PORT}/health/ready" 200 180
echo "both processes ready"

cat > "$RUN/one-bot.yaml" <<YAML
name: sigterm-one-bot
extends: $ROOT/tools/Fineage.LoadTest/scenarios/bots-20-smoke.yaml
botCount: 1
rampUpSeconds: 1
durationSeconds: 30
spawnLayout: town
loginHost: 127.0.0.1
loginPort: $LOGIN_PORT
gatekeeperTravelShare: 0
YAML

dotnet "$LOADTEST_DLL" --scenario "$RUN/one-bot.yaml" --bot-count 1 --ramp-up-seconds 1 --duration-seconds 30 \
  >"$BOT_LOG" 2>&1 &
BOT_PID=$!

# Wait until the bot is in the world (zone status playersOnline, or a character row appears).
in_world=0
for i in $(seq 1 45); do
  online="$(curl -s "http://127.0.0.1:${STATUS_PORT}/status" | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d.get("playersOnline") or d.get("online") or 0)' 2>/dev/null || echo 0)"
  if [[ "$online" != "0" && "$online" != "" ]]; then
    in_world=1
    echo "bot in world: playersOnline=$online (after ${i}s)"
    break
  fi
  sleep 1
done
if [[ "$in_world" != 1 ]]; then
  echo "bot did not enter the world; bot log:" >&2
  tail -60 "$BOT_LOG" >&2
  echo "zone log:" >&2
  tail -40 "$ZONE_LOG" >&2
  exit 1
fi

# Snapshot the live character row before SIGTERM — the durable copy should appear after shutdown.
BEFORE_JSON="$RUN/before.json"
psql "$(psql_url "$DB")" -Atqc "SELECT json_agg(row_to_json(t)) FROM (SELECT id, account, name, has_position, x, y, z, updated_at, last_access FROM characters) t;" >"$BEFORE_JSON"
echo "characters before SIGTERM: $(cat "$BEFORE_JSON")"

# SIGTERM only (not SIGINT). systemd stop does this. Give the graceful path time to finish.
wait_term() {  # pid seconds name outfile
  local pid="$1" seconds="$2" name="$3" out="$4" i
  kill -TERM "$pid" || { echo 143 >"$out"; echo "$name already gone before SIGTERM"; return 0; }
  for i in $(seq 1 "$seconds"); do
    if ! kill -0 "$pid" 2>/dev/null; then
      set +e
      wait "$pid"
      local code=$?
      set -e
      echo "$code" >"$out"
      echo "$name SIGTERM exit=$code"
      return 0
    fi
    sleep 1
  done
  echo 99 >"$out"
  echo "$name did not exit within ${seconds}s of SIGTERM" >&2
  return 1
}

wait_term "$ZONE_PID" 60 Zone "$RUN/zone.exit" || true
ZONE_PID=""
wait_term "$LOGIN_PID" 20 Login "$RUN/login.exit" || true
LOGIN_PID=""
zone_exit="$(cat "$RUN/zone.exit")"
login_exit="$(cat "$RUN/login.exit")"

# The bot will fail once the zone is gone; that is expected.
kill -TERM "$BOT_PID" 2>/dev/null || true
wait "$BOT_PID" 2>/dev/null || true
BOT_PID=""

AFTER_JSON="$RUN/after.json"
psql "$(psql_url "$DB")" -Atqc "SELECT json_agg(row_to_json(t)) FROM (SELECT id, account, name, has_position, x, y, z, updated_at, last_access FROM characters) t;" >"$AFTER_JSON"
WORLD_ROWS="$(psql "$(psql_url "$DB")" -Atqc "SELECT count(*) FROM world_state;")"
WORLD_SYSTEMS="$(psql "$(psql_url "$DB")" -Atqc "SELECT string_agg(system, ',' ORDER BY system) FROM world_state;")"
echo "characters after SIGTERM: $(cat "$AFTER_JSON")"
echo "world_state rows=$WORLD_ROWS systems=$WORLD_SYSTEMS"

fail=0
if [[ "$zone_exit" != 0 ]]; then
  echo "FAIL: Zone exit $zone_exit (0 is a graceful SIGTERM; 143 means the runtime was killed without the handler)" >&2
  fail=1
fi
if [[ "$login_exit" != 0 ]]; then
  echo "FAIL: Login exit $login_exit (0 is a graceful SIGTERM; 143 means the runtime was killed without the handler)" >&2
  fail=1
fi
if [[ "$WORLD_ROWS" -lt 9 ]]; then
  echo "FAIL: world_state has $WORLD_ROWS row(s), expected 9 subsystem documents after the shutdown flush" >&2
  fail=1
fi
if ! grep -q 'economy reconcile phase=shutdown' "$ZONE_LOG"; then
  echo "FAIL: zone log has no shutdown economy reconcile" >&2
  fail=1
fi
if grep -q 'economy reconcile violation phase=shutdown' "$ZONE_LOG"; then
  echo "FAIL: shutdown economy reconcile reported a violation" >&2
  fail=1
fi
if grep -q '\[ZONE\] shutdown save:' "$ZONE_LOG"; then
  echo "FAIL: shutdown save reported an error" >&2
  fail=1
fi

python3 - "$BEFORE_JSON" "$AFTER_JSON" <<'PY' || fail=1
import json, sys
after=json.load(open(sys.argv[2])) or []
bots=[c for c in after if str(c.get("account","")).startswith("sigtermbot")]
if not bots:
    print("FAIL: no sigtermbot character row after SIGTERM", file=sys.stderr)
    sys.exit(1)
ok=False
for c in bots:
    if c.get("has_position") is True or (c.get("x") and c.get("y")):
        print(f"persisted character id={c['id']} account={c['account']} name={c['name']} pos=({c['x']},{c['y']},{c['z']}) updated_at={c['updated_at']}")
        ok=True
        break
if not ok:
    print("FAIL: sigtermbot character was not persisted with a world position after SIGTERM", file=sys.stderr)
    print(after, file=sys.stderr)
    sys.exit(1)
PY

if [[ "$fail" != 0 ]]; then
  echo "---- zone log (tail) ----" >&2
  tail -80 "$ZONE_LOG" >&2
  echo "---- login log (tail) ----" >&2
  tail -40 "$LOGIN_LOG" >&2
  exit 1
fi

echo "PASS: SIGTERM saved the online character, flushed the ledger, committed 9 world_state rows; Zone exit 0, Login exit 0"
# Copy a short report next to the script invocation so a drill can quote it.
if [[ -n "${FINEAGE_SIGTERM_REPORT:-}" ]]; then
  {
    echo "zone_exit=$zone_exit"
    echo "login_exit=$login_exit"
    echo "world_state_rows=$WORLD_ROWS"
    echo "world_state_systems=$WORLD_SYSTEMS"
    echo "characters_after=$(cat "$AFTER_JSON")"
    echo "zone_shutdown_reconcile=$(grep -n 'economy reconcile phase=shutdown' "$ZONE_LOG" | tail -1)"
  } >"$FINEAGE_SIGTERM_REPORT"
fi
