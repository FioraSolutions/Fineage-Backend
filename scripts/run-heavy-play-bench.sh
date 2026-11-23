#!/usr/bin/env bash
# Debug heavy-play perf matrix (#951). Local stack only.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="${FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX:-loadbot}"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="${FINEAGE_LOGIN_LOADTEST_PASSWORD:-loadtest-pass-v1}"
export FINEAGE_ZONE_LOADTEST_COMBAT="${FINEAGE_ZONE_LOADTEST_COMBAT:-1}"
export FINEAGE_ZONE_MAPS="${FINEAGE_ZONE_MAPS:-loadtest_aoe_arena}"
export FINEAGE_BUILD_CONFIGURATION="${FINEAGE_BUILD_CONFIGURATION:-Debug}"
export FINEAGE_STARTING_LEVEL="${FINEAGE_STARTING_LEVEL:-77}"
export FINEAGE_ZONE_METRICS_PORT="${FINEAGE_ZONE_METRICS_PORT:-9465}"

REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/loadtest-heavy/$(date -u +%Y%m%dT%H%M%SZ)}"
LOADTEST_DLL="$ROOT/tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll"
mkdir -p "$REPORT_ROOT"

dotnet build "$ROOT/tools/Fineage.LoadTest" -c Release -v q -nologo

./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
FINEAGE_GEODATA_REQUIRED=0 ./scripts/run-local-net.sh start
ZONE_PID="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"

run_one() {
  local yaml="$1"
  local tag
  tag="$(basename "$yaml" .yaml)"
  local out="$REPORT_ROOT/$tag"
  mkdir -p "$out"
  dotnet "$LOADTEST_DLL" run \
    --scenario "$ROOT/tools/Fineage.LoadTest/scenarios/$yaml" \
    --target fineage \
    --report-dir "$out" \
    --login-log "$ROOT/local-logs/login-net.log" \
    --zone-log "$ROOT/local-logs/zone-net.log" \
    --zone-pid "$ZONE_PID" \
    --region-workers 0
  echo "load-test-report: $out/load-test-report.md"
}

run_one perf-aoe-train.yaml
run_one perf-aoe-multiparty.yaml
run_one perf-aoe-combined.yaml

./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
echo "Heavy-play reports under $REPORT_ROOT"