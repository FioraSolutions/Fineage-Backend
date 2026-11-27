#!/usr/bin/env bash
# #987 disposable backup/restore drill.
#
# Creates two throwaway PostgreSQL containers (source and restore target), seeds synthetic
# characters/inventory through the existing Login CLI + SQL, starts Zone once to produce every
# world-state sidecar, takes a real backup, restores into the fresh container, compares
# characters/inventory, then starts Login+Zone against the restored database and sidecar
# directory. World entry uses the existing Fineage.LoadTest bot smoke. The existing ledger
# invariant is read from fineage_ledger_invariant_violations_total; if that API is absent the
# drill fails explicitly instead of reporting a fake zero.
#
# Environment:
#   FINEAGE_DRILL_BACKUP_ROOT    default local-backups/drill
#   FINEAGE_DRILL_RUN_ROOT       default local-logs/backup-restore-drill
#   FINEAGE_DRILL_KEEP=1         keep containers/run directory for debugging
#   FINEAGE_DRILL_SKIP_BUILD=1   use already-built binaries
#   FINEAGE_DRILL_SKIP_SMOKE=1   stop after DB/sidecar restore (world-entry acceptance stays open)
#   FINEAGE_BUILD_CONFIGURATION  Debug (local, AGENTS) or Release (CI)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
BACKUP_ROOT="${FINEAGE_DRILL_BACKUP_ROOT:-$ROOT/local-backups/drill}"
RUN_ROOT="${FINEAGE_DRILL_RUN_ROOT:-$ROOT/local-logs/backup-restore-drill}"
CFG="${FINEAGE_BUILD_CONFIGURATION:-Debug}"
KEEP="${FINEAGE_DRILL_KEEP:-0}"
SKIP_BUILD="${FINEAGE_DRILL_SKIP_BUILD:-0}"
SKIP_SMOKE="${FINEAGE_DRILL_SKIP_SMOKE:-0}"
REQUIRE_SMOKE="${FINEAGE_DRILL_REQUIRE_SMOKE:-0}"
PG_IMAGE="${FINEAGE_PG_IMAGE:-postgres:16-alpine}"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
SOURCE_CONTAINER="fineage-drill-src-$RUN_ID"
TARGET_CONTAINER="fineage-drill-dst-$RUN_ID"
RUN_DIR="$RUN_ROOT/$RUN_ID"
SOURCE_STATE="$RUN_DIR/source-state"
TARGET_STATE="$RUN_DIR/restored-state"
LOGIN_LOG="$RUN_DIR/login.log"
ZONE_LOG="$RUN_DIR/zone.log"
SOURCE_ZONE_LOG="$RUN_DIR/source-zone.log"
DB_PASSWORD="drill-local-only"
SERVICE_PIDS=()

log() { printf '[drill] %s\n' "$*"; }
die() { printf '[drill] ERROR: %s\n' "$*" >&2; exit 1; }
need_cmd() { command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"; }
sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | awk '{print $1}';
  else shasum -a 256 "$1" | awk '{print $1}'; fi
}
sha256_stream() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum | awk '{print $1}';
  else shasum -a 256 | awk '{print $1}'; fi
}
free_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}
port_open() {
  python3 - "$1" <<'PY'
import socket, sys
s = socket.socket(); s.settimeout(0.5)
try: raise SystemExit(0 if s.connect_ex(("127.0.0.1", int(sys.argv[1]))) == 0 else 1)
finally: s.close()
PY
}
wait_tcp() { # port label timeout
  local port="$1" label="$2" timeout="${3:-120}" n=0
  while (( n < timeout )); do
    if port_open "$port"; then return 0; fi
    sleep 1; n=$((n + 1))
  done
  die "$label did not listen on 127.0.0.1:$port within ${timeout}s"
}
psql_exec() { # container [sql]
  if [[ $# -ge 2 ]]; then
    docker exec -e PGPASSWORD="$DB_PASSWORD" "$1" psql -U fineage -d fineage -Atq -v ON_ERROR_STOP=1 -c "$2"
  else
    docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$1" psql -U fineage -d fineage -Atq -v ON_ERROR_STOP=1
  fi
}
db_digest() { # container sql
  psql_exec "$1" "$2" | sha256_stream
}
start_proc() { # name log command...
  local name="$1" logfile="$2"; shift 2
  python3 -c 'import os, signal, sys; signal.signal(signal.SIGINT, signal.SIG_DFL); os.execvp(sys.argv[1], sys.argv[1:])' "$@" >"$logfile" 2>&1 &
  local pid=$!
  SERVICE_PIDS+=("$pid")
  printf '%s\n' "$pid" >"$RUN_DIR/$name.pid"
}
stop_proc() { # pid
  local pid="$1"
  [[ -n "$pid" ]] || return 0
  kill -0 "$pid" 2>/dev/null || return 0
  kill -INT "$pid" 2>/dev/null || true
  local i
  for i in $(seq 1 30); do kill -0 "$pid" 2>/dev/null || return 0; sleep 1; done
  kill -TERM "$pid" 2>/dev/null || true
  sleep 2
  kill -9 "$pid" 2>/dev/null || true
}
cleanup() {
  local status=$?
  set +e
  local pidfile pid
  if [[ "$KEEP" == "1" ]]; then
    log "keeping run artifacts; leaving service PIDs running for inspection: ${SERVICE_PIDS[*]:-none}"
    exit "$status"
  fi
  for pidfile in "$RUN_DIR"/*.pid; do
    [[ -f "$pidfile" ]] || continue
    pid="$(cat "$pidfile")"
    stop_proc "$pid"
  done
  docker rm -f "$SOURCE_CONTAINER" "$TARGET_CONTAINER" >/dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT

need_cmd docker
need_cmd python3
docker info >/dev/null 2>&1 || die "Docker is not running"
mkdir -p "$RUN_DIR" "$SOURCE_STATE" "$BACKUP_ROOT"
export FINEAGE_BACKUP_ROOT="$BACKUP_ROOT"

if [[ "$SKIP_BUILD" != "1" ]]; then
  log "building Login, Zone and LoadTest ($CFG)"
  dotnet build src/Fineage.Login -c "$CFG" -v q -nologo >/dev/null
  dotnet build src/Fineage.Zone -c "$CFG" -v q -nologo >/dev/null
  dotnet build tools/Fineage.LoadTest -c "$CFG" -v q -nologo >/dev/null
fi
LOGIN_DLL="$ROOT/src/Fineage.Login/bin/$CFG/net10.0/Fineage.Login.dll"
ZONE_DLL="$ROOT/src/Fineage.Zone/bin/$CFG/net10.0/Fineage.Zone.dll"
LOADTEST_DLL="$ROOT/tools/Fineage.LoadTest/bin/$CFG/net10.0/Fineage.LoadTest.dll"
[[ -f "$LOGIN_DLL" ]] || die "missing $LOGIN_DLL"
[[ -f "$ZONE_DLL" ]] || die "missing $ZONE_DLL"

start_container() { # name
  docker run -d --name "$1" --label fineage.role=backup-restore-drill \
    -e POSTGRES_USER=fineage -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB=fineage \
    -p 127.0.0.1::5432 "$PG_IMAGE" >/dev/null
  local i
  for i in $(seq 1 90); do
    docker exec "$1" pg_isready -U fineage -d fineage >/dev/null 2>&1 && return 0
    sleep 1
  done
  docker logs "$1" >&2 || true
  die "PostgreSQL container $1 did not become ready"
}

container_port() { docker port "$1" 5432/tcp | sed 's/.*://'; }

log "creating throwaway source PostgreSQL ($SOURCE_CONTAINER)"
start_container "$SOURCE_CONTAINER"
SRC_PORT="$(container_port "$SOURCE_CONTAINER")"
SRC_URL="Host=127.0.0.1;Port=$SRC_PORT;Database=fineage;Username=fineage;Password=$DB_PASSWORD"

log "applying migrations and creating app-level fixture characters"
FINEAGE_DATABASE_URL="$SRC_URL" dotnet "$LOGIN_DLL" migrate >/dev/null
FINEAGE_DATABASE_URL="$SRC_URL" dotnet "$LOGIN_DLL" --database "$SRC_URL" create drillhero1 DrillPass123 DrillHeroOne fighter
FINEAGE_DATABASE_URL="$SRC_URL" dotnet "$LOGIN_DLL" --database "$SRC_URL" create drillhero2 DrillPass123 DrillHeroTwo mystic

log "seeding synthetic bulk characters/inventory"
psql_exec "$SOURCE_CONTAINER" <<'SQL'
INSERT INTO accounts (name, password_hash, password_salt, iterations)
SELECT 'bulk' || g, 'synthetic', 'synthetic', 1 FROM generate_series(1, 200) g
ON CONFLICT (name) DO NOTHING;
INSERT INTO characters (id, account, name, class_id, race, sex, level, payload)
SELECT 10000 + g, 'bulk' || g, 'Bulk' || g, 0, 0, 0, 1, '{}'::jsonb
FROM generate_series(1, 200) g
ON CONFLICT (id) DO NOTHING;
INSERT INTO character_items (object_id, character_id, item_id, count, enchant_level, loc, loc_data)
SELECT 500000 + g * 10 + i, 10000 + g,
       CASE i % 4 WHEN 0 THEN 57 WHEN 1 THEN 1835 WHEN 2 THEN 1060 ELSE 187 END,
       1 + i, 0, 'inventory', 0
FROM generate_series(1, 200) g, generate_series(1, 10) i
ON CONFLICT (object_id) DO NOTHING;
UPDATE store_meta SET value_bigint = 10201 WHERE key = 'next_character_id';
UPDATE store_meta SET value_bigint = 600000 WHERE key = 'next_item_object_id';
SQL

log "starting source Zone once to persist all eight canonical sidecars"
SRC_ZONE_PORT="$(free_port)"
SRC_STATUS_PORT="$(free_port)"
SRC_METRICS_PORT="$(free_port)"
export FINEAGE_DATABASE_URL="$SRC_URL"
export FINEAGE_HANDOFF_SECRET="drill-$RUN_ID"
export FINEAGE_ZONE_HOST=127.0.0.1
export FINEAGE_GEODATA_REQUIRED=0
export FINEAGE_ZONE_REGION_WORKERS=0
export FINEAGE_ZONE_ADMIN=0
FINEAGE_NET_ZONE_PORT="$SRC_ZONE_PORT" FINEAGE_ZONE_STATUS_PORT="$SRC_STATUS_PORT"
export FINEAGE_NET_ZONE_PORT FINEAGE_ZONE_STATUS_PORT
export FINEAGE_ZONE_METRICS_PORT="$SRC_METRICS_PORT"
start_proc source-zone "$SOURCE_ZONE_LOG" \
    dotnet "$ZONE_DLL" --bind 127.0.0.1 --state-dir "$SOURCE_STATE"
wait_tcp "$SRC_ZONE_PORT" "source Zone" 180
SRC_ZONE_PID="$(cat "$RUN_DIR/source-zone.pid")"
stop_proc "$SRC_ZONE_PID"
for required in boss-schedule.json seven-signs.json olympiad.json lottery.json \
  raid-boss-points-store.json punishments.json monster-race-state.json fishing-championship.json; do
  [[ -f "$SOURCE_STATE/$required" ]] ||
    die "source Zone graceful shutdown did not write $required (see $SOURCE_ZONE_LOG)"
done
log "source sidecars persisted: $(ls "$SOURCE_STATE" | tr '\n' ' ')"

T_BACKUP_START=$SECONDS
log "taking backup"
BACKUP_OUT="$(bash "$ROOT/scripts/backup-state.sh" backup \
  --pg-container "$SOURCE_CONTAINER" --pg-user fineage --pg-db fineage \
  --state-dir "$SOURCE_STATE" --label drill --backup-root "$BACKUP_ROOT")"
BACKUP_DIR="$(printf '%s\n' "$BACKUP_OUT" | sed -n 's/^BACKUP_DIR=//p' | tail -1)"
[[ -d "$BACKUP_DIR" ]] || die "backup did not report a directory"

log "verifying pg_restore --list and sidecar archive"
bash "$ROOT/scripts/backup-state.sh" verify --backup-dir "$BACKUP_DIR"
DUMP_BYTES="$(wc -c <"$BACKUP_DIR/postgres.dump" | tr -d ' ')"
SIDECAR_SHA="$(sha256_file "$BACKUP_DIR/sidecar.tar.gz")"

log "creating fresh restore target ($TARGET_CONTAINER)"
start_container "$TARGET_CONTAINER"
DST_PORT="$(container_port "$TARGET_CONTAINER")"
DST_URL="Host=127.0.0.1;Port=$DST_PORT;Database=fineage;Username=fineage;Password=$DB_PASSWORD"
T_RESTORE_START=$SECONDS
FINEAGE_RESTORE_CONFIRM=disposable-only bash "$ROOT/scripts/backup-state.sh" restore \
  --backup-dir "$BACKUP_DIR" --pg-container "$TARGET_CONTAINER" --pg-user fineage --pg-db fineage \
  --state-dir "$TARGET_STATE"
T_RESTORE_END=$SECONDS

log "checking restored migrations and database equality"
FINEAGE_DATABASE_URL="$DST_URL" dotnet "$LOGIN_DLL" migrate --plan | tee "$RUN_DIR/migrate-plan.txt"
grep -q 'up to date' "$RUN_DIR/migrate-plan.txt" ||
  die "restored database still has pending migrations (see $RUN_DIR/migrate-plan.txt)"

compare_query() { # label sql
  local label="$1" sql="$2" src dst
  src="$(db_digest "$SOURCE_CONTAINER" "$sql")"
  dst="$(db_digest "$TARGET_CONTAINER" "$sql")"
  [[ "$src" == "$dst" ]] || die "$label differs after restore (source=$src restored=$dst)"
  log "$label restored exactly ($src)"
}
compare_query characters \
  "SELECT id,account,name,class_id,race,sex,level,payload::text,updated_at FROM characters ORDER BY id"
compare_query character_items \
  "SELECT object_id,character_id,item_id,count,enchant_level,loc,loc_data,mana_left,time_ms,custom_type1,custom_type2,option1,option2 FROM character_items ORDER BY object_id"
compare_query accounts \
  "SELECT name,coalesce(email,''),password_hash,password_salt,iterations FROM accounts ORDER BY name"
compare_query store_snapshot \
  "SELECT version,payload::text FROM store_snapshot"
compare_query schema_migrations \
  "SELECT id,coalesce(checksum,'') FROM schema_migrations ORDER BY id"
CHARACTER_COUNT="$(psql_exec "$TARGET_CONTAINER" 'SELECT count(*) FROM characters;')"
ITEM_COUNT="$(psql_exec "$TARGET_CONTAINER" 'SELECT count(*) FROM character_items;')"

log "starting Login+Zone against the restored database and sidecars"
LOGIN_PORT="$(free_port)"; HTTP_PORT="$(free_port)"; ZONE_PORT="$(free_port)"
STATUS_PORT="$(free_port)"; LOGIN_METRICS="$(free_port)"; ZONE_METRICS="$(free_port)"
export FINEAGE_DATABASE_URL="$DST_URL"
export FINEAGE_HANDOFF_SECRET="drill-$RUN_ID"
export FINEAGE_ZONE_HOST=127.0.0.1
export FINEAGE_ZONE_PORT="$ZONE_PORT"
export FINEAGE_NET_ZONE_PORT="$ZONE_PORT"
export FINEAGE_NET_LOGIN_PORT="$LOGIN_PORT"
export FINEAGE_LOGIN_HTTP_PORT="$HTTP_PORT"
export FINEAGE_LOGIN_METRICS_PORT="$LOGIN_METRICS"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="drillbot"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="drill-bot-pass"
export FINEAGE_ZONE_STATUS_PORT="$STATUS_PORT"
export FINEAGE_STATE_REQUIRE_EXISTING=1
export FINEAGE_GEODATA_REQUIRED=0
export FINEAGE_ZONE_REGION_WORKERS=0
export FINEAGE_ZONE_ADMIN=0

start_proc login "$LOGIN_LOG" dotnet "$LOGIN_DLL" --bind 127.0.0.1 --http-port "$HTTP_PORT"
export FINEAGE_NET_ZONE_PORT="$ZONE_PORT"
export FINEAGE_ZONE_STATUS_PORT="$STATUS_PORT"
export FINEAGE_ZONE_METRICS_PORT="$ZONE_METRICS"
export FINEAGE_STATE_DIR="$TARGET_STATE"
start_proc zone "$ZONE_LOG" dotnet "$ZONE_DLL" --bind 127.0.0.1 --state-dir "$TARGET_STATE"
wait_tcp "$LOGIN_PORT" "restored Login" 120
wait_tcp "$ZONE_PORT" "restored Zone" 180
T_SERVICES_READY=$SECONDS

log "reading a restored app-created character through Login"
FINEAGE_DATABASE_URL="$DST_URL" dotnet "$LOGIN_DLL" --database "$DST_URL" \
  select drillhero1 DrillPass123 DrillHeroOne >"$RUN_DIR/restored-character.txt"
grep -q 'drillhero1' "$RUN_DIR/restored-character.txt" ||
  die "restored character could not be selected through Login"

WORLD_ENTRY="skipped"
if [[ "$SKIP_SMOKE" != "1" ]]; then
  log "world entry smoke via existing Fineage.LoadTest bots"
  sleep 2
  kill -0 "$(cat "$RUN_DIR/zone.pid")" 2>/dev/null ||
    die "restored Zone exited before the world-entry smoke (tail $ZONE_LOG)"
  port_open "$ZONE_PORT" ||
    die "restored Zone stopped listening on $ZONE_PORT before the world-entry smoke (tail $ZONE_LOG)"
  log "pre-smoke: zone pid=$(cat "$RUN_DIR/zone.pid") listener=$ZONE_PORT login=$LOGIN_PORT"
  command -v lsof >/dev/null 2>&1 && lsof -nP -iTCP:"$ZONE_PORT" -sTCP:LISTEN >"$RUN_DIR/pre-smoke-listeners.txt" 2>&1 || true
  SCENARIO="$RUN_DIR/backup-restore-smoke.yaml"
  cat >"$SCENARIO" <<EOF
name: backup-restore-smoke
extends: $ROOT/tools/Fineage.LoadTest/scenarios/bots-20-smoke.yaml
# Keep the restore check a short, bounded smoke. Omitting these keys inherits the
# 50-bot stress defaults from the transitive stress-mixed-weights.yaml parent.
botCount: 8
rampUpSeconds: 3
durationSeconds: 15
loginPort: $LOGIN_PORT
zonePort: $ZONE_PORT
EOF
  FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="drillbot" \
  FINEAGE_LOGIN_LOADTEST_PASSWORD="drill-bot-pass" \
  FINEAGE_LOGIN_METRICS_PORT="$LOGIN_METRICS" \
  FINEAGE_ZONE_METRICS_PORT="$ZONE_METRICS" \
    dotnet "$LOADTEST_DLL" --scenario "$SCENARIO" --target fineage \
      --report-dir "$RUN_DIR/loadtest" --login-log "$LOGIN_LOG" --zone-log "$ZONE_LOG" \
      --zone-pid "$(cat "$RUN_DIR/zone.pid")" --region-workers 0 >"$RUN_DIR/loadtest-console.txt" 2>&1 ||
    log "load-test exited non-zero; inspecting its report"
  WORLD_ENTRY="$(python3 - "$RUN_DIR/loadtest/report.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    r = json.load(f)
print(f"botsInWorld={r.get('BotsInWorld')} passed={str(r.get('Passed')).lower()} "
      f"fail={'; '.join(r.get('FailReasons') or [])[:300]}")
print(str(r.get('Passed')).lower())
PY
)"
  WORLD_PASSED="$(printf '%s\n' "$WORLD_ENTRY" | tail -1)"
  if [[ "$WORLD_PASSED" != "true" ]]; then
    WORLD_ENTRY="FAILED (acceptance open): $(printf '%s\n' "$WORLD_ENTRY" | head -1)"
    if [[ "$REQUIRE_SMOKE" == "1" ]]; then
      die "world-entry smoke failed: $WORLD_ENTRY"
    fi
    log "world-entry smoke failed; backup/restore result is recorded separately: $WORLD_ENTRY"
  else
    WORLD_ENTRY="$(printf '%s\n' "$WORLD_ENTRY" | head -1)"
  fi
else
  WORLD_ENTRY="skipped by FINEAGE_DRILL_SKIP_SMOKE=1 (acceptance remains open)"
  log "world entry smoke skipped by FINEAGE_DRILL_SKIP_SMOKE=1; acceptance remains open"
fi
WORLD_ENTRY="${WORLD_ENTRY//\"/}"
T_SMOKE_END=$SECONDS

log "checking existing ledger invariant facility"
METRICS_TEXT="$RUN_DIR/zone-metrics.txt"
python3 - "$ZONE_METRICS" "$METRICS_TEXT" <<'PY'
import sys, urllib.request
url = f"http://127.0.0.1:{sys.argv[1]}/metrics"
try:
    text = urllib.request.urlopen(url, timeout=10).read().decode()
except Exception as ex:
    raise SystemExit(f"blocking dependency: zone metrics unavailable: {ex}")
open(sys.argv[2], "w", encoding="utf-8").write(text)
PY
INVARIANTS="$(python3 - "$METRICS_TEXT" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read()
m = re.search(r"^fineage_ledger_invariant_violations_total\s+(\d+)", text, re.M)
if not m:
    raise SystemExit("blocking dependency: existing ledger invariant API not exposed "
                     "(fineage_ledger_invariant_violations_total missing)")
value = int(m.group(1))
print(value)
PY
)" || die "ledger invariant check blocked/failed (see above); zero divergence was NOT claimed"
[[ "$INVARIANTS" == "0" ]] || die "existing ledger invariant checker reports $INVARIANTS violation(s)"
grep -q 'auditable economy ledger invariant' "$ZONE_LOG" &&
  die "zone log contains an existing ledger invariant violation" || true

T_END=$SECONDS
if [[ "$WORLD_ENTRY" == FAILED* || "$WORLD_ENTRY" == skipped* ]]; then
  RESULT="PASS_WITH_OPEN_ACCEPTANCE"
else
  RESULT="PASS"
fi
BACKUP_SECONDS=$((T_RESTORE_START - T_BACKUP_START))
RESTORE_SECONDS=$((T_RESTORE_END - T_RESTORE_START))
RTO_SERVICES_SECONDS=$((T_SERVICES_READY - T_RESTORE_START))
RTO_WITH_SMOKE_SECONDS=$((T_SMOKE_END - T_RESTORE_START))

cat >"$RUN_DIR/report.md" <<EOF
# Backup/restore drill report (#987)

- Run: \`$RUN_ID\`
- Result: **$RESULT**
- Source dump: \`postgres.dump\` $(numfmt --to=iec "$DUMP_BYTES" 2>/dev/null || echo "$DUMP_BYTES bytes")
- Sidecar archive SHA-256: \`$SIDECAR_SHA\`
- Backup dir: \`$BACKUP_DIR\`
- Characters restored: $CHARACTER_COUNT
- Inventory rows restored: $ITEM_COUNT
- Restored migrations: up to date (see \`migrate-plan.txt\`)
- Restored sidecars loaded with \`FINEAGE_STATE_REQUIRE_EXISTING=1\`
- World entry: $WORLD_ENTRY
- Existing ledger invariant metric: \`fineage_ledger_invariant_violations_total $INVARIANTS\`

## Measured times (seconds)

| phase | seconds |
| --- | ---: |
| backup (pg_dump custom + sidecar archive + TOC list) | $BACKUP_SECONDS |
| restore into fresh disposable container + sidecars | $RESTORE_SECONDS |
| restore start → restored Login/Zone listening | $RTO_SERVICES_SECONDS |
| restore start → smoke/checkpoint end | $RTO_WITH_SMOKE_SECONDS |

RPO with the nightly workflow is at most 24 hours (one backup per day; retention
${FINEAGE_BACKUP_RETENTION_DAYS:-14} days or ${FINEAGE_BACKUP_RETENTION_COUNT:-14} copies).
RTO is the measured restore-to-healthy time above, not an estimate.
EOF

cat >"$RUN_DIR/report.json" <<EOF
{
  "run": "$RUN_ID",
  "result": "$RESULT",
  "backupDir": "$BACKUP_DIR",
  "dumpBytes": $DUMP_BYTES,
  "sidecarSha256": "$SIDECAR_SHA",
  "characters": $CHARACTER_COUNT,
  "inventoryRows": $ITEM_COUNT,
  "worldEntry": "$WORLD_ENTRY",
  "ledgerInvariantViolations": $INVARIANTS,
  "backupSeconds": $BACKUP_SECONDS,
  "restoreSeconds": $RESTORE_SECONDS,
  "rtoServicesSeconds": $RTO_SERVICES_SECONDS,
  "rtoWithSmokeSeconds": $RTO_WITH_SMOKE_SECONDS
}
EOF

log "PASS: $RUN_DIR/report.md"
cat "$RUN_DIR/report.md"
