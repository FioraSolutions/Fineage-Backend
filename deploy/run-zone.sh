#!/usr/bin/env bash
# Restart Fineage Zone on the live Pi layout (/home/pi/fineage-backend).
# Public port: TCP 7777 (override with FINEAGE_ZONE_PORT).
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
ZONE_PORT="${FINEAGE_ZONE_PORT:-7777}"
BIND="${FINEAGE_BIND:-0.0.0.0}"

BIN="$ROOT/zone/Fineage.Zone"
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
  --port "$ZONE_PORT" \
  --store "$STORE" \
  >>"$ROOT/logs/zone.log" 2>&1 &
echo $! >"$ROOT/logs/zone.pid"
echo "Fineage.Zone pid $(cat "$ROOT/logs/zone.pid") on ${BIND}:${ZONE_PORT}"
