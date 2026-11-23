#!/usr/bin/env bash
# Local ramp 10 → 50 → 100 → 200 bots with consistency oracle reports.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${HOME}/.dotnet:${PATH}"
REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/loadtest-ramp/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$REPORT_ROOT"

export FINEAGE_DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"
export FINEAGE_HANDOFF_SECRET="${FINEAGE_HANDOFF_SECRET:-FineageLoginHandoffV1}"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="${FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX:-loadbot}"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="${FINEAGE_LOGIN_LOADTEST_PASSWORD:-loadtest-pass-v1}"
export FINEAGE_LOGIN_METRICS_PORT="${FINEAGE_LOGIN_METRICS_PORT:-9464}"
export FINEAGE_ZONE_METRICS_PORT="${FINEAGE_ZONE_METRICS_PORT:-9465}"
export FINEAGE_ZONE_REGION_WORKERS="${FINEAGE_ZONE_REGION_WORKERS:-0}"
export FINEAGE_ZONE_AOI_CELL="${FINEAGE_ZONE_AOI_CELL:-3000}"

dotnet build "$ROOT/tools/Fineage.LoadTest" -c Release -v q -nologo

run_scenario() {
  local file="$1"
  local tag
  tag="$(basename "$file" .yaml)"
  local out="$REPORT_ROOT/$tag"
  mkdir -p "$out"
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  ./scripts/run-local-net.sh start
  ZONE_PID="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"
  set +e
  dotnet "$ROOT/tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll" \
    --scenario "$ROOT/tools/Fineage.LoadTest/scenarios/$file" \
    --target fineage \
    --report-dir "$out" \
    --login-log "$ROOT/local-logs/login-net.log" \
    --zone-log "$ROOT/local-logs/zone-net.log" \
    --zone-pid "$ZONE_PID" \
    --region-workers "$FINEAGE_ZONE_REGION_WORKERS"
  status=$?
  set -e
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  if [[ ! -f "$out/load-test-report.md" ]]; then
    echo "FAIL: load test runner wrote no report under $out" >&2
    exit 1
  fi
  if [[ "$status" -ne 0 ]]; then
    echo "FAIL: load test exit $status ($out/load-test-report.md)" >&2
    exit "$status"
  fi
}

for f in bots-10-town.yaml bots-50-town.yaml bots-100-town.yaml bots-200-spread.yaml; do
  run_scenario "$f"
done

echo "Ramp reports: $REPORT_ROOT"