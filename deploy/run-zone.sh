#!/usr/bin/env bash
# Restart Fineage Zone on the live Pi layout (/home/pi/fineage-backend).
# Public port: Net TCP 9777 (FINEAGE_NET_ZONE_PORT). The legacy MessagePack V2 listener (--port)
# is only started when FINEAGE_LEGACY_V2_ENABLE=1.
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
BIND="${FINEAGE_BIND:-0.0.0.0}"
LEGACY_V2="${FINEAGE_LEGACY_V2_ENABLE:-0}"
NET_ZONE_PORT="${FINEAGE_NET_ZONE_PORT:-9777}"

# --port is the legacy MessagePack V2 listener only. Net owns NET_ZONE_PORT, so a stale
# FINEAGE_ZONE_PORT=9777 in fineage.env must not be handed to the V2 socket (bind conflict).
ZONE_PORT="${FINEAGE_LEGACY_ZONE_PORT:-${FINEAGE_ZONE_PORT:-9877}}"
if [[ "$ZONE_PORT" == "$NET_ZONE_PORT" ]]; then
  ZONE_PORT=9877
fi

BIN="$ROOT/zone/Fineage.Zone"
if [[ ! -x "$BIN" ]]; then
  echo "missing executable: $BIN" >&2
  exit 1
fi

# Match any running Fineage.Zone (started as ./login/... or absolute path) — an absolute-path
# pgrep missed relative launches, so the old process kept the port and new builds never ran.
PATTERN='(^|/)Fineage.Zone( |$)'
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
  --port "$ZONE_PORT" \
  --store "$STORE" \
  >>"$ROOT/logs/zone.log" 2>&1 &
echo $! >"$ROOT/logs/zone.pid"
echo "Fineage.Zone pid $(cat "$ROOT/logs/zone.pid") net ${BIND}:${NET_ZONE_PORT} legacyV2=${LEGACY_V2}/${ZONE_PORT}"
