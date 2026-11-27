#!/usr/bin/env bash
# Host-side release manager for Fineage (issue #989).
#
#   release-manager.sh deploy --app-root DIR --staging DIR --release-id ID [options]
#   release-manager.sh ensure-layout --app-root DIR
#   release-manager.sh switch --app-root DIR --release-id ID
#   release-manager.sh rollback --app-root DIR
#   release-manager.sh ready --login-url URL --zone-url URL [--timeout SECONDS]
#
# The app root keeps shared configuration/state (fineage.env, data/, logs/) and immutable
# releases/<id>/. current and previous are symlinks, so a switch or rollback is one atomic
# rename of a symlink. A failed preflight never touches current. A failed restart, readiness
# wait, or smoke test switches current back to previous and verifies the previous release is
# really ready too; it never reports rollback success from the switch alone.
set -euo pipefail

APP_ROOT="${FINEAGE_APP_ROOT:-/home/nikita/fineage-backend/deploy}"
RESTART="${FINEAGE_RESTART:-systemd}"
LOGIN_UNIT="${FINEAGE_LOGIN_UNIT:-fineage-login}"
ZONE_UNIT="${FINEAGE_ZONE_UNIT:-fineage-zone}"
READY_TIMEOUT="${FINEAGE_DEPLOY_READY_TIMEOUT:-90}"
SMOKE_MODE="${FINEAGE_DEPLOY_SMOKE:-auto}"

log() { printf '[release] %s\n' "$*"; }
die() { printf '[release] ERROR: %s\n' "$*" >&2; exit 1; }

need_cmd() { command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"; }

load_env() {
  if [[ -f "$APP_ROOT/fineage.env" ]]; then
    set -a
    # shellcheck disable=SC1091
    source "$APP_ROOT/fineage.env"
    set +a
  fi
}

current_target() {
  [[ -L "$APP_ROOT/current" ]] || return 1
  local target
  target="$(readlink "$APP_ROOT/current")" || return 1
  [[ -n "$target" ]] || return 1
  printf '%s\n' "$target"
}

target_path() {
  case "$1" in
    /*) printf '%s\n' "$1" ;;
    *) printf '%s/%s\n' "$APP_ROOT" "$1" ;;
  esac
}

atomic_link() { # target link
  local target="$1" link="$2" tmp
  if [[ -d "$link" && ! -L "$link" ]]; then
    die "refusing to replace directory $link with a symlink"
  fi
  tmp="${link}.new.$$"
  rm -f "$tmp"
  ln -s "$target" "$tmp"
  # rename(2) over the existing symlink is atomic; the old release stays intact.
  # Python's os.replace is used because GNU mv -T is unavailable on the local test host.
  need_cmd python3
  python3 - "$tmp" "$link" <<'PY'
import os, sys
os.replace(sys.argv[1], sys.argv[2])
PY
}

ensure_link() { # link expected-target
  local link="$1" expected="$2"
  if [[ -L "$link" ]]; then
    [[ "$(readlink "$link")" == "$expected" ]] || die "$link points at $(readlink "$link"), expected $expected"
    return
  fi
  [[ -e "$link" ]] && die "$link exists and is not a symlink; refusing to overwrite it"
  atomic_link "$expected" "$link"
}

ensure_layout() {
  mkdir -p "$APP_ROOT/releases" "$APP_ROOT/logs" "$APP_ROOT/data"
  if [[ ! -L "$APP_ROOT/current" ]]; then
    if [[ -d "$APP_ROOT/login" && ! -L "$APP_ROOT/login" && -d "$APP_ROOT/zone" && ! -L "$APP_ROOT/zone" ]]; then
      local bootstrap="bootstrap-$(date -u +%Y%m%dT%H%M%SZ)-$$"
      log "importing legacy live directories as rollback release $bootstrap"
      mkdir -p "$APP_ROOT/releases/$bootstrap"
      cp -a "$APP_ROOT/login" "$APP_ROOT/releases/$bootstrap/login"
      cp -a "$APP_ROOT/zone" "$APP_ROOT/releases/$bootstrap/zone"
      atomic_link "releases/$bootstrap" "$APP_ROOT/current"
      rm -rf "$APP_ROOT/login" "$APP_ROOT/zone"
    else
      die "no current release and no legacy login/zone directories to bootstrap under $APP_ROOT"
    fi
  fi
  ensure_link "$APP_ROOT/login" "current/login"
  ensure_link "$APP_ROOT/zone" "current/zone"
}

sanitize_release_id() {
  local id="$1"
  [[ "$id" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] ||
    die "invalid release id '$id' (allowed: letters, digits, '.', '_', '-')"
  printf '%s\n' "$id"
}

install_staging() { # staging release-id -> prints release directory
  local staging="$1" id="$2" target suffix
  [[ -d "$staging/login" && -d "$staging/zone" ]] ||
    die "staging '$staging' is missing login/ and zone/"
  target="$APP_ROOT/releases/$id"
  suffix=1
  while [[ -e "$target" ]]; do
    target="$APP_ROOT/releases/$id-$suffix"
    suffix=$((suffix + 1))
  done
  mv "$staging" "$target"
  printf '%s\n' "$target"
}

preflight() { # release-dir
  local target="$1"
  local login="$target/login/Fineage.Login"
  local zone="$target/zone/Fineage.Zone"
  [[ -x "$login" ]] || die "staged login binary is missing or not executable: $login"
  [[ -x "$zone" ]] || die "staged zone binary is missing or not executable: $zone"
  [[ -n "${FINEAGE_DATABASE_URL:-}" ]] ||
    die "FINEAGE_DATABASE_URL is not set (missing or incomplete $APP_ROOT/fineage.env)"

  local logfile="$APP_ROOT/logs/preflight-$(basename "$target").log"
  log "migration preflight: $login migrate --plan"
  if ! "$login" migrate --plan >"$logfile" 2>&1; then
    cat "$logfile" >&2 || true
    die "migration preflight failed; current release was not changed"
  fi
  cat "$logfile"
}

record_previous() {
  local current
  current="$(current_target)" || die "no current release to record as previous"
  [[ -d "$(target_path "$current")" ]] || die "current release target does not exist: $current"
  if [[ -L "$APP_ROOT/previous" && "$(readlink "$APP_ROOT/previous")" == "$current" ]]; then
    return
  fi
  log "previous known-good release: $current"
  atomic_link "$current" "$APP_ROOT/previous"
}

switch_release() { # release-id
  local id="$1"
  local target="$APP_ROOT/releases/$id"
  [[ -d "$target/login" && -d "$target/zone" ]] || die "release $id is incomplete under $APP_ROOT/releases"
  atomic_link "releases/$id" "$APP_ROOT/current"
  log "current -> releases/$id"
}

restart_services() {
  if [[ "$RESTART" == "systemd" ]]; then
    log "restarting systemd units $LOGIN_UNIT and $ZONE_UNIT"
    sudo systemctl restart "$LOGIN_UNIT" "$ZONE_UNIT"
  elif [[ "$RESTART" == "scripts" ]]; then
    log "restarting through run-login.sh and run-zone.sh"
    (cd "$APP_ROOT" && ./run-login.sh && ./run-zone.sh)
  else
    die "invalid FINEAGE_RESTART='$RESTART' (expected systemd or scripts)"
  fi
}

http_get() { # url output-file -> prints HTTP status; 000 on connection failure
  local url="$1" out="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -sS --max-time 3 -o "$out" -w '%{http_code}' "$url" 2>/dev/null || true
    return
  fi
  need_cmd python3
  python3 - "$url" "$out" <<'PY'
import sys, urllib.request, urllib.error
url, out = sys.argv[1], sys.argv[2]
try:
    response = urllib.request.urlopen(url, timeout=3)
    status = response.status
    data = response.read()
except urllib.error.HTTPError as ex:
    status = ex.code
    data = ex.read()
except Exception:
    print("000")
    raise SystemExit(0)
with open(out, "wb") as f:
    f.write(data)
print(status)
PY
}

parse_ready() { # body-file -> 0 if {"status":"ready","ready":true}
  local body="$1"
  need_cmd python3
  python3 - "$body" <<'PY'
import json, sys
try:
    with open(sys.argv[1], encoding="utf-8") as f:
        doc = json.load(f)
except Exception as ex:
    print("invalid readiness JSON: " + str(ex))
    raise SystemExit(1)
if doc.get("ready") is True and doc.get("status") == "ready":
    raise SystemExit(0)
reasons = doc.get("reasons") or []
print("; ".join(str(reason) for reason in reasons) or "ready flag is false")
raise SystemExit(1)
PY
}

wait_ready() { # login-url zone-url
  local login_url="$1" zone_url="$2"
  local deadline=$((SECONDS + READY_TIMEOUT))
  local login_body="$APP_ROOT/logs/.ready-login.$$"
  local zone_body="$APP_ROOT/logs/.ready-zone.$$"
  local last_login="" last_zone=""
  while (( SECONDS < deadline )); do
    local login_status zone_status login_ok=1 zone_ok=1
    login_status="$(http_get "$login_url" "$login_body" | tr -d '\r\n')"
    zone_status="$(http_get "$zone_url" "$zone_body" | tr -d '\r\n')"
    [[ "$login_status" == "200" ]] && parse_ready "$login_body" >/dev/null 2>&1 || login_ok=0
    [[ "$zone_status" == "200" ]] && parse_ready "$zone_body" >/dev/null 2>&1 || zone_ok=0
    if (( login_ok && zone_ok )); then
      rm -f "$login_body" "$zone_body"
      log "readiness OK: login=$login_url zone=$zone_url"
      return 0
    fi
    last_login="login HTTP $login_status: $(parse_ready "$login_body" 2>&1 || true)"
    last_zone="zone HTTP $zone_status: $(parse_ready "$zone_body" 2>&1 || true)"
    sleep 2
  done
  rm -f "$login_body" "$zone_body"
  printf '[release] readiness timed out after %ss\n  %s\n  %s\n' "$READY_TIMEOUT" "$last_login" "$last_zone" >&2
  return 1
}

ready_urls() {
  local http_port="${FINEAGE_LOGIN_HTTP_PORT:-5080}"
  local status_port="${FINEAGE_ZONE_STATUS_PORT:-7780}"
  case "$status_port" in
    0|off|OFF|disabled|DISABLED)
      die "FINEAGE_ZONE_STATUS_PORT is disabled; Zone readiness cannot be verified" ;;
  esac
  printf 'http://127.0.0.1:%s/readyz %s\n' "$http_port" "http://127.0.0.1:${status_port}/readyz"
}

run_smoke() { # release-dir
  local target="$1"
  if [[ "$SMOKE_MODE" == "skip" ]]; then
    log "smoke explicitly skipped by FINEAGE_DEPLOY_SMOKE=skip; readiness was still verified"
    return 0
  fi
  local smoke="$target/smoke/Fineage.LoadTest"
  [[ -x "$smoke" ]] ||
    die "smoke binary missing at $smoke; pass the published LoadTest directory or set FINEAGE_DEPLOY_SMOKE=skip explicitly"

  local prefix="${FINEAGE_DEPLOY_SMOKE_ACCOUNT_PREFIX:-deploysmoke}"
  local account="${prefix}00000"
  local password="${FINEAGE_DEPLOY_SMOKE_PASSWORD:-}"
  local secret_file="$APP_ROOT/smoke.secret"
  if [[ -z "$password" ]]; then
    if [[ ! -s "$secret_file" ]]; then
      (umask 077; python3 - <<'PY' >"$secret_file"
import secrets
print(secrets.token_hex(24))
PY
      )
    fi
    password="$(tr -d '\n\r' <"$secret_file")"
  fi
  [[ -n "$password" ]] || die "smoke password is empty"

  local login="$target/login/Fineage.Login"
  if ! "$login" --database "$FINEAGE_DATABASE_URL" login "$account" "$password" >/dev/null 2>&1; then
    log "creating deploy smoke account $account"
    "$login" --database "$FINEAGE_DATABASE_URL" register "$account" "$password" >/dev/null ||
      die "deploy smoke account $account could not be created or authenticated"
  fi

  local scenario="$APP_ROOT/logs/smoke-$(basename "$target").yaml"
  local report_dir="$APP_ROOT/logs/smoke-$(basename "$target")"
  mkdir -p "$report_dir"
  cat >"$scenario" <<EOF
name: deploy-smoke
botCount: 1
rampUpSeconds: 0
durationSeconds: 2
gatekeeperTravelShare: 0
loginPort: ${FINEAGE_NET_LOGIN_PORT:-9106}
zonePort: ${FINEAGE_NET_ZONE_PORT:-9777}
actionWeights:
  move: 1
EOF
  log "world-entry smoke: account $account, bot enters, then exits"
  if ! FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="$prefix" \
       FINEAGE_LOGIN_LOADTEST_PASSWORD="$password" \
       "$smoke" --smoke --scenario "$scenario" --report-dir "$report_dir" \
       >"$report_dir/console.txt" 2>&1; then
    tail -n 40 "$report_dir/console.txt" >&2 || true
    die "world-entry smoke failed for $account"
  fi
  cat "$report_dir/console.txt"
}

write_status() { # release previous result reason
  local release="$1" previous="$2" result="$3" reason="$4"
  python3 - "$APP_ROOT/last-deploy.json" "$release" "$previous" "$result" "$reason" <<'PY'
import json, sys, time
path, release, previous, result, reason = sys.argv[1:]
with open(path, "w", encoding="utf-8") as f:
    json.dump({
        "release": release,
        "previous": previous,
        "result": result,
        "reason": reason,
        "at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }, f, indent=2, sort_keys=True)
    f.write("\n")
PY
}

rollback() { # failed-release reason
  local failed="$1" reason="$2"
  local previous
  previous="$(readlink "$APP_ROOT/previous" 2>/dev/null || true)"
  [[ -n "$previous" ]] || die "ROLLBACK FAILED: no previous release recorded after $reason"
  [[ -d "$(target_path "$previous")" ]] || die "ROLLBACK FAILED: previous release target is missing: $previous"

  log "rolling back to $previous after $reason"
  atomic_link "$previous" "$APP_ROOT/current"
  if ! restart_services; then
    die "ROLLBACK FAILED: $previous could not be restarted after switching current"
  fi
  local urls login_url zone_url
  read -r login_url zone_url <<<"$(ready_urls)"
  if wait_ready "$login_url" "$zone_url"; then
    write_status "$(basename "$(target_path "$failed")")" "$previous" "rolled_back" "$reason"
    log "ROLLBACK OK: $previous is ready again; failed release kept for inspection"
    return 0
  fi
  write_status "$(basename "$(target_path "$failed")")" "$previous" "rollback_failed" "$reason"
  die "ROLLBACK FAILED: previous release $previous did not become ready"
}

cmd_deploy() {
  local staging="" release_id=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --app-root) APP_ROOT="$2"; shift 2 ;;
      --staging) staging="$2"; shift 2 ;;
      --release-id) release_id="$2"; shift 2 ;;
      --restart) RESTART="$2"; shift 2 ;;
      --login-unit) LOGIN_UNIT="$2"; shift 2 ;;
      --zone-unit) ZONE_UNIT="$2"; shift 2 ;;
      *) die "unknown deploy option: $1" ;;
    esac
  done
  [[ -n "$staging" && -d "$staging" ]] || die "deploy requires --staging <existing directory>"
  [[ -n "$release_id" ]] || die "deploy requires --release-id"
  release_id="$(sanitize_release_id "$release_id")"
  need_cmd python3
  load_env
  ensure_layout

  local target
  target="$(install_staging "$staging" "$release_id")"
  log "staged release at $target"

  if ! preflight "$target"; then
    rm -rf "$target"
    die "preflight failed; current release was not changed"
  fi

  local current
  current="$(current_target)" || die "no current release; cannot guarantee rollback"
  record_previous

  if ! switch_release "$(basename "$target")"; then
    die "atomic switch failed; current release was not changed"
  fi

  if ! restart_services; then
    rollback "$target" "service restart failed"
    die "deploy failed: service restart failed; rolled back"
  fi

  local login_url zone_url
  read -r login_url zone_url <<<"$(ready_urls)"
  if ! wait_ready "$login_url" "$zone_url"; then
    rollback "$target" "readiness timeout or failed check"
    die "deploy failed: readiness did not pass; rolled back"
  fi

  if ! run_smoke "$target"; then
    rollback "$target" "world-entry smoke failed"
    die "deploy failed: world-entry smoke failed; rolled back"
  fi

  write_status "$(basename "$target")" "$current" "success" ""
  log "DEPLOY OK: current=$(readlink "$APP_ROOT/current") previous=$current"
}

cmd_ensure_layout() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --app-root) APP_ROOT="$2"; shift 2 ;;
      *) die "unknown ensure-layout option: $1" ;;
    esac
  done
  ensure_layout
}

cmd_switch() {
  local release_id=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --app-root) APP_ROOT="$2"; shift 2 ;;
      --release-id) release_id="$2"; shift 2 ;;
      *) die "unknown switch option: $1" ;;
    esac
  done
  [[ -n "$release_id" ]] || die "switch requires --release-id"
  ensure_layout
  switch_release "$(sanitize_release_id "$release_id")"
}

cmd_rollback() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --app-root) APP_ROOT="$2"; shift 2 ;;
      --restart) RESTART="$2"; shift 2 ;;
      *) die "unknown rollback option: $1" ;;
    esac
  done
  load_env
  need_cmd python3
  rollback "$APP_ROOT/releases/unknown" "manual rollback"
}

cmd_ready() {
  local login_url="" zone_url="" timeout=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --login-url) login_url="$2"; shift 2 ;;
      --zone-url) zone_url="$2"; shift 2 ;;
      --timeout) timeout="$2"; shift 2 ;;
      *) die "unknown ready option: $1" ;;
    esac
  done
  [[ -n "$login_url" && -n "$zone_url" ]] || die "ready requires --login-url and --zone-url"
  [[ -n "$timeout" ]] && READY_TIMEOUT="$timeout"
  need_cmd python3
  wait_ready "$login_url" "$zone_url"
}

usage() {
  cat >&2 <<'EOF'
usage: release-manager.sh <command> [options]
  deploy --app-root DIR --staging DIR --release-id ID [--restart systemd|scripts]
  ensure-layout --app-root DIR
  switch --app-root DIR --release-id ID
  rollback --app-root DIR [--restart systemd|scripts]
  ready --login-url URL --zone-url URL [--timeout SECONDS]
EOF
  exit 1
}

case "${1:-}" in
  deploy) shift; cmd_deploy "$@" ;;
  ensure-layout) shift; cmd_ensure_layout "$@" ;;
  switch) shift; cmd_switch "$@" ;;
  rollback) shift; cmd_rollback "$@" ;;
  ready) shift; cmd_ready "$@" ;;
  *) usage ;;
esac
