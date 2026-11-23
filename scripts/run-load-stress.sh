#!/usr/bin/env bash
# Stress matrix: 100 / 300 / 500 / 1000 mixed bots (local only). Override LEVELS="100 300".
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${HOME}/.dotnet:${PATH}"
LEVELS="${LEVELS:-100 300 500 1000}"
REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/loadtest-stress/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$REPORT_ROOT"

export FINEAGE_DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"
export FINEAGE_HANDOFF_SECRET="${FINEAGE_HANDOFF_SECRET:-FineageLoginHandoffV1}"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="${FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX:-loadbot}"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="${FINEAGE_LOGIN_LOADTEST_PASSWORD:-loadtest-pass-v1}"
export FINEAGE_LOGIN_METRICS_PORT="${FINEAGE_LOGIN_METRICS_PORT:-9464}"
export FINEAGE_ZONE_METRICS_PORT="${FINEAGE_ZONE_METRICS_PORT:-9465}"
export FINEAGE_ZONE_REGION_WORKERS="${FINEAGE_ZONE_REGION_WORKERS:-0}"
export FINEAGE_ZONE_OUTBOUND_QUEUE="${FINEAGE_ZONE_OUTBOUND_QUEUE:-512}"
export FINEAGE_LOGIN_MAX_CONNECTIONS="${FINEAGE_LOGIN_MAX_CONNECTIONS:-4096}"
export FINEAGE_ZONE_MAX_CONNECTIONS="${FINEAGE_ZONE_MAX_CONNECTIONS:-4096}"

dotnet build "$ROOT/tools/Fineage.LoadTest" -c Release -v q -nologo
LT="$ROOT/tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll"

for n in $LEVELS; do
  scenario="bots-${n}-mixed.yaml"
  out="$REPORT_ROOT/bots-${n}-mixed"
  mkdir -p "$out"
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  ./scripts/run-local-net.sh start
  ZONE_PID="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"
  set +e
  dotnet "$LT" \
    --scenario "$ROOT/tools/Fineage.LoadTest/scenarios/$scenario" \
    --spawn-layout spread \
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
done

echo "Stress reports: $REPORT_ROOT"