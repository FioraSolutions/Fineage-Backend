#!/usr/bin/env bash
# Stage self-contained publish trees on the live host and hand them to release-manager.sh.
# The manager keeps releases/<id>/ with atomic current/previous symlinks, runs the migration
# --plan preflight, restarts systemd, waits for real Login/Zone readiness, runs the world-entry
# smoke, and rolls back to the previous known-good release on any failure.
#
# Defaults target the live homeserver. Override with:
#   FINEAGE_DEPLOY_USER  ssh user            (default: nikita)
#   FINEAGE_HOST         ssh host            (default: homeserver)
#   FINEAGE_APP_ROOT     artifact root       (default: /home/nikita/fineage-backend/deploy)
#   FINEAGE_SSH_KEY      private key path    (default: ~/.ssh/id_ed25519)
#   FINEAGE_RELEASE_ID   release directory   (default: UTC timestamp; CI sets the commit sha)
#   FINEAGE_RESTART      systemd|scripts     (default: systemd)
#   FINEAGE_DEPLOY_SMOKE auto|skip           (default: auto; skip must be explicit)
set -euo pipefail

LOGIN_SRC="${1:?login publish directory}"
ZONE_SRC="${2:?zone publish directory}"
SMOKE_SRC="${3:-}"

DEPLOY_USER="${FINEAGE_DEPLOY_USER:-nikita}"
HOST="${FINEAGE_HOST:-homeserver}"
APP_ROOT="${FINEAGE_APP_ROOT:-/home/nikita/fineage-backend/deploy}"
SSH_KEY="${FINEAGE_SSH_KEY:-$HOME/.ssh/id_ed25519}"
RESTART="${FINEAGE_RESTART:-systemd}"
RELEASE_ID="${FINEAGE_RELEASE_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
SMOKE_MODE="${FINEAGE_DEPLOY_SMOKE:-auto}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -d "$LOGIN_SRC" || ! -d "$ZONE_SRC" ]]; then
  echo "Missing publish directories: $LOGIN_SRC $ZONE_SRC" >&2
  exit 1
fi
if [[ "$SMOKE_MODE" != "skip" && ! -d "$SMOKE_SRC" ]]; then
  echo "Missing LoadTest smoke publish directory (third argument); pass it or set FINEAGE_DEPLOY_SMOKE=skip explicitly." >&2
  exit 1
fi

SSH=(ssh -i "$SSH_KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new)
RSYNC=(rsync -az -e "ssh -i $SSH_KEY -o BatchMode=yes -o StrictHostKeyChecking=accept-new")

REMOTE="${DEPLOY_USER}@${HOST}"
STAGING="${APP_ROOT}/releases/.staging-${RELEASE_ID}-$$"

echo "Deploying release ${RELEASE_ID} to ${REMOTE}:${APP_ROOT} (login, zone, smoke)…"

"${SSH[@]}" "$REMOTE" "mkdir -p '${STAGING}/login' '${STAGING}/zone' '${APP_ROOT}/releases' '${APP_ROOT}/logs' '${APP_ROOT}/data'"

"${RSYNC[@]}" "${LOGIN_SRC}/" "${REMOTE}:${STAGING}/login/"
"${RSYNC[@]}" "${ZONE_SRC}/" "${REMOTE}:${STAGING}/zone/"
if [[ -n "$SMOKE_SRC" ]]; then
  "${SSH[@]}" "$REMOTE" "mkdir -p '${STAGING}/smoke'"
  "${RSYNC[@]}" "${SMOKE_SRC}/" "${REMOTE}:${STAGING}/smoke/"
fi
"${RSYNC[@]}" "${SCRIPT_DIR}/release-manager.sh" "${SCRIPT_DIR}/run-login.sh" "${SCRIPT_DIR}/run-zone.sh" \
  "${SCRIPT_DIR}/../scripts/fetch-geodata.sh" "${REMOTE}:${APP_ROOT}/"

if ! "${SSH[@]}" "$REMOTE" \
  "chmod +x '${APP_ROOT}/release-manager.sh' '${APP_ROOT}/run-login.sh' '${APP_ROOT}/run-zone.sh' '${APP_ROOT}/fetch-geodata.sh' '${STAGING}/login/Fineage.Login' '${STAGING}/zone/Fineage.Zone'; \
   FINEAGE_DEPLOY_SMOKE='${SMOKE_MODE}' \
   FINEAGE_DEPLOY_READY_TIMEOUT='${FINEAGE_DEPLOY_READY_TIMEOUT:-90}' \
   FINEAGE_DEPLOY_SMOKE_ACCOUNT_PREFIX='${FINEAGE_DEPLOY_SMOKE_ACCOUNT_PREFIX:-deploysmoke}' \
   bash '${APP_ROOT}/release-manager.sh' deploy \
     --app-root '${APP_ROOT}' \
     --staging '${STAGING}' \
     --release-id '${RELEASE_ID}' \
     --restart '${RESTART}'"; then
  echo "Deploy failed; release-manager rollback attempted. Inspect ${APP_ROOT}/last-deploy.json and ${APP_ROOT}/logs/." >&2
  exit 1
fi
