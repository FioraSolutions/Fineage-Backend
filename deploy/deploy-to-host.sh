#!/usr/bin/env bash
# Sync self-contained publish trees to the live host app root and restart it via systemd.
# Preserves data/, logs/, fineage.env, and handoff.secret under the app root.
#
# Defaults target the live homeserver. Override with:
#   FINEAGE_DEPLOY_USER  ssh user            (default: nikita)
#   FINEAGE_HOST         ssh host            (default: homeserver)
#   FINEAGE_APP_ROOT     artifact root       (default: /home/nikita/fineage-backend/deploy)
#   FINEAGE_SSH_KEY      private key path    (default: ~/.ssh/id_ed25519)
#   FINEAGE_RESTART      systemd|scripts     (default: systemd)
#   FINEAGE_LOGIN_UNIT / FINEAGE_ZONE_UNIT   (default: fineage-login / fineage-zone)
set -euo pipefail

LOGIN_SRC="${1:?login publish directory}"
ZONE_SRC="${2:?zone publish directory}"

DEPLOY_USER="${FINEAGE_DEPLOY_USER:-nikita}"
HOST="${FINEAGE_HOST:-homeserver}"
APP_ROOT="${FINEAGE_APP_ROOT:-/home/nikita/fineage-backend/deploy}"
SSH_KEY="${FINEAGE_SSH_KEY:-$HOME/.ssh/id_ed25519}"
RESTART="${FINEAGE_RESTART:-systemd}"
LOGIN_UNIT="${FINEAGE_LOGIN_UNIT:-fineage-login}"
ZONE_UNIT="${FINEAGE_ZONE_UNIT:-fineage-zone}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -d "$LOGIN_SRC" || ! -d "$ZONE_SRC" ]]; then
  echo "Missing publish directories: $LOGIN_SRC $ZONE_SRC" >&2
  exit 1
fi

SSH=(ssh -i "$SSH_KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new)
RSYNC=(rsync -az --delete -e "ssh -i $SSH_KEY -o BatchMode=yes -o StrictHostKeyChecking=accept-new")

REMOTE="${DEPLOY_USER}@${HOST}"
STAGING="/tmp/fineage-deploy-$$"

echo "Deploying to ${REMOTE}:${APP_ROOT} (login/ and zone/ binaries only)…"

"${SSH[@]}" "$REMOTE" "mkdir -p '${STAGING}/login' '${STAGING}/zone' '${APP_ROOT}/login' '${APP_ROOT}/zone' '${APP_ROOT}/logs' '${APP_ROOT}/data'"

"${RSYNC[@]}" "${LOGIN_SRC}/" "${REMOTE}:${STAGING}/login/"
"${RSYNC[@]}" "${ZONE_SRC}/" "${REMOTE}:${STAGING}/zone/"
"${RSYNC[@]}" "${SCRIPT_DIR}/run-login.sh" "${SCRIPT_DIR}/run-zone.sh" "${REMOTE}:${APP_ROOT}/"

"${SSH[@]}" "$REMOTE" bash -s -- "$STAGING" "$APP_ROOT" "$RESTART" "$LOGIN_UNIT" "$ZONE_UNIT" <<'REMOTE'
set -euo pipefail
STAGING="$1"
APP_ROOT="$2"
RESTART="$3"
LOGIN_UNIT="$4"
ZONE_UNIT="$5"

# Binaries are replaced while the processes are down; systemd units point at these paths.
rsync -a --delete "${STAGING}/login/" "${APP_ROOT}/login/"
rsync -a --delete "${STAGING}/zone/" "${APP_ROOT}/zone/"
chmod +x "${APP_ROOT}/login/Fineage.Login" "${APP_ROOT}/zone/Fineage.Zone" \
  "${APP_ROOT}/run-login.sh" "${APP_ROOT}/run-zone.sh"
rm -rf "${STAGING}"

if [[ "$RESTART" == "systemd" ]]; then
  # Units are the source of truth on this host; run-*.sh would fight them over the ports.
  sudo systemctl restart "$LOGIN_UNIT" "$ZONE_UNIT"
else
  cd "${APP_ROOT}"
  ./run-login.sh
  ./run-zone.sh
fi

# This shell needs the operator's port overrides, or the probe checks defaults.
if [[ -f "${APP_ROOT}/fineage.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${APP_ROOT}/fineage.env"
  set +a
fi

# Net owns the client listeners: login TCP + HTTP, zone TCP.
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
if [[ "$RESTART" == "systemd" ]]; then
  sudo journalctl -u "$LOGIN_UNIT" -n 40 --no-pager >&2 || true
  sudo journalctl -u "$ZONE_UNIT" -n 40 --no-pager >&2 || true
else
  echo "--- login.log (tail) ---" >&2
  tail -n 40 "${APP_ROOT}/logs/login.log" >&2 || true
  echo "--- zone.log (tail) ---" >&2
  tail -n 40 "${APP_ROOT}/logs/zone.log" >&2 || true
fi
exit 1
REMOTE
