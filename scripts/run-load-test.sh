#!/usr/bin/env bash
# Reproducible load-profile matrix against a local Fineage stack (never public servers).
#
# Modes:
#   smoke      one short 20-bot consistency run
#   fast       5-minute regression matrix (all #990 profiles)
#   soak       20-30 minute Release soak (all #990 profiles)
#   endurance  1-2 hour Release endurance (all #990 profiles)
#   stability  8-24 hour Release stability (full-world-1-player by default; self-hosted/dispatch only)
#   profile    every profile in the #990 table at its YAML default duration
#   <name>     one scenario name from tools/Fineage.LoadTest/scenarios
#
# Every scenario gets a fresh Login+Zone process, a bounded wall-clock timeout and its own report.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${HOME}/.dotnet:${PATH}"

MODE="${1:-profile}"
BUILD_CONFIGURATION="${FINEAGE_BUILD_CONFIGURATION:-Release}"
export FINEAGE_BUILD_CONFIGURATION="$BUILD_CONFIGURATION"
REPORT_ROOT="${REPORT_ROOT:-$ROOT/local-logs/loadtest-runs/${MODE}-$(date -u +%Y%m%dT%H%M%SZ)}"
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
export FINEAGE_ZONE_REGION_WORKERS="${FINEAGE_ZONE_REGION_WORKERS:-0}"
export FINEAGE_GEODATA_REQUIRED="${FINEAGE_GEODATA_REQUIRED:-0}"
export FINEAGE_STARTING_LEVEL="${FINEAGE_STARTING_LEVEL:-77}"
export FINEAGE_STARTING_ADENA="${FINEAGE_STARTING_ADENA:-5000000}"
EXTRA_MAPS_BASE="${FINEAGE_ZONE_EXTRA_MAPS:-}"

PROFILE_SCENARIOS=(
  bots-50-town
  bots-200-spread
  pve-farming
  perf-aoe-train
  party-farming
  bots-economy-hammer
  raid-boss-observe
  bots-500-siege
  reconnect-storm
  full-world-1-player
)
# The five profiles added by #990. Fast/soak/endurance run these first.
NEW_PROFILES=(pve-farming party-farming raid-boss-observe reconnect-storm full-world-1-player)

DURATION="${SCENARIO_DURATION_SECONDS:-}"
RAMP="${SCENARIO_RAMP_SECONDS:-}"
BOT_COUNT=""
SCENARIOS=()
TIMEOUT_SECONDS="${SCENARIO_TIMEOUT_SECONDS:-0}"

case "$MODE" in
  smoke)
    SCENARIOS=(bots-20-smoke)
    DURATION="${SMOKE_DURATION_SECONDS:-45}"
    RAMP="${SMOKE_RAMP_SECONDS:-10}"
    ;;
  fast)
    SCENARIOS=("${NEW_PROFILES[@]}")
    DURATION="${FAST_DURATION_SECONDS:-60}"
    RAMP="${FAST_RAMP_SECONDS:-15}"
    ;;
  soak)
    SCENARIOS=("${NEW_PROFILES[@]}")
    DURATION="${SOAK_DURATION_SECONDS:-240}"
    RAMP="${SOAK_RAMP_SECONDS:-30}"
    ;;
  endurance)
    SCENARIOS=("${NEW_PROFILES[@]}")
    DURATION="${ENDURANCE_DURATION_SECONDS:-900}"
    RAMP="${ENDURANCE_RAMP_SECONDS:-60}"
    ;;
  stability)
    SCENARIOS=(${STABILITY_SCENARIOS:-full-world-1-player})
    DURATION="${STABILITY_DURATION_SECONDS:-28800}"
    RAMP="${STABILITY_RAMP_SECONDS:-120}"
    ;;
  profile|matrix)
    SCENARIOS=("${PROFILE_SCENARIOS[@]}")
    ;;
  *)
    scenario_file="$ROOT/tools/Fineage.LoadTest/scenarios/${MODE%.yaml}.yaml"
    if [[ ! -f "$scenario_file" ]]; then
      echo "unknown mode/scenario '$MODE'; see scripts/run-load-test.sh header" >&2
      exit 2
    fi
    SCENARIOS=("${MODE%.yaml}")
    ;;
esac

if [[ "$TIMEOUT_SECONDS" -le 0 ]]; then
  TIMEOUT_SECONDS=$((DURATION + RAMP + 240))
  if [[ -z "$DURATION" ]]; then
    TIMEOUT_SECONDS="${SCENARIO_TIMEOUT_SECONDS_DEFAULT:-900}"
  fi
fi

WORKERS=(${MATRIX_WORKERS:-0})

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  dotnet build "$ROOT/src/Fineage.Login" -c "$BUILD_CONFIGURATION" -v q -nologo
  dotnet build "$ROOT/src/Fineage.Zone" -c "$BUILD_CONFIGURATION" -v q -nologo
  dotnet build "$ROOT/tools/Fineage.LoadTest" -c "$BUILD_CONFIGURATION" -v q -nologo
fi

LOADTEST_DLL="$ROOT/tools/Fineage.LoadTest/bin/$BUILD_CONFIGURATION/net10.0/Fineage.LoadTest.dll"
if [[ ! -f "$LOADTEST_DLL" ]]; then
  echo "MISSING: $LOADTEST_DLL (build tools/Fineage.LoadTest -c $BUILD_CONFIGURATION)" >&2
  exit 1
fi

stop_stack() {
  ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
}
trap stop_stack EXIT

run_bounded() {
  local seconds="$1"
  shift
  python3 - "$seconds" "$@" <<'PY'
import subprocess, sys
limit = float(sys.argv[1])
try:
    raise SystemExit(subprocess.call(sys.argv[2:], timeout=limit))
except subprocess.TimeoutExpired:
    print(f"TIMEOUT after {limit:.0f}s: {' '.join(sys.argv[2:])}", file=sys.stderr)
    raise SystemExit(124)
PY
}

scenario_env() {
  local scenario="$1"
  case "$scenario" in
    pve-farming|party-farming|raid-boss-observe|perf-aoe-train|perf-aoe-multiparty|perf-aoe-combined)
      export FINEAGE_ZONE_LOADTEST_COMBAT=1
      export FINEAGE_ZONE_EXTRA_MAPS="loadtest_aoe_arena${EXTRA_MAPS_BASE:+ $EXTRA_MAPS_BASE}"
      ;;
    *)
      export FINEAGE_ZONE_LOADTEST_COMBAT=0
      export FINEAGE_ZONE_EXTRA_MAPS="$EXTRA_MAPS_BASE"
      ;;
  esac
  if [[ "$scenario" == bots-economy-hammer || "$scenario" == bots-economy-hammer-vm ]]; then
    export FINEAGE_LOADTEST_ECONOMY_HAMMER=1
  else
    unset FINEAGE_LOADTEST_ECONOMY_HAMMER
  fi
  export FINEAGE_ZONE_LOADTEST_TELEPORT=1
}

run_one() {
  local workers="$1"
  local scenario="$2"
  local duration="$3"
  local ramp="$4"
  local tag="${scenario}-w${workers}${duration:+-d${duration}}"
  local out="$REPORT_ROOT/$tag"
  mkdir -p "$out"

  scenario_env "$scenario"
  export FINEAGE_ZONE_REGION_WORKERS="$workers"
  stop_stack
  if ! ./scripts/run-local-net.sh start; then
    echo "FAIL: local stack was not listening (zone boot may still have been in progress)" >&2
    stop_stack
    return 1
  fi

  local zone_pid
  zone_pid="$(cat local-logs/zone-net.pid 2>/dev/null || echo 0)"
  local args=(
    run
    --scenario "$ROOT/tools/Fineage.LoadTest/scenarios/${scenario}.yaml"
    --target fineage
    --report-dir "$out"
    --login-log "$ROOT/local-logs/login-net.log"
    --zone-log "$ROOT/local-logs/zone-net.log"
    --zone-pid "$zone_pid"
    --region-workers "$workers"
    --require-gates
  )
  [[ -n "$duration" ]] && args+=(--duration-seconds "$duration")
  [[ -n "$ramp" ]] && args+=(--ramp-up-seconds "$ramp")
  [[ -n "$BOT_COUNT" ]] && args+=(--bot-count "$BOT_COUNT")

  echo "== $tag: ${BUILD_CONFIGURATION}, workers=$workers, duration=${duration:-yaml}, timeout=${TIMEOUT_SECONDS}s =="
  set +e
  run_bounded "$TIMEOUT_SECONDS" dotnet "$LOADTEST_DLL" "${args[@]}"
  local status=$?
  set -e
  stop_stack

  if [[ ! -f "$out/load-test-report.md" ]]; then
    echo "FAIL: no report under $out (exit $status)" >&2
    return 1
  fi
  echo "load-test-report: $out/load-test-report.md"
  if [[ "$status" -ne 0 ]]; then
    echo "FAIL: load test exit $status ($out/load-test-report.md)" >&2
    return "$status"
  fi
  return 0
}

failures=0
for workers in "${WORKERS[@]}"; do
  for scenario in "${SCENARIOS[@]}"; do
    if ! run_one "$workers" "$scenario" "$DURATION" "$RAMP"; then
      failures=$((failures + 1))
    fi
  done
done

python3 - "$REPORT_ROOT" "$MODE" "$BUILD_CONFIGURATION" "$DURATION" "$RAMP" "$failures" <<'PY'
import json, os, platform, subprocess, sys
from datetime import datetime, timezone
from pathlib import Path

root, mode, build, duration, ramp, failures = sys.argv[1:7]
root = Path(root)
runs = []
for report_path in sorted(root.glob("*/report.json")):
    try:
        report = json.loads(report_path.read_text())
    except Exception as exc:
        runs.append({"tag": report_path.parent.name, "error": str(exc), "report": str(report_path)})
        continue
    env = report.get("Environment") or {}
    failed_gates = [g for g in report.get("GateVerdicts", []) if not g.get("Passed")]
    runs.append({
        "tag": report_path.parent.name,
        "scenario": report.get("ScenarioName"),
        "passed": report.get("Passed"),
        "gatesPassed": not failed_gates,
        "failedGates": [g.get("Gate") for g in failed_gates],
        "bots": report.get("BotCount"),
        "durationSeconds": report.get("DurationSeconds"),
        "rampUpSeconds": report.get("RampUpSeconds"),
        "report": str(report_path),
        "zoneBuildCommit": env.get("ZoneBuildCommit"),
        "zoneBuildConfiguration": env.get("ZoneBuildConfiguration"),
        "rssMbEnd": ((report.get("ZoneProcess") or {}).get("RssBytes") or 0) / (1024 * 1024),
        "timeSeriesBacklogTrend": report.get("TimeSeriesBacklogTrend"),
        "environment": env,
    })

first_env = next((r.get("environment") or {} for r in runs if r.get("environment")), {})
commit = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
status = subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True).stdout.strip()
summary = {
    "schema": 1,
    "generatedAt": datetime.now(timezone.utc).isoformat(),
    "mode": mode,
    "buildConfiguration": build,
    "requestedDurationSeconds": int(duration) if duration else None,
    "requestedRampUpSeconds": int(ramp) if ramp else None,
    "failures": int(failures),
    "git": {"commit": commit, "dirty": bool(status)},
    "host": {
        "platform": platform.platform(),
        "python": platform.python_version(),
        "processorCount": first_env.get("ProcessorCount"),
        "totalRamMb": first_env.get("TotalRamMb"),
        "os": first_env.get("OsDescription"),
        "dotnet": first_env.get("DotNetVersion"),
    },
    "environment": first_env,
    "runs": runs,
}
out = root / "matrix-summary.json"
out.write_text(json.dumps(summary, indent=2))
md = root / "matrix-summary.md"
lines = [f"# Load-test matrix: {mode}", "", f"- Build: **{build}**", f"- Commit: `{commit}`" + (" (dirty)" if status else ""),
         f"- Host: {summary['host']['processorCount']} cores / {summary['host']['totalRamMb']} MiB RAM / {summary['host']['os']}",
         "", "| scenario | bots | duration | runner | gates | report |", "| --- | ---: | ---: | --- | --- | --- |"]
for r in runs:
    lines.append(f"| {r.get('tag')} | {r.get('bots', '?')} | {r.get('durationSeconds', '?')}s | "
                 f"{'PASS' if r.get('passed') else 'FAIL'} | {'PASS' if r.get('gatesPassed') else 'FAIL'} | `{r.get('report')}` |")
md.write_text("\n".join(lines) + "\n")
print(f"matrix summary: {out}")
print(f"matrix report:  {md}")
PY

if [[ "$failures" -ne 0 ]]; then
  echo "FAIL: $failures scenario(s) failed or produced no report; see $REPORT_ROOT/matrix-summary.md" >&2
  exit 2
fi

echo "Reports under $REPORT_ROOT"
