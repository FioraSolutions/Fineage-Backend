#!/usr/bin/env bash
# Upload self-contained publish trees as a new release on the live host and activate it health-gated (#989).
# Each release lands in <app-root>/releases/<id>/ (never edited in place); deploy/release-switch.sh on the host
# runs the migration preflight, switches the `current` symlink atomically, restarts, waits for /health/ready on
# Login and Zone, and rolls back to the previous release when readiness does not come.
# Preserves data/, logs/, fineage.env, and handoff.secret under the app root.
#
#   FINEAGE_RELEASE_ID   release directory name   (default: UTC timestamp; the workflow passes the commit sha)
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
RELEASE_ID="${FINEAGE_RELEASE_ID:-$(date -u +%Y%m%d%H%M%S)}"
PARTIAL="${APP_ROOT}/releases/${RELEASE_ID}.partial"

echo "Deploying release ${RELEASE_ID} to ${REMOTE}:${APP_ROOT}…"

"${SSH[@]}" "$REMOTE" "mkdir -p '${PARTIAL}/login' '${PARTIAL}/zone' '${APP_ROOT}/logs' '${APP_ROOT}/data'"

"${RSYNC[@]}" "${LOGIN_SRC}/" "${REMOTE}:${PARTIAL}/login/"
"${RSYNC[@]}" "${ZONE_SRC}/" "${REMOTE}:${PARTIAL}/zone/"
"${RSYNC[@]}" "${SCRIPT_DIR}/run-login.sh" "${SCRIPT_DIR}/run-zone.sh" "${SCRIPT_DIR}/release-switch.sh" \
  "${SCRIPT_DIR}/../scripts/fetch-geodata.sh" "${REMOTE}:${APP_ROOT}/"

# The host script owns preflight, the atomic switch, the readiness gate and the rollback; its exit code is the
# deploy result (1 = failed, with production on the previous release when a rollback was possible).
"${SSH[@]}" "$REMOTE" "chmod +x '${APP_ROOT}/run-login.sh' '${APP_ROOT}/run-zone.sh' '${APP_ROOT}/release-switch.sh' '${APP_ROOT}/fetch-geodata.sh' '${PARTIAL}/login/Fineage.Login' '${PARTIAL}/zone/Fineage.Zone' \
  && FINEAGE_RESTART='${RESTART}' FINEAGE_LOGIN_UNIT='${LOGIN_UNIT}' FINEAGE_ZONE_UNIT='${ZONE_UNIT}' '${APP_ROOT}/release-switch.sh' '${APP_ROOT}' '${RELEASE_ID}'"
