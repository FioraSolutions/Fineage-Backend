#!/usr/bin/env bash
# Sync self-contained publish trees to the Pi and restart systemd units.
# Does not modify /etc/fineage.env or /var/lib/fineage/accounts.json.
set -euo pipefail

LOGIN_SRC="${1:?login publish directory}"
ZONE_SRC="${2:?zone publish directory}"

PI_USER="${FINEAGE_PI_USER:-pi}"
PI_HOST="${FINEAGE_PI_HOST:-raspberrypi}"
SSH_KEY="${FINEAGE_PI_SSH_KEY:-$HOME/.ssh/id_ed25519}"

if [[ ! -d "$LOGIN_SRC" || ! -d "$ZONE_SRC" ]]; then
  echo "Missing publish directories: $LOGIN_SRC $ZONE_SRC" >&2
  exit 1
fi

SSH=(ssh -i "$SSH_KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new)
RSYNC=(rsync -az --delete -e "ssh -i $SSH_KEY -o BatchMode=yes -o StrictHostKeyChecking=accept-new")

REMOTE="${PI_USER}@${PI_HOST}"
STAGING="/tmp/fineage-deploy-$$"

echo "Deploying to ${REMOTE} (binaries only under /opt/fineage)…"

"${SSH[@]}" "$REMOTE" "mkdir -p '${STAGING}/login' '${STAGING}/zone'"

"${RSYNC[@]}" "${LOGIN_SRC}/" "${REMOTE}:${STAGING}/login/"
"${RSYNC[@]}" "${ZONE_SRC}/" "${REMOTE}:${STAGING}/zone/"

"${SSH[@]}" "$REMOTE" bash -s -- "$STAGING" <<'REMOTE'
set -euo pipefail
STAGING="$1"

sudo install -d -o fineage -g fineage /opt/fineage/login /opt/fineage/zone
sudo rsync -a --delete "${STAGING}/login/" /opt/fineage/login/
sudo rsync -a --delete "${STAGING}/zone/" /opt/fineage/zone/
sudo chown -R fineage:fineage /opt/fineage/login /opt/fineage/zone
rm -rf "${STAGING}"

sudo systemctl restart fineage-login fineage-zone

deadline=$((SECONDS + 45))
while (( SECONDS < deadline )); do
  if systemctl is-active --quiet fineage-login && systemctl is-active --quiet fineage-zone; then
    break
  fi
  sleep 1
done

if ! systemctl is-active --quiet fineage-login; then
  echo "fineage-login not active:" >&2
  systemctl status fineage-login --no-pager >&2 || true
  journalctl -u fineage-login -n 30 --no-pager >&2 || true
  exit 1
fi

if ! systemctl is-active --quiet fineage-zone; then
  echo "fineage-zone not active:" >&2
  systemctl status fineage-zone --no-pager >&2 || true
  journalctl -u fineage-zone -n 30 --no-pager >&2 || true
  exit 1
fi

for port in 5080 9106 9777; do
  if ! timeout 3 bash -c "echo >/dev/tcp/127.0.0.1/${port}" 2>/dev/null; then
    echo "health check failed: nothing listening on 127.0.0.1:${port}" >&2
    exit 1
  fi
done

echo "Deploy OK: fineage-login and fineage-zone active; ports 5080, 9106, 9777 listening."
REMOTE
