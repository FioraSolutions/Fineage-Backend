#!/usr/bin/env bash
# Fineage backup/restore primitives (issue #987).
#
#   backup-state.sh backup --pg-container NAME --pg-user U --pg-db D --state-dir DIR
#   backup-state.sh backup --database-url URL --state-dir DIR
#   backup-state.sh verify --backup-dir DIR
#   backup-state.sh restore --backup-dir DIR --pg-container NAME --pg-user U --pg-db D --state-dir DIR
#
# A backup is one timestamped directory: postgres.dump (pg_dump custom format), sidecar.tar.gz
# (the FINEAGE_STATE_DIR logs and archives), pg_restore.list (TOC integrity proof), sidecars.tsv and
# manifest.json (checksums + sizes). Restore is deliberately restricted to a disposable,
# labelled PostgreSQL container; it cannot be pointed at the live database.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_ROOT="${FINEAGE_BACKUP_ROOT:-$ROOT/local-backups}"
RETENTION_DAYS="${FINEAGE_BACKUP_RETENTION_DAYS:-14}"
RETENTION_COUNT="${FINEAGE_BACKUP_RETENTION_COUNT:-14}"
PG_IMAGE="${FINEAGE_PG_IMAGE:-postgres:16-alpine}"
DRILL_CONTAINER_PREFIX="${FINEAGE_RESTORE_CONTAINER_PREFIX:-fineage-drill-}"
DRILL_ROLE_LABEL="backup-restore-drill"

# #996: world subsystem state (lottery, monster race, Seven Signs, olympiad, raid points,
# punishments, fishing championship, boss schedule, PC cafe) lives in the world_state table and
# is inside postgres.dump. The state directory keeps only append-only logs (gm-audit.jsonl) and
# archived *.imported-* copies of the pre-#996 sidecars; it is still archived, but no file in it
# is required. tests/Fineage.Zone.Tests/BackupRestoreScriptTests.cs pins this.
REQUIRED_SIDECARS=()

log() { printf '[backup] %s\n' "$*"; }
die() { printf '[backup] ERROR: %s\n' "$*" >&2; exit 1; }

need_cmd() { command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"; }

docker_ok() {
  command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    die "neither sha256sum nor shasum is available"
  fi
}

file_size() { wc -c <"$1" | tr -d ' '; }

now_utc() { date -u +%Y-%m-%dT%H:%M:%SZ; }

manifest_get() { # manifest.json key
  python3 - "$1" "$2" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    value = json.load(f)
for part in sys.argv[2].split("."):
    value = value[part]
print(value)
PY
}

# FINEAGE_DATABASE_URL is the project's Npgsql form (Host=...;Database=...;Username=...;Password=...).
# pg_dump/pg_restore only accept a libpq URI or keyword string; feeding the Npgsql form fails with
# "invalid connection option Host" and writes an empty dump.
libpq_url() {
  python3 - "$1" <<'PY'
import re, sys, urllib.parse
s = sys.argv[1].strip()
if s.startswith("postgres"):
    print(s)
    raise SystemExit(0)
parts = {k.lower(): v for k, v in (p.split("=", 1) for p in re.split(r"[;&]", s) if "=" in p)}
user = urllib.parse.quote(parts.get("username") or parts.get("user") or "fineage", safe="")
password = urllib.parse.quote(parts.get("password") or "", safe="")
host = parts.get("host") or "127.0.0.1"
port = parts.get("port") or "5432"
db = parts.get("database") or parts.get("db") or "fineage"
print(f"postgresql://{user}:{password}@{host}:{port}/{db}")
PY
}

run_pg_dump() { # container user db out
  local container="$1" user="$2" db="$3" out="$4"
  if [[ -n "$container" ]]; then
    docker exec -e PGPASSWORD="${PGPASSWORD:-}" "$container" \
      pg_dump -U "$user" -d "$db" --format=custom --no-owner --no-acl >"$out"
  else
    need_cmd pg_dump
    pg_dump "$(libpq_url "$DATABASE_URL")" --format=custom --no-owner --no-acl >"$out"
  fi
}

run_pg_restore_list() { # dump out
  local dump="$1" out="$2"
  if command -v pg_restore >/dev/null 2>&1; then
    pg_restore --list "$dump" >"$out"
    return
  fi
  docker_ok || die "pg_restore is not installed and Docker is unavailable for the TOC integrity check"
  local dir base
  dir="$(cd "$(dirname "$dump")" && pwd)"
  base="$(basename "$dump")"
  docker run --rm -v "$dir:/backup:ro" "$PG_IMAGE" pg_restore --list "/backup/$base" >"$out"
}

run_psql() { # container db user sql
  local container="$1" db="$2" user="$3" sql="$4"
  docker exec -e PGPASSWORD="${PGPASSWORD:-}" "$container" \
    psql -U "$user" -d "$db" -Atq -v ON_ERROR_STOP=1 -c "$sql"
}

assert_disposable_target() {
  [[ "${FINEAGE_RESTORE_CONFIRM:-}" == "disposable-only" ]] ||
    die "restore refused: set FINEAGE_RESTORE_CONFIRM=disposable-only"
  local container="${1:-}"
  if [[ -n "$container" ]]; then
    [[ "$container" == "$DRILL_CONTAINER_PREFIX"* ]] ||
      die "restore refused: container '$container' must start with '$DRILL_CONTAINER_PREFIX'"
    local role
    role="$(docker inspect --format '{{ index .Config.Labels "fineage.role" }}' "$container" 2>/dev/null || true)"
    [[ "$role" == "$DRILL_ROLE_LABEL" ]] ||
      die "restore refused: container '$container' is not labelled fineage.role=$DRILL_ROLE_LABEL"
    return
  fi
  [[ -n "${2:-}" ]] || die "restore refused: no target database"
  local host db
  host="$(python3 - "$2" <<'PY'
import re, sys
parts = dict(p.split("=", 1) for p in re.split(r"[;&]", sys.argv[1]) if "=" in p)
print(parts.get("Host", parts.get("host", "")))
PY
)"
  db="$(python3 - "$2" <<'PY'
import re, sys
parts = dict(p.split("=", 1) for p in re.split(r"[;&]", sys.argv[1]) if "=" in p)
print(parts.get("Database", parts.get("database", "")))
PY
)"
  [[ "$host" == "127.0.0.1" || "$host" == "localhost" ]] ||
    die "restore refused: target host '$host' is not localhost/127.0.0.1"
  [[ "$db" == fineage_restore* || "$db" == fineage_drill* ]] ||
    die "restore refused: target database '$db' must start with fineage_restore or fineage_drill"
}

verify_archive() {
  local dir="$1"
  [[ -f "$dir/manifest.json" ]] || die "missing manifest.json in $dir"
  local dump="$dir/postgres.dump" tarfile="$dir/sidecar.tar.gz" list="$dir/pg_restore.list"
  [[ -s "$dump" ]] || die "missing or empty postgres.dump in $dir"
  [[ -s "$tarfile" ]] || die "missing or empty sidecar.tar.gz in $dir"
  [[ -s "$list" ]] || die "missing or empty pg_restore.list in $dir"

  local expected actual
  expected="$(manifest_get "$dir/manifest.json" database.dump_sha256)"
  actual="$(sha256_file "$dump")"
  [[ "$expected" == "$actual" ]] || die "postgres.dump checksum mismatch: manifest=$expected actual=$actual"
  expected="$(manifest_get "$dir/manifest.json" sidecar.sha256)"
  actual="$(sha256_file "$tarfile")"
  [[ "$expected" == "$actual" ]] || die "sidecar.tar.gz checksum mismatch: manifest=$expected actual=$actual"

  run_pg_restore_list "$dump" "$dir/pg_restore.list.verify"
  cmp -s "$list" "$dir/pg_restore.list.verify" ||
    die "pg_restore --list output changed since backup"
  rm -f "$dir/pg_restore.list.verify"

  local required
  for required in schema_migrations characters character_items store_snapshot economy_ledger world_state; do
    grep -q "TABLE DATA public $required" "$list" ||
      die "pg_restore --list is missing TABLE DATA public $required"
  done

  local members sidecar
  members="$(tar -tzf "$tarfile" | sed 's#^\./##')"
  for sidecar in "${REQUIRED_SIDECARS[@]+"${REQUIRED_SIDECARS[@]}"}"; do
    grep -qx "$sidecar" <<<"$members" || die "sidecar archive is missing required file: $sidecar"
  done

  while IFS=$'\t' read -r sha bytes path; do
    [[ -n "$path" ]] || continue
    grep -qx "$path" <<<"$members" || die "sidecar archive is missing $path"
  done <"$dir/sidecars.tsv"

  log "verified $dir (dump $(file_size "$dump") bytes, $(wc -l <"$list" | tr -d ' ') TOC entries)"
}

prune_backups() {
  local root="$1"
  [[ -d "$root" ]] || return 0
  local keep_count="$RETENTION_COUNT" keep_days="$RETENTION_DAYS"
  local dirs=() dir
  while IFS= read -r dir; do
    [[ -n "$dir" ]] && dirs+=("$dir")
  done < <(find "$root" -mindepth 1 -maxdepth 1 -type d -name '20????????T??????Z*' -print | sort)
  local total="${#dirs[@]}"
  local index=0
  for dir in "${dirs[@]}"; do
    index=$((index + 1))
    [[ -f "$dir/manifest.json" ]] || continue
    local mtime
    mtime="$(stat -f %m "$dir" 2>/dev/null || stat -c %Y "$dir")"
    local age_seconds=$(( $(date +%s) - mtime ))
    local too_old=0
    if (( age_seconds > keep_days * 86400 )); then too_old=1; fi
    local over_count=0
    if (( index <= total - keep_count )); then over_count=1; fi
    if (( too_old || over_count )); then
      log "retention: removing $dir"
      rm -rf "$dir"
    fi
  done
  log "retention: kept $(( total < keep_count ? total : keep_count )) of $total backup(s) " \
      "(max ${keep_days}d / ${keep_count} copies)"
}

cmd_backup() {
  local container="" user="" db="" state_dir="" label="manual" backup_root="$BACKUP_ROOT"
  DATABASE_URL="${FINEAGE_DATABASE_URL:-}"
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --pg-container) container="$2"; shift 2;;
      --pg-user) user="$2"; shift 2;;
      --pg-db) db="$2"; shift 2;;
      --database-url) DATABASE_URL="$2"; shift 2;;
      --state-dir) state_dir="$2"; shift 2;;
      --label) label="$2"; shift 2;;
      --backup-root) backup_root="$2"; shift 2;;
      *) die "unknown backup option: $1";;
    esac
  done
  [[ -n "$state_dir" && -d "$state_dir" ]] || die "backup requires --state-dir <existing directory>"
  if [[ -n "$container" ]]; then
    [[ -n "$user" && -n "$db" ]] || die "--pg-container requires --pg-user and --pg-db"
    docker inspect "$container" >/dev/null 2>&1 || die "PostgreSQL container not found: $container"
  else
    [[ -n "$DATABASE_URL" ]] || die "backup requires --pg-container or --database-url"
  fi

  local missing=() sidecar
  for sidecar in "${REQUIRED_SIDECARS[@]+"${REQUIRED_SIDECARS[@]}"}"; do
    [[ -f "$state_dir/$sidecar" ]] || missing+=("$sidecar")
  done
  ((${#missing[@]} == 0)) ||
    die "state directory '$state_dir' is missing required sidecar(s): ${missing[*]} (refusing a partial backup)"

  local stamp out
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  out="$backup_root/$stamp"
  local suffix=1
  while [[ -e "$out" ]]; do out="$backup_root/$stamp-$suffix"; suffix=$((suffix + 1)); done
  mkdir -p "$out"

  log "dumping database to $out/postgres.dump"
  run_pg_dump "$container" "$user" "$db" "$out/postgres.dump"
  [[ -s "$out/postgres.dump" ]] || die "pg_dump produced an empty file"

  log "archiving state directory $state_dir"
  tar -C "$state_dir" -czf "$out/sidecar.tar.gz" --exclude '*.tmp-*' --exclude '.DS_Store' .
  : >"$out/sidecars.tsv"
  for sidecar in "${REQUIRED_SIDECARS[@]+"${REQUIRED_SIDECARS[@]}"}"; do
    printf '%s\t%s\t%s\n' "$(sha256_file "$state_dir/$sidecar")" \
      "$(file_size "$state_dir/$sidecar")" "$sidecar" >>"$out/sidecars.tsv"
  done
  if [[ -f "$state_dir/gm-audit.jsonl" ]]; then
    printf '%s\t%s\t%s\n' "$(sha256_file "$state_dir/gm-audit.jsonl")" \
      "$(file_size "$state_dir/gm-audit.jsonl")" "gm-audit.jsonl" >>"$out/sidecars.tsv"
  fi

  log "checking pg_restore --list"
  run_pg_restore_list "$out/postgres.dump" "$out/pg_restore.list" ||
    die "pg_restore --list failed: the dump is not a readable custom-format archive"
  local required
  for required in schema_migrations characters character_items store_snapshot economy_ledger world_state; do
    grep -q "TABLE DATA public $required" "$out/pg_restore.list" ||
      die "pg_restore --list does not contain TABLE DATA public $required"
  done

  local server_version="unknown"
  if [[ -n "$container" ]]; then
    server_version="$(run_psql "$container" "$db" "$user" 'SHOW server_version;')"
  fi

  BACKUP_DIR="$out" BACKUP_CREATED_UTC="$(now_utc)" python3 - "$out" "$label" "$db" "$server_version" <<'PY'
import json, os, sys
out, label, db, server_version = sys.argv[1:]
sidecars = []
with open(os.path.join(out, "sidecars.tsv"), encoding="utf-8") as f:
    for line in f:
        sha, size, path = line.rstrip("\n").split("\t")
        sidecars.append({"path": path, "bytes": int(size), "sha256": sha})
def sha(path):
    import hashlib
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()
manifest = {
    "format": 1,
    "label": label,
    "created_utc": os.environ.get("BACKUP_CREATED_UTC", ""),
    "database": {
        "name": db,
        "server_version": server_version,
        "dump_format": "custom",
        "dump_bytes": os.path.getsize(os.path.join(out, "postgres.dump")),
        "dump_sha256": sha(os.path.join(out, "postgres.dump")),
    },
    "sidecar": {
        "archive": "sidecar.tar.gz",
        "bytes": os.path.getsize(os.path.join(out, "sidecar.tar.gz")),
        "sha256": sha(os.path.join(out, "sidecar.tar.gz")),
        "files": sidecars,
    },
    "pg_restore_list": {
        "path": "pg_restore.list",
        "sha256": sha(os.path.join(out, "pg_restore.list")),
    },
}
with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2, sort_keys=True)
    f.write("\n")
PY

  prune_backups "$backup_root"
  log "backup complete: $out"
  printf 'BACKUP_DIR=%s\n' "$out"
}

cmd_verify() {
  local dir=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --backup-dir) dir="$2"; shift 2;;
      *) die "unknown verify option: $1";;
    esac
  done
  [[ -n "$dir" ]] || dir="$(find "$BACKUP_ROOT" -mindepth 1 -maxdepth 1 -type d -name '20*' | sort | tail -1)"
  [[ -n "$dir" && -d "$dir" ]] || die "no backup directory found"
  verify_archive "$dir"
  printf 'VERIFIED_BACKUP=%s\n' "$dir"
}

cmd_restore() {
  local dir="" container="" user="" db="" state_dir="" database_url=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --backup-dir) dir="$2"; shift 2;;
      --pg-container) container="$2"; shift 2;;
      --pg-user) user="$2"; shift 2;;
      --pg-db) db="$2"; shift 2;;
      --database-url) database_url="$2"; shift 2;;
      --state-dir) state_dir="$2"; shift 2;;
      *) die "unknown restore option: $1";;
    esac
  done
  [[ -n "$dir" && -d "$dir" ]] || die "restore requires --backup-dir"
  [[ -n "$state_dir" ]] || die "restore requires --state-dir"
  assert_disposable_target "$container" "$database_url"
  if [[ -n "$container" ]]; then
    [[ -n "$user" && -n "$db" ]] || die "--pg-container requires --pg-user and --pg-db"
    local occupied
    occupied="$(run_psql "$container" "$db" "$user" "SELECT to_regclass('public.store_snapshot') IS NOT NULL;")"
    [[ "$occupied" == "f" ]] || die "target database '$db' is not empty; refusing to restore over an existing database"
  else
    [[ -n "$database_url" ]] || die "restore requires --pg-container or --database-url"
    local occupied
    occupied="$(psql "$(libpq_url "$database_url")" -Atqc "SELECT to_regclass('public.store_snapshot') IS NOT NULL;" 2>/dev/null || echo f)"
    [[ "$occupied" == "f" ]] || die "target database is not empty; refusing to restore over an existing database"
  fi
  [[ ! -e "$state_dir" || -z "$(ls -A "$state_dir" 2>/dev/null)" ]] ||
    die "target state directory '$state_dir' is not empty"

  verify_archive "$dir"
  mkdir -p "$state_dir"
  log "restoring database into disposable target"
  if [[ -n "$container" ]]; then
    docker exec -i -e PGPASSWORD="${PGPASSWORD:-}" "$container" \
      pg_restore -U "$user" -d "$db" --no-owner --no-acl --exit-on-error <"$dir/postgres.dump"
  else
    need_cmd pg_restore
    pg_restore --dbname="$(libpq_url "$database_url")" --no-owner --no-acl --exit-on-error <"$dir/postgres.dump"
  fi
  tar -xzf "$dir/sidecar.tar.gz" -C "$state_dir"
  while IFS=$'\t' read -r sha bytes path; do
    [[ -n "$path" ]] || continue
    [[ -f "$state_dir/$path" ]] || die "restored state directory is missing $path"
    local actual
    actual="$(sha256_file "$state_dir/$path")"
    [[ "$actual" == "$sha" ]] || die "restored sidecar $path checksum mismatch"
  done <"$dir/sidecars.tsv"
  log "restore complete: database + $(wc -l <"$dir/sidecars.tsv" | tr -d ' ') sidecar file(s)"
  printf 'RESTORED_STATE_DIR=%s\n' "$state_dir"
}

case "${1:-}" in
  backup) shift; cmd_backup "$@";;
  verify) shift; cmd_verify "$@";;
  restore) shift; cmd_restore "$@";;
  *) die "usage: $0 {backup|verify|restore} ...";;
esac
