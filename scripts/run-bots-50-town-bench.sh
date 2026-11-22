#!/usr/bin/env bash
# Release bots-50-town at FINEAGE_ZONE_REGION_WORKERS 0 then 4; writes report.json per worker.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="${DOTNET_ROOT}:$PATH"
export FINEAGE_DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"
export FINEAGE_HANDOFF_SECRET="${FINEAGE_HANDOFF_SECRET:-FineageLoginHandoffV1}"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="${FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX:-loadbot}"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="${FINEAGE_LOGIN_LOADTEST_PASSWORD:-loadtest-pass-v1}"
export FINEAGE_LOGIN_METRICS_PORT="${FINEAGE_LOGIN_METRICS_PORT:-9464}"
export FINEAGE_ZONE_METRICS_PORT="${FINEAGE_ZONE_METRICS_PORT:-9465}"
export FINEAGE_BUILD_CONFIGURATION="${FINEAGE_BUILD_CONFIGURATION:-Release}"
SCENARIO="${ROOT}/tools/Fineage.LoadTest/scenarios/bots-50-town.yaml"
LOADTEST_DLL="${ROOT}/tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll"
REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/bots-50-town-bench}"
mkdir -p "$REPORT_ROOT"
dotnet build "$ROOT/tools/Fineage.LoadTest" -c Release -v q -nologo

run_one() {
  local workers="$1"
  local out="$REPORT_ROOT/w${workers}"
  mkdir -p "$out"
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  export FINEAGE_ZONE_REGION_WORKERS="$workers"
  ./scripts/run-local-net.sh start
  local zone_pid
  zone_pid="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"
  dotnet "$LOADTEST_DLL" run \
    --scenario "$SCENARIO" \
    --spawn-layout town \
    --target fineage \
    --report-dir "$out" \
    --login-log "$ROOT/local-logs/login-net.log" \
    --zone-log "$ROOT/local-logs/zone-net.log" \
    --zone-pid "$zone_pid" \
    --region-workers "$workers"
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
}

run_one 0
run_one 4
echo "Reports under $REPORT_ROOT"