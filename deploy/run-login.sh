#!/usr/bin/env bash
# Restart Fineage Login on the live Pi layout (/home/pi/fineage-backend).
# Public ports: Net TCP 9106 (FINEAGE_NET_LOGIN_PORT), HTTP 5080 (FINEAGE_LOGIN_HTTP_PORT).
# The legacy MessagePack V2 listener (--port) is only started when FINEAGE_LEGACY_V2_ENABLE=1.
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
HTTP_PORT="${FINEAGE_LOGIN_HTTP_PORT:-5080}"
BIND="${FINEAGE_BIND:-0.0.0.0}"
LEGACY_V2="${FINEAGE_LEGACY_V2_ENABLE:-0}"
NET_LOGIN_PORT="${FINEAGE_NET_LOGIN_PORT:-9106}"

# --port is the legacy MessagePack V2 listener only. Net owns NET_LOGIN_PORT, so a stale
# FINEAGE_LOGIN_PORT=9106 in fineage.env must not be handed to the V2 socket (bind conflict).
LOGIN_PORT="${FINEAGE_LEGACY_LOGIN_PORT:-${FINEAGE_LOGIN_PORT:-9206}}"
if [[ "$LOGIN_PORT" == "$NET_LOGIN_PORT" ]]; then
  LOGIN_PORT=9206
fi

BIN="$ROOT/login/Fineage.Login"
if [[ ! -x "$BIN" ]]; then
  echo "missing executable: $BIN" >&2
  exit 1
fi

# Match any running Fineage.Login (started as ./login/... or absolute path) — an absolute-path
# pgrep missed relative launches, so the old process kept the port and new builds never ran.
PATTERN='(^|/)Fineage.Login( |$)'
if pgrep -f "$PATTERN" >/dev/null 2>&1; then
  pkill -f "$PATTERN" || true
  for _ in $(seq 1 20); do
    pgrep -f "$PATTERN" >/dev/null 2>&1 || break
    sleep 0.5
  done
  pkill -9 -f "$PATTERN" 2>/dev/null || true
fi

nohup "$BIN" \
  --bind "$BIND" \
  --port "$LOGIN_PORT" \
  --http-port "$HTTP_PORT" \
  --store "$STORE" \
  >>"$ROOT/logs/login.log" 2>&1 &
echo $! >"$ROOT/logs/login.pid"
echo "Fineage.Login pid $(cat "$ROOT/logs/login.pid") net ${BIND}:${NET_LOGIN_PORT} http ${HTTP_PORT} legacyV2=${LEGACY_V2}/${LOGIN_PORT}"
