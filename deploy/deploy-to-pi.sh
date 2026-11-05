#!/usr/bin/env bash
# Sync self-contained publish trees to the live Pi app root and restart via run-*.sh.
# Preserves data/, logs/, fineage.env, and handoff.secret under the app root.
set -euo pipefail

LOGIN_SRC="${1:?login publish directory}"
ZONE_SRC="${2:?zone publish directory}"

PI_USER="${FINEAGE_PI_USER:-pi}"
PI_HOST="${FINEAGE_PI_HOST:-raspberrypi}"
APP_ROOT="${FINEAGE_PI_APP_ROOT:-/home/pi/fineage-backend}"
SSH_KEY="${FINEAGE_PI_SSH_KEY:-$HOME/.ssh/id_ed25519}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -d "$LOGIN_SRC" || ! -d "$ZONE_SRC" ]]; then
  echo "Missing publish directories: $LOGIN_SRC $ZONE_SRC" >&2
  exit 1
fi

SSH=(ssh -i "$SSH_KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new)
RSYNC=(rsync -az --delete -e "ssh -i $SSH_KEY -o BatchMode=yes -o StrictHostKeyChecking=accept-new")

REMOTE="${PI_USER}@${PI_HOST}"
STAGING="/tmp/fineage-deploy-$$"

echo "Deploying to ${REMOTE}:${APP_ROOT} (login/ and zone/ binaries only)…"

"${SSH[@]}" "$REMOTE" "mkdir -p '${STAGING}/login' '${STAGING}/zone' '${APP_ROOT}/login' '${APP_ROOT}/zone' '${APP_ROOT}/logs' '${APP_ROOT}/data'"

"${RSYNC[@]}" "${LOGIN_SRC}/" "${REMOTE}:${STAGING}/login/"
"${RSYNC[@]}" "${ZONE_SRC}/" "${REMOTE}:${STAGING}/zone/"
"${RSYNC[@]}" "${SCRIPT_DIR}/run-login.sh" "${SCRIPT_DIR}/run-zone.sh" "${REMOTE}:${APP_ROOT}/"

"${SSH[@]}" "$REMOTE" bash -s -- "$STAGING" "$APP_ROOT" <<'REMOTE'
set -euo pipefail
STAGING="$1"
APP_ROOT="$2"

rsync -a --delete "${STAGING}/login/" "${APP_ROOT}/login/"
rsync -a --delete "${STAGING}/zone/" "${APP_ROOT}/zone/"
chmod +x "${APP_ROOT}/login/Fineage.Login" "${APP_ROOT}/zone/Fineage.Zone" \
  "${APP_ROOT}/run-login.sh" "${APP_ROOT}/run-zone.sh"
rm -rf "${STAGING}"

cd "${APP_ROOT}"
# run-*.sh source fineage.env in their own process; this shell needs the same overrides or the
# probe below checks the defaults while the servers listen on an operator-pinned port.
if [[ -f "${APP_ROOT}/fineage.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${APP_ROOT}/fineage.env"
  set +a
fi

./run-login.sh
./run-zone.sh

# Net owns the public listeners now: 9106 login, 9777 zone, 5080 login HTTP.
LOGIN_PORT="${FINEAGE_NET_LOGIN_PORT:-9106}"
ZONE_PORT="${FINEAGE_NET_ZONE_PORT:-9777}"
HTTP_PORT="${FINEAGE_LOGIN_HTTP_PORT:-5080}"

deadline=$((SECONDS + 60))
while (( SECONDS < deadline )); do
  ok=true
  for port in "$LOGIN_PORT" "$ZONE_PORT" "$HTTP_PORT"; do
    if ! timeout 2 bash -c "echo >/dev/tcp/127.0.0.1/${port}" 2>/dev/null; then
      ok=false
      break
    fi
  done
  if $ok; then
    echo "Deploy OK: listeners on 127.0.0.1:${LOGIN_PORT} (login Net), ${ZONE_PORT} (zone Net), ${HTTP_PORT} (login HTTP)."
    exit 0
  fi
  sleep 2
done

echo "health check failed: expected listeners on 127.0.0.1:${LOGIN_PORT}, ${ZONE_PORT}, and ${HTTP_PORT}" >&2
echo "--- login.log (tail) ---" >&2
tail -n 40 "${APP_ROOT}/logs/login.log" >&2 || true
echo "--- zone.log (tail) ---" >&2
tail -n 40 "${APP_ROOT}/logs/zone.log" >&2 || true
exit 1
REMOTE
