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

## Economy hammer (#955)

`scenarios/bots-economy-hammer.yaml` drives NPC shop buy/sell, warehouse deposit, P2P trade (adena), private sell/buy stores, manufacture listings, manor seed purchase, and drop/pickup. Flows use Mobius CT0 Interlude wire messages and exercise the auditable economy ledger.

After the run, `EconomyConsistencyOracle` fails hard on unexplained zone `reject` codes, zone log ledger/invariant lines, `fineage_ledger_invariant_violations_total`, or `fineage_ledger_flush_failures_total` (when `economy.requireLedgerInvariant` is true).

Start Zone with `FINEAGE_LOADTEST_ECONOMY_HAMMER=1` so dwarf manufacturers (bot id % 6 == 0) and manufacture customers (bot id % 6 == 1) are provisioned on enter.

## Identity namespace

Accounts are `<accountPrefix><botId:D5>`. Character names are derived from the same prefix, because Login keeps one
global case-insensitive character-name index: a second run with a fresh account prefix must not try to create the
first run's names. The default `loadbot` prefix keeps the historical `Bot0000` names; every other prefix gets
`<readable prefix><stable prefix hash><botId:D4>` (3-16 alphanumerics, deterministic across reconnects and reruns).
An account that already has a character reuses its roster instead of creating another one.

## Safety

Run only against **local** `./scripts/run-local-net.sh`. Do not point bots at public hosts.