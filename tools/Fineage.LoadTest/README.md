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

## Profile matrix and Release modes (#990)

```bash
./scripts/run-load-test.sh profile   # all profiles at YAML defaults
./scripts/run-load-test.sh fast      # 5-minute PR/perf matrix (Release)
./scripts/run-load-test.sh soak      # 20-30 minute nightly soak (Release)
./scripts/run-load-test.sh endurance # 1-2 hour weekly endurance (Release)
./scripts/run-load-test.sh stability # 8-24 hour dispatch/self-hosted only
```

Each scenario starts an independent Login+Zone stack, has a bounded timeout and writes its own report.
`matrix-summary.json` records commit, build, hardware and gate results. The five #990 profiles are
`pve-farming`, `party-farming`, `raid-boss-observe`, `reconnect-storm`, `full-world-1-player`; see
`docs/perf/acceptance-gates.md` for the workload and gate table.

Bounded overrides for a single run:

```bash
dotnet tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll \
  --scenario tools/Fineage.LoadTest/scenarios/full-world-1-player.yaml \
  --duration-seconds 600 --ramp-up-seconds 30 --bot-count 1 \
  --zone-pid "$(cat local-logs/zone-net.pid)" --require-gates
```

## Ramp matrix (10 → 50 → 100 → 200)

```bash
./scripts/run-load-ramp.sh
```

A player ceiling is only published from a recorded ramp; profiles and the matrix do not invent one.

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