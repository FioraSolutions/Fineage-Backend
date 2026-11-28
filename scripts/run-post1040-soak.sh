#!/usr/bin/env bash
# Clean, build-stamped post-#1040 acceptance soak (bots-200-town-iso-soak).
# Requires a committed source tree: the Zone build-info commit/module and the --zone-dll file MVID must agree
# with HEAD, and the report fails (not just warns) when any of them is missing or was rebuilt under the running Zone.
# This script starts the Zone itself, so it rebuilds before the process starts; never rebuild while it runs.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
SOURCE_PATHS=(
  src/Fineage.Zone src/Fineage.Protocol src/Fineage.Game src/Fineage.Accounts
  src/Fineage.Net.Host src/Fineage.Net.Contract src/Fineage.Login
  tools/Fineage.LoadTest
  '*.props' '*.targets'
)
if [[ -n "$(git status --porcelain -- "${SOURCE_PATHS[@]}")" ]]; then
  echo "refusing: compiled source is dirty; commit or stash it before a before/after soak" >&2
  exit 2
fi
# The scenario is a read-only input and lives with the original artifacts; pass SCENARIO when it is outside ROOT.
SCENARIO="${SCENARIO:-$ROOT/local-scenarios/bots-200-town-iso-soak.yaml}"
if [[ ! -f "$SCENARIO" ]]; then
  echo "missing scenario: $SCENARIO (set SCENARIO=/path/to/bots-200-town-iso-soak.yaml)" >&2
  exit 1
fi

export FINEAGE_BUILD_CONFIGURATION="${FINEAGE_BUILD_CONFIGURATION:-Debug}"
export FINEAGE_ZONE_REGION_WORKERS=0
export FINEAGE_NET_LOGIN_PORT="${FINEAGE_NET_LOGIN_PORT:-19106}"
export FINEAGE_NET_ZONE_PORT="${FINEAGE_NET_ZONE_PORT:-19777}"
export FINEAGE_LOGIN_METRICS_PORT="${FINEAGE_LOGIN_METRICS_PORT:-9464}"
export FINEAGE_ZONE_METRICS_PORT="${FINEAGE_ZONE_METRICS_PORT:-9465}"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX="${FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX:-loadbot}"
export FINEAGE_LOGIN_LOADTEST_PASSWORD="${FINEAGE_LOGIN_LOADTEST_PASSWORD:-loadtest-pass-v1}"

BUILD_CFG="$FINEAGE_BUILD_CONFIGURATION"
ZONE_DLL="$ROOT/src/Fineage.Zone/bin/$BUILD_CFG/net10.0/Fineage.Zone.dll"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
REPORT_DIR="$ROOT/local-logs/post1040-200-town-$STAMP"
mkdir -p "$REPORT_DIR"
cleanup() { ./scripts/run-local-net.sh stop >/dev/null 2>&1 || true; }
trap cleanup EXIT

dotnet build "$ROOT/tools/Fineage.LoadTest" -c "$BUILD_CFG" -v q -nologo
./scripts/run-local-net.sh stop >/dev/null 2>&1 || true
./scripts/run-local-net.sh start
ZONE_PID="$(cat "$ROOT/local-logs/zone-net.pid")"
if [[ ! -f "$ZONE_DLL" ]]; then
  echo "missing $ZONE_DLL" >&2
  exit 1
fi
EXPECTED="$(git rev-parse HEAD)"
echo "zone pid=$ZONE_PID dll=$ZONE_DLL expect=$EXPECTED report=$REPORT_DIR"

dotnet run --project "$ROOT/tools/Fineage.LoadTest" -c "$BUILD_CFG" --no-build -- \
  --scenario "$SCENARIO" \
  --target fineage \
  --spawn-layout town \
  --region-workers 0 \
  --zone-pid "$ZONE_PID" \
  --zone-dll "$ZONE_DLL" \
  --expect-zone-commit "$EXPECTED" \
  --require-clean-provenance \
  --login-log "$ROOT/local-logs/login-net.log" \
  --zone-log "$ROOT/local-logs/zone-net.log" \
  --report-dir "$REPORT_DIR"
