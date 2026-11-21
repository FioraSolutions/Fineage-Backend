# Fineage.LoadTest

Headless Net bots (same `PacketEnvelope` protocol as production clients) for local Login+Zone soak and **consistency oracle** checks.

## Prerequisites

```bash
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot
export FINEAGE_LOGIN_LOADTEST_PASSWORD=loadtest-pass-v1
export FINEAGE_LOGIN_METRICS_PORT=9464
export FINEAGE_ZONE_METRICS_PORT=9465
./scripts/run-local-net.sh start
```

## Run one scenario

```bash
dotnet build tools/Fineage.LoadTest -c Release
dotnet tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll \
  --scenario tools/Fineage.LoadTest/scenarios/bots-50-town.yaml \
  --report-dir local-logs/loadtest/run1 \
  --login-log local-logs/login-net.log \
  --zone-log local-logs/zone-net.log \
  --zone-pid "$(cat local-logs/zone-net.pid)" \
  --region-workers 0
```

## Ramp matrix (10 → 50 → 100 → 200)

```bash
./scripts/run-load-ramp.sh
```

## Consistency oracle

Each bot records inbound zone frames (spawn/despawn, move, status, combat, social). After the scenario, `ConsistencyOracle` cross-checks observers in AOI for ghosts, missing peer spawns, HP/position skew, combat fanout gaps, and visibility leaks. Results land in `report.json` / `report.md` under `consistency`.

## Safety

Run only against **local** `./scripts/run-local-net.sh`. Do not point bots at public hosts.