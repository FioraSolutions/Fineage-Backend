#!/usr/bin/env bash
# Run a load-test matrix against a local Fineage stack (never public servers).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${HOME}/.dotnet:${PATH}"

MATRIX="${1:-matrix}"
REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/loadtest-runs/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$REPORT_ROOT"

export FINEAGE_DATABASE_URL="${FINEAGE_DATABASE_URL:-Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage}"
export FINEAGE_HANDOFF_SECRET="${FINEAGE_HANDOFF_SECRET:-FineageLoginHandoffV1}"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="${FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX:-loadbot}"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="${FINEAGE_LOGIN_LOADTEST_PASSWORD:-loadtest-pass-v1}"
export FINEAGE_LOGIN_METRICS_PORT="${FINEAGE_LOGIN_METRICS_PORT:-9464}"
export FINEAGE_ZONE_METRICS_PORT="${FINEAGE_ZONE_METRICS_PORT:-9465}"
export FINEAGE_LOGIN_MAX_CONNECTIONS="${FINEAGE_LOGIN_MAX_CONNECTIONS:-4096}"
export FINEAGE_ZONE_MAX_CONNECTIONS="${FINEAGE_ZONE_MAX_CONNECTIONS:-4096}"
export FINEAGE_LOGIN_MAX_MSG_PER_SEC="${FINEAGE_LOGIN_MAX_MSG_PER_SEC:-600}"
export FINEAGE_ZONE_MAX_MSG_PER_SEC="${FINEAGE_ZONE_MAX_MSG_PER_SEC:-600}"

dotnet build "$ROOT/tools/Fineage.LoadTest" -c Release -v q -nologo

run_one() {
  local workers="$1"
  local scenario="$2"
  local layout="$3"
  local tag="${scenario}-${layout}-w${workers}"
  local out="$REPORT_ROOT/$tag"
  mkdir -p "$out"

  export FINEAGE_ZONE_REGION_WORKERS="$workers"
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  ./scripts/run-local-net.sh start

  ZONE_PID="$(cat local-logs/zone-net.pid 2>/dev/null || true)"
  export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX FINEAGE_LOGIN_LOADTEST_PASSWORD

  SCENARIO_FILE="$ROOT/tools/Fineage.LoadTest/scenarios/${scenario}.yaml"

  dotnet "$ROOT/tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll" run \
    --scenario "$SCENARIO_FILE" \
    --spawn-layout "$layout" \
    --target fineage \
    --report-dir "$out" \
    --login-log "$ROOT/local-logs/login-net.log" \
    --zone-log "$ROOT/local-logs/zone-net.log" \
    --zone-pid "${ZONE_PID:-0}" \
    --region-workers "$workers" \
    || true

  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  cp -f "$out/report.json" "$REPORT_ROOT/${tag}.json" 2>/dev/null || true
  cp -f "$out/report.md" "$REPORT_ROOT/${tag}.md" 2>/dev/null || true
}

if [[ "$MATRIX" == "smoke" ]]; then
  run_one 0 bots-50-town town
  exit 0
fi

for w in 0 4; do
  for scenario in bots-50-town bots-200-spread bots-500-siege bots-1000-town; do
    for layout in town spread siege; do
      run_one "$w" "$scenario" "$layout"
    done
  done
done

echo "Reports under $REPORT_ROOT"