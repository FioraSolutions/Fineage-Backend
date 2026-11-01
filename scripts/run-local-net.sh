#!/usr/bin/env bash
# Local Login+Zone: Net on 9106/9777; legacy V2 on 9206/9877; Postgres by default.
# Usage: scripts/run-local-net.sh [start|stop|status]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
mkdir -p local-logs local-data
CMD="${1:-start}"

export FINEAGE_STORE="${FINEAGE_STORE:-postgres}"
export FINEAGE_DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"

stop() {
  for n in login-net zone-net; do
    if [[ -f local-logs/$n.pid ]]; then
      pid="$(cat local-logs/$n.pid)"
      if [[ -n "$pid" ]]; then
        kill -- -"$pid" 2>/dev/null || kill "$pid" 2>/dev/null || true
      fi
      rm -f local-logs/$n.pid
    fi
  done
  pkill -f 'Fineage.Login.dll' 2>/dev/null || true
  pkill -f 'Fineage.Zone.dll' 2>/dev/null || true
  for p in 5080 9106 9206 9777 9877; do
    pids=$(lsof -tiTCP:"$p" -sTCP:LISTEN 2>/dev/null || true)
    if [[ -n "${pids}" ]]; then kill ${pids} 2>/dev/null || true; fi
  done
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
  if [[ "${FINEAGE_STORE}" != "postgres" && "${FINEAGE_STORE}" != "pg" ]]; then
    return 0
  fi
  if ! command -v docker >/dev/null 2>&1; then
    echo "docker not found; start Postgres yourself or FINEAGE_STORE=json" >&2
    return 1
  fi
  docker compose -f "$ROOT/docker-compose.yml" up -d postgres >/dev/null
  for _ in $(seq 1 40); do
    if docker compose -f "$ROOT/docker-compose.yml" exec -T postgres pg_isready -U fineage -d fineage >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.5
  done
  echo "postgres not ready" >&2
  return 1
}

case "$CMD" in
  stop) stop; echo stopped; exit 0;;
  status)
    for p in 9106 9206 9777 9877; do
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
    echo "FINEAGE_STORE=${FINEAGE_STORE}"
    exit 0;;
esac

stop
ensure_postgres
dotnet build src/Fineage.Login -c Debug -v q -nologo >/dev/null
dotnet build src/Fineage.Zone -c Debug -v q -nologo >/dev/null

LOGIN_DLL="$ROOT/src/Fineage.Login/bin/Debug/net10.0/Fineage.Login.dll"
ZONE_DLL="$ROOT/src/Fineage.Zone/bin/Debug/net10.0/Fineage.Zone.dll"
if [[ ! -f "$LOGIN_DLL" || ! -f "$ZONE_DLL" ]]; then
  echo "missing build outputs under bin/Debug/net10.0" >&2
  exit 1
fi

export FINEAGE_NET_ENABLE=1
export FINEAGE_NET_LOGIN_PORT=9106
export FINEAGE_NET_ZONE_PORT=9777
export FINEAGE_LOGIN_PORT=9206
export FINEAGE_ZONE_PORT=9877
export FINEAGE_ZONE_STATUS_PORT=0
export FINEAGE_ZONE_HOST=127.0.0.1

daemonize login-net \
  dotnet "$LOGIN_DLL" --bind 127.0.0.1 --port 9206 --http-port 5080 --store local-data/accounts.json

daemonize zone-net \
  dotnet "$ZONE_DLL" --bind 127.0.0.1 --port 9877 --store local-data/accounts.json

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
echo "Net login=9106 zone=9777 | store=${FINEAGE_STORE} | V2 login=9206 zone=9877"
echo "stop: ./scripts/run-local-net.sh stop"
