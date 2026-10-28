#!/usr/bin/env bash
# Restart Fineage Login on the live Pi layout (/home/pi/fineage-backend).
# Public ports: TCP 2106, HTTP 5080 (override with FINEAGE_LOGIN_PORT / FINEAGE_LOGIN_HTTP_PORT).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
mkdir -p logs data

if [[ -f "$ROOT/fineage.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$ROOT/fineage.env"
  set +a
fi

if [[ -z "${FINEAGE_HANDOFF_SECRET:-}" && -f "$ROOT/handoff.secret" ]]; then
  export FINEAGE_HANDOFF_SECRET
  FINEAGE_HANDOFF_SECRET="$(tr -d '\n\r' < "$ROOT/handoff.secret")"
fi

STORE="${FINEAGE_LOGIN_STORE:-$ROOT/data/accounts.json}"
LOGIN_PORT="${FINEAGE_LOGIN_PORT:-2106}"
HTTP_PORT="${FINEAGE_LOGIN_HTTP_PORT:-5080}"
BIND="${FINEAGE_BIND:-0.0.0.0}"

BIN="$ROOT/login/Fineage.Login"
if [[ ! -x "$BIN" ]]; then
  echo "missing executable: $BIN" >&2
  exit 1
fi

if pgrep -f "$BIN" >/dev/null 2>&1; then
  pkill -f "$BIN" || true
  for _ in $(seq 1 20); do
    pgrep -f "$BIN" >/dev/null 2>&1 || break
    sleep 0.5
  done
fi

nohup "$BIN" \
  --bind "$BIND" \
  --port "$LOGIN_PORT" \
  --http-port "$HTTP_PORT" \
  --store "$STORE" \
  >>"$ROOT/logs/login.log" 2>&1 &
echo $! >"$ROOT/logs/login.pid"
echo "Fineage.Login pid $(cat "$ROOT/logs/login.pid") on ${BIND}:${LOGIN_PORT} http ${HTTP_PORT}"
