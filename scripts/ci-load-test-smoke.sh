#!/usr/bin/env bash
# CI: consistency oracle + 100-bot load (sequential vs workers=4). Requires Postgres on 5432.
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
REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/ci-load-smoke}"
SUMMARY="${REPORT_ROOT}/summary.json"
mkdir -p "$REPORT_ROOT"

dotnet build "$ROOT/tools/Fineage.LoadTest" -c Release -v q -nologo

run_load() {
  local workers="$1"
  local tag="bots-50-town-w${workers}"
  local out="$REPORT_ROOT/$tag"
  mkdir -p "$out"

  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  export FINEAGE_ZONE_REGION_WORKERS="$workers"
  ./scripts/run-local-net.sh start

  local zone_pid
  zone_pid="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"
  dotnet "$LOADTEST_DLL" run \
    --scenario "$SCENARIO" \
    --spawn-layout spread \
    --target fineage \
    --report-dir "$out" \
    --login-log "$ROOT/local-logs/login-net.log" \
    --zone-log "$ROOT/local-logs/zone-net.log" \
    --zone-pid "$zone_pid" \
    --region-workers "$workers"

  echo "load-test-report: $out/load-test-report.md"
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
}

echo "== load: 50-town bots, workers=0 =="
run_load 0
echo "== load: 50-town bots, workers=4 =="
run_load 4

python3 - "$REPORT_ROOT" "$SUMMARY" <<'PY'
import json, sys
from pathlib import Path
root = Path(sys.argv[1])
summary_path = Path(sys.argv[2])

def read_run(tag):
    p = root / tag / "report.json"
    if not p.is_file():
        raise SystemExit(f"missing {p}")
    return json.loads(p.read_text())

def pick(r):
    s = r.get("Server") or {}
    c = r.get("Consistency") or {}
    h = (r.get("Honest") or {}).get("SteadyState") or {}
    return {
        "regionWorkers": r["RegionWorkers"],
        "passed": r["Passed"],
        "botsInWorld": r["BotsInWorld"],
        "botsStuck": r["BotsStuck"],
        "botsDisconnected": r["BotsDisconnected"],
        "tickP95Ms": h.get("TickP95Ms", s.get("TickP95Ms", 0)),
        "tickP99Ms": h.get("TickP99EstimateMs", h.get("TickP99Ms", s.get("TickP99Ms", 0))),
        "tickMaxMs": h.get("TickMaxEstimateMs", h.get("TickMaxMs", 0)),
        "commandsStageP99Ms": h.get("CommandsStageP99EstimateMs", h.get("CommandsStageP99Ms", s.get("CommandsStageP99Ms", 0))),
        "commandToMailboxesP99Ms": h.get("CommandToMailboxesP99Ms", 0),
        "fanoutDrainP99Ms": h.get("FanoutDrainP99Ms", 0),
        "clientAttackFanoutP95Ms": h.get("ClientAttackFanoutP95EstimateMs", 0),
        "clientAttackFanoutP99Ms": h.get("ClientAttackFanoutP99EstimateMs", 0),
        "clientSkillFanoutP95Ms": h.get("ClientSkillFanoutP95EstimateMs", 0),
        "clientSkillFanoutP99Ms": h.get("ClientSkillFanoutP99EstimateMs", 0),
        "fanoutDroppedTotal": h.get("FanoutDroppedTotal", 0),
        "socketWriteCountSteady": h.get("SocketWriteCountSteady", 0),
        "socketWriteFramesMeanSteady": h.get("SocketWriteFramesMeanSteady", 0),
        "clientCombatFanoutP99Ms": h.get("ClientCombatFanoutP99EstimateMs", h.get("ClientCombatFanoutP99Ms", c.get("SteadyBroadcastLatencyP99Ms", 0))),
        "clientCombatFanoutMaxMs": h.get("ClientCombatFanoutMaxMs", c.get("SteadyBroadcastLatencyMaxMs", 0)),
        "attackSampleCount": c.get("AttackSampleCount", 0),
        "skillSampleCount": c.get("SkillSampleCount", 0),
        "legacyCommandFanoutP99Ms": c.get("BroadcastLatencyP99Ms", 0),
    }

runs = {f"w{k}": pick(read_run(f"bots-50-town-w{k}")) for k in (0, 4)}
failures = []
for k, row in runs.items():
    if not row["passed"]:
        failures.append(f"{k}: report failed")
    if row["botsStuck"] != 0:
        failures.append(f"{k}: stuck={row['botsStuck']}")
    if row["botsInWorld"] < 50:
        failures.append(f"{k}: inWorld={row['botsInWorld']}/50")

if runs["w0"]["botsInWorld"] != runs["w4"]["botsInWorld"]:
    failures.append(f"oracle: inWorld {runs['w0']['botsInWorld']} vs {runs['w4']['botsInWorld']}")

summary = {"runs": runs, "failures": failures, "passed": not failures}
summary_path.write_text(json.dumps(summary, indent=2))
print(json.dumps(summary, indent=2))
if failures:
    raise SystemExit("load-test smoke failed: " + "; ".join(failures))
PY

if [[ "${CI_LOAD_TEST_SIEGE:-0}" == "1" ]]; then
  echo "== optional: 500-bot siege (workers 0 and 4) =="
  SIEGE="${ROOT}/tools/Fineage.LoadTest/scenarios/bots-500-siege.yaml"
  run_siege() {
    local workers="$1"
    local tag="bots-500-w${workers}"
    local out="$REPORT_ROOT/$tag"
    mkdir -p "$out"
    ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
    export FINEAGE_ZONE_REGION_WORKERS="$workers"
    ./scripts/run-local-net.sh start
    local zone_pid
    zone_pid="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"
    dotnet "$LOADTEST_DLL" run \
      --scenario "$SIEGE" \
      --spawn-layout siege \
      --target fineage \
      --report-dir "$out" \
      --login-log "$ROOT/local-logs/login-net.log" \
      --zone-log "$ROOT/local-logs/zone-net.log" \
      --zone-pid "$zone_pid" \
      --region-workers "$workers" || echo "siege w${workers} failed (non-fatal in extended smoke)"
    ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
  }
  run_siege 0
  run_siege 4
fi

echo "load-test smoke OK; summary at $SUMMARY"
for tag in bots-50-town-w0 bots-50-town-w4; do
  if [[ -f "$REPORT_ROOT/$tag/load-test-report.md" ]]; then
    echo "  $REPORT_ROOT/$tag/load-test-report.md"
  fi
done