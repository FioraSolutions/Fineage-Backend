#!/usr/bin/env bash
# Local Login+Zone SoT (Net only). PostgreSQL via docker compose.
# Usage: ./scripts/run-local-net.sh [start|stop|status]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
mkdir -p local-logs local-data
CMD="${1:-start}"

export FINEAGE_DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"
# Hosted default is every enabled spawn map. Leave INCLUDE unset (or 1). Pass FINEAGE_ZONE_MAPS /
# FINEAGE_ZONE_BOUNDS only to shard a region; INCLUDE=0 is a single-map test escape hatch.
export FINEAGE_ZONE_MAPS="${FINEAGE_ZONE_MAPS-}"
export FINEAGE_ZONE_BOUNDS="${FINEAGE_ZONE_BOUNDS-}"
# Visibility must be spatial for ~34k NPCs (Zone defaults AOI_CELL to 3000 if unset).
export FINEAGE_ZONE_AOI_CELL="${FINEAGE_ZONE_AOI_CELL:-3000}"
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-fineage-backend}"
# Non-loopback binds refuse DefaultSecret; local SoT binds 0.0.0.0 so set the shared secret.
export FINEAGE_HANDOFF_SECRET="${FINEAGE_HANDOFF_SECRET:-FineageLoginHandoffV1}"

PORTS=(5080 9106 9777 7780)

bootout_launch_agents() {
  local uid
  uid="$(id -u)"
  for label in local.fineage.login local.fineage.zone; do
    launchctl bootout "gui/${uid}/${label}" 2>/dev/null || true
  done
}

kill_ports() {
  local p pids
  for p in "${PORTS[@]}"; do
    pids=$(lsof -tiTCP:"$p" -sTCP:LISTEN 2>/dev/null || true)
    if [[ -n "${pids}" ]]; then kill ${pids} 2>/dev/null || true; fi
  done
  for _ in $(seq 1 10); do
    local busy=0
    for p in "${PORTS[@]}"; do
      if [[ -n "$(lsof -tiTCP:"$p" -sTCP:LISTEN 2>/dev/null || true)" ]]; then busy=1; fi
    done
    [[ "$busy" == 0 ]] && break
    sleep 0.5
  done
  for p in "${PORTS[@]}"; do
    pids=$(lsof -tiTCP:"$p" -sTCP:LISTEN 2>/dev/null || true)
    if [[ -n "${pids}" ]]; then kill -9 ${pids} 2>/dev/null || true; fi
  done
}

stop() {
  bootout_launch_agents
  for n in login-net zone-net; do
    if [[ -f local-logs/$n.pid ]]; then
      pid="$(cat local-logs/$n.pid)"
      if [[ -n "$pid" ]]; then
        kill -- -"$pid" 2>/dev/null || kill "$pid" 2>/dev/null || true
      fi
      rm -f local-logs/$n.pid
    fi
  done
  # Match both `dotnet …/Fineage.Login.dll` and the apphost `…/Fineage.Login`.
  pkill -f 'Fineage\.Login(\.dll)?( |$)' 2>/dev/null || true
  pkill -f 'Fineage\.Zone(\.dll)?( |$)' 2>/dev/null || true
  kill_ports
  sleep 0.5
}

port_open() {
  python3 - "$1" <<'PY'
import socket, sys
p=int(sys.argv[1]); s=socket.socket(); s.settimeout(1)
try:
  raise SystemExit(0 if s.connect_ex(("127.0.0.1", p))==0 else 1)
finally:
  s.close()
PY
}

postgres_tcp_ready() {
  python3 - <<'PY'
import socket
s=socket.socket(); s.settimeout(1)
try:
  raise SystemExit(0 if s.connect_ex(("127.0.0.1", 5432))==0 else 1)
finally:
  s.close()
PY
}

daemonize() {
  local name=$1; shift
  local logfile="local-logs/${name}.log"
  local pidfile="local-logs/${name}.pid"
  : > "$logfile"
  python3 - "$ROOT" "$pidfile" "$logfile" "$@" <<'PY'
import os, sys, subprocess
root, pidfile, logfile, *cmd = sys.argv[1:]
os.chdir(root)
with open(logfile, "ab", buffering=0) as log:
    p = subprocess.Popen(
        cmd,
        stdin=subprocess.DEVNULL,
        stdout=log,
        stderr=subprocess.STDOUT,
        start_new_session=True,
        cwd=root,
        env=os.environ.copy(),
    )
with open(pidfile, "w", encoding="utf-8") as f:
    f.write(str(p.pid))
print(p.pid)
PY
}

ensure_postgres() {
  if postgres_tcp_ready; then
    return 0
  fi
  if ! command -v docker >/dev/null 2>&1; then
    echo "docker not found; PostgreSQL is required on 127.0.0.1:5432 (docker compose up -d postgres)" >&2
    return 1
  fi
  # Don't hang forever if Docker Desktop is wedged.
  if command -v timeout >/dev/null 2>&1; then
    timeout 30 docker compose -f "$ROOT/docker-compose.yml" up -d postgres >/dev/null \
      || echo "warning: docker compose up timed out or failed; checking 5432 anyway" >&2
  else
    docker compose -f "$ROOT/docker-compose.yml" up -d postgres >/dev/null || true
  fi
  for _ in $(seq 1 40); do
    if postgres_tcp_ready; then
      return 0
    fi
    sleep 0.5
  done
  echo "postgres not ready on 127.0.0.1:5432" >&2
  return 1
}

launch_agents_loaded() {
  local uid
  uid="$(id -u)"
  launchctl print "gui/${uid}/local.fineage.login" >/dev/null 2>&1 \
    || launchctl print "gui/${uid}/local.fineage.zone" >/dev/null 2>&1
}

case "$CMD" in
  stop) stop; echo stopped; exit 0;;
  status)
    for p in 9106 9777 5080; do
      if port_open "$p"; then echo "$p open"; else echo "$p closed"; fi
    done
    for n in login-net zone-net; do
      if [[ -f local-logs/$n.pid ]]; then
        pid=$(cat local-logs/$n.pid)
        if kill -0 "$pid" 2>/dev/null; then echo "$n pid=$pid alive"; else echo "$n pid=$pid dead"; fi
      else
        echo "$n no-pid"
      fi
    done
    if launch_agents_loaded; then
      echo "warn: LaunchAgents local.fineage.* still loaded (run stop to bootout)"
    fi
    echo "store=postgres"
    exit 0;;
esac

stop
ensure_postgres
dotnet build src/Fineage.Login -c Debug -v q -nologo >/dev/null
dotnet build src/Fineage.Zone -c Debug -v q -nologo >/dev/null

LOGIN_BIN="$ROOT/src/Fineage.Login/bin/Debug/net10.0/Fineage.Login"
ZONE_BIN="$ROOT/src/Fineage.Zone/bin/Debug/net10.0/Fineage.Zone"
LOGIN_DLL="$ROOT/src/Fineage.Login/bin/Debug/net10.0/Fineage.Login.dll"
ZONE_DLL="$ROOT/src/Fineage.Zone/bin/Debug/net10.0/Fineage.Zone.dll"
if [[ -x "$LOGIN_BIN" && -x "$ZONE_BIN" ]]; then
  LOGIN_CMD=("$LOGIN_BIN")
  ZONE_CMD=("$ZONE_BIN")
elif [[ -f "$LOGIN_DLL" && -f "$ZONE_DLL" ]]; then
  LOGIN_CMD=(dotnet "$LOGIN_DLL")
  ZONE_CMD=(dotnet "$ZONE_DLL")
else
  echo "missing build outputs under bin/Debug/net10.0" >&2
  exit 1
fi

export FINEAGE_NET_LOGIN_PORT=9106
export FINEAGE_NET_ZONE_PORT=9777
# Loopback /status so Login's /api/status can report the zone as up.
export FINEAGE_ZONE_STATUS_PORT="${FINEAGE_ZONE_STATUS_PORT:-7780}"
export FINEAGE_ZONE_HOST=127.0.0.1

# World sidecars (seven-signs.json, boss-schedule.json, olympiad.json) live in local-data.
daemonize login-net \
  "${LOGIN_CMD[@]}" --bind 0.0.0.0 --http-port 5080

daemonize zone-net \
  "${ZONE_CMD[@]}" --bind 0.0.0.0 --port 9777 --state-dir local-data

ok=0
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 15; do
  sleep 1
  if port_open 9106 && port_open 9777; then ok=1; break; fi
done
if [[ "$ok" != 1 ]]; then
  echo "not listening, see local-logs/*-net.log" >&2
  ./scripts/run-local-net.sh status || true
  tail -40 local-logs/login-net.log 2>/dev/null || true
  tail -40 local-logs/zone-net.log 2>/dev/null || true
  exit 1
fi
echo "Net login=9106 zone=9777 http=5080 | store=postgres | maps=${FINEAGE_ZONE_MAPS:-default}"
echo "stop: ./scripts/run-local-net.sh stop"
