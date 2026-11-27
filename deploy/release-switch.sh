#!/usr/bin/env bash
# Host side of a deploy (#989): activate one uploaded release, wait for readiness, roll back atomically on failure.
#
#   release-switch.sh <app-root> <release-id>
#
# Layout under <app-root>:
#   releases/<id>/login, releases/<id>/zone   one directory per release (never edited after upload)
#   current -> releases/<id>                  the live release; switched with an atomic rename(2)
#   login -> current/login, zone -> current/zone
#                                             the paths systemd units and run-*.sh already use
#   data/ logs/ fineage.env handoff.secret    shared, never inside a release
#
# The release must already be uploaded to releases/<id>.partial/ (deploy-to-host.sh does that); this script
# renames it to releases/<id>, runs the read-only migration preflight from it, switches `current`, restarts,
# and waits until every readiness URL answers 200 and every TCP port accepts. If that does not happen within
# FINEAGE_READY_TIMEOUT seconds, `current` is switched back to the previous release, the services are restarted
# on it and its readiness is awaited too; the script then exits 1 (the deploy failed, production is on the old
# release). A release that applied forward-only migrations cannot be rolled back by binaries alone: the script
# refuses the binary rollback and says so (restore with the backup drill or fix forward).
#
# Environment (defaults match the live host):
#   FINEAGE_RESTART          systemd|scripts|command  (default systemd)
#   FINEAGE_RESTART_COMMAND  used when FINEAGE_RESTART=command (tests)
#   FINEAGE_LOGIN_UNIT / FINEAGE_ZONE_UNIT           (default fineage-login / fineage-zone)
#   FINEAGE_READY_TIMEOUT    seconds to wait for readiness (default 240)
#   FINEAGE_READY_URLS       space-separated URLs that must return 200
#                            (default login http://127.0.0.1:$FINEAGE_LOGIN_HTTP_PORT/health/ready and
#                             zone http://127.0.0.1:$FINEAGE_ZONE_STATUS_PORT/health/ready)
#   FINEAGE_READY_TCP_PORTS  space-separated loopback ports that must accept (default the three client ports)
#   FINEAGE_KEEP_RELEASES    releases kept on disk (default 5; current and previous are never pruned)
#   FINEAGE_SKIP_PREFLIGHT=1 skip `Fineage.Login migrate --plan` (tests only)
#   FINEAGE_SMOKE_USER / FINEAGE_SMOKE_PASSWORD
#                            when both are set (in fineage.env), POST /api/auth/login with them must succeed
set -euo pipefail

APP_ROOT="${1:?app root}"
RELEASE_ID="${2:?release id}"
[[ "$RELEASE_ID" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "release id '$RELEASE_ID' has characters outside [A-Za-z0-9._-]" >&2; exit 1; }

cd "$APP_ROOT"
if [[ -f fineage.env ]]; then
  set -a
  # shellcheck disable=SC1091
  source fineage.env
  set +a
fi

RESTART="${FINEAGE_RESTART:-systemd}"
LOGIN_UNIT="${FINEAGE_LOGIN_UNIT:-fineage-login}"
ZONE_UNIT="${FINEAGE_ZONE_UNIT:-fineage-zone}"
READY_TIMEOUT="${FINEAGE_READY_TIMEOUT:-240}"
KEEP="${FINEAGE_KEEP_RELEASES:-5}"
HTTP_PORT="${FINEAGE_LOGIN_HTTP_PORT:-5080}"
ZONE_STATUS_PORT="${FINEAGE_ZONE_STATUS_PORT:-7780}"
READY_URLS="${FINEAGE_READY_URLS:-http://127.0.0.1:${HTTP_PORT}/health/ready http://127.0.0.1:${ZONE_STATUS_PORT}/health/ready}"
READY_TCP_PORTS="${FINEAGE_READY_TCP_PORTS:-${FINEAGE_NET_LOGIN_PORT:-9106} ${FINEAGE_NET_ZONE_PORT:-9777} ${HTTP_PORT}}"

log() { echo "[release] $*"; }
fail() { echo "[release] $*" >&2; }

mkdir -p releases logs data

# --- 1. one-time adoption of the old in-place layout -------------------------------------------------------
# Before #989 login/ and zone/ were real directories replaced by rsync --delete. Keep them as the first
# release so the very first health-gated deploy already has something to roll back to.
if [[ ! -L current && ( -d login || -d zone ) && ! -L login ]]; then
  legacy="legacy-$(date -u +%Y%m%d%H%M%S)"
  log "adopting the in-place layout as releases/${legacy}"
  mkdir -p "releases/${legacy}"
  [[ -d login ]] && mv login "releases/${legacy}/login"
  [[ -d zone ]] && mv zone "releases/${legacy}/zone"
  ln -sfn "releases/${legacy}" current
fi
for part in login zone; do
  if [[ -e "$part" && ! -L "$part" ]]; then
    fail "$APP_ROOT/$part is a real directory next to a release layout; move it away by hand"
    exit 1
  fi
  ln -sfn "current/${part}" "$part"
done

PREVIOUS=""
if [[ -L current ]]; then
  PREVIOUS="$(readlink current)"
  PREVIOUS="${PREVIOUS#releases/}"
fi

# --- 2. finalize the upload ---------------------------------------------------------------------------------
if [[ -d "releases/${RELEASE_ID}.partial" ]]; then
  rm -rf "releases/${RELEASE_ID}"
  mv "releases/${RELEASE_ID}.partial" "releases/${RELEASE_ID}"
fi
if [[ ! -x "releases/${RELEASE_ID}/login/Fineage.Login" || ! -x "releases/${RELEASE_ID}/zone/Fineage.Zone" ]]; then
  chmod +x "releases/${RELEASE_ID}/login/Fineage.Login" "releases/${RELEASE_ID}/zone/Fineage.Zone" 2>/dev/null || true
fi
for binary in "releases/${RELEASE_ID}/login/Fineage.Login" "releases/${RELEASE_ID}/zone/Fineage.Zone"; do
  [[ -x "$binary" ]] || { fail "release ${RELEASE_ID} is missing $binary"; exit 1; }
done
if [[ "$PREVIOUS" == "$RELEASE_ID" ]]; then
  log "release ${RELEASE_ID} is already current; restarting it under the same readiness gate"
fi

# --- 3. read-only migration preflight from the new release --------------------------------------------------
PENDING_MIGRATIONS=0
if [[ "${FINEAGE_SKIP_PREFLIGHT:-0}" != "1" ]]; then
  if ! plan="$("releases/${RELEASE_ID}/login/Fineage.Login" migrate --plan 2>&1)"; then
    echo "$plan" >&2
    fail "migration preflight failed; current release (${PREVIOUS:-none}) was not touched"
    exit 1
  fi
  echo "$plan"
  # `migrations: applied=N pending=M unverified=K` (Cli.ReportPlan).
  PENDING_MIGRATIONS="$(sed -n 's/.*migrations: applied=[0-9]* pending=\([0-9]*\).*/\1/p' <<<"$plan" | head -n1)"
  [[ "$PENDING_MIGRATIONS" =~ ^[0-9]+$ ]] || { fail "could not read the pending count from the migration plan"; exit 1; }
fi

switch_to() {
  # Atomic: rename(2) of a fresh symlink over `current`; readers see the old or the new release, never neither.
  ln -sfn "releases/$1" current.next
  mv -T current.next current
  log "current -> releases/$1"
}

restart() {
  case "$RESTART" in
    systemd) sudo systemctl restart "$LOGIN_UNIT" "$ZONE_UNIT" ;;
    scripts) ./run-login.sh && ./run-zone.sh ;;
    command) bash -c "${FINEAGE_RESTART_COMMAND:?FINEAGE_RESTART_COMMAND is required for FINEAGE_RESTART=command}" ;;
    *) fail "FINEAGE_RESTART=$RESTART is not systemd|scripts|command"; return 1 ;;
  esac
}

LAST_FAILURE=""
probe_once() {
  local url port body code
  for port in $READY_TCP_PORTS; do
    if ! timeout 2 bash -c "echo >/dev/tcp/127.0.0.1/${port}" 2>/dev/null; then
      LAST_FAILURE="tcp 127.0.0.1:${port} refused"
      return 1
    fi
  done
  for url in $READY_URLS; do
    body="$(mktemp)"
    code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 "$url" || true)"
    if [[ "$code" != "200" ]]; then
      LAST_FAILURE="${url} -> ${code:-no answer}: $(head -c 600 "$body" 2>/dev/null)"
      rm -f "$body"
      return 1
    fi
    rm -f "$body"
  done
  if [[ -n "${FINEAGE_SMOKE_USER:-}" && -n "${FINEAGE_SMOKE_PASSWORD:-}" ]]; then
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 -H 'Content-Type: application/json' \
      --data "$(printf '{"username":"%s","password":"%s"}' "$FINEAGE_SMOKE_USER" "$FINEAGE_SMOKE_PASSWORD")" \
      "http://127.0.0.1:${HTTP_PORT}/api/auth/login" || true)"
    if [[ "$code" != "200" ]]; then
      LAST_FAILURE="smoke login as ${FINEAGE_SMOKE_USER} -> ${code:-no answer}"
      return 1
    fi
  fi
  return 0
}

wait_ready() {
  local deadline=$((SECONDS + READY_TIMEOUT))
  while (( SECONDS < deadline )); do
    if probe_once; then
      return 0
    fi
    sleep 2
  done
  fail "not ready after ${READY_TIMEOUT}s: ${LAST_FAILURE}"
  return 1
}

dump_logs() {
  if [[ "$RESTART" == "systemd" ]]; then
    sudo journalctl -u "$LOGIN_UNIT" -n 40 --no-pager >&2 || true
    sudo journalctl -u "$ZONE_UNIT" -n 40 --no-pager >&2 || true
  else
    tail -n 40 logs/login.log >&2 2>/dev/null || true
    tail -n 40 logs/zone.log >&2 2>/dev/null || true
  fi
}

prune() {
  local keep_list=("$RELEASE_ID" "${PREVIOUS:-}") kept=0 dir name
  # Newest first by mtime; current and previous always stay.
  while IFS= read -r dir; do
    name="$(basename "$dir")"
    [[ "$name" == *.partial ]] && continue
    if [[ " ${keep_list[*]} " == *" ${name} "* ]]; then
      continue
    fi
    kept=$((kept + 1))
    if (( kept > KEEP - 2 )); then
      log "pruning releases/${name}"
      rm -rf "releases/${name}"
    fi
  done < <(find releases -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | cut -d' ' -f2-)
}

# --- 4. switch, restart, gate ------------------------------------------------------------------------------
switch_to "$RELEASE_ID"
if restart && wait_ready; then
  log "Deploy OK: release ${RELEASE_ID} is ready (${READY_URLS}; tcp ${READY_TCP_PORTS})"
  prune
  exit 0
fi

fail "release ${RELEASE_ID} failed readiness"
dump_logs
if [[ -z "$PREVIOUS" || "$PREVIOUS" == "$RELEASE_ID" ]]; then
  fail "no previous release to roll back to; production stays on the failed release ${RELEASE_ID}"
  exit 1
fi
if (( PENDING_MIGRATIONS > 0 )); then
  fail "rollback refused: ${RELEASE_ID} applied ${PENDING_MIGRATIONS} forward-only migration(s); release ${PREVIOUS} would reject the newer schema history."
  fail "fix forward, or restore the database from backup (docs/operations/deployment.md) and then switch current back."
  exit 1
fi

fail "rolling back to ${PREVIOUS}"
switch_to "$PREVIOUS"
if restart && wait_ready; then
  fail "ROLLED BACK: production is on ${PREVIOUS}; failed release kept at releases/${RELEASE_ID} for inspection"
else
  fail "rollback to ${PREVIOUS} is not ready either: ${LAST_FAILURE}"
  dump_logs
fi
exit 1
