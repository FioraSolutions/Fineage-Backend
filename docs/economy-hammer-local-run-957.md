# Economy hammer local run (PR #957)

## Zone (Debug)

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$DOTNET_ROOT:$PATH"
export FINEAGE_DATABASE_URL="Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage"
export FINEAGE_HANDOFF_SECRET=FineageLoginHandoffV1
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot
export FINEAGE_LOGIN_LOADTEST_PASSWORD=loadtest-pass-v1
export FINEAGE_LOGIN_METRICS_PORT=9464
export FINEAGE_ZONE_METRICS_PORT=9465
export FINEAGE_LOADTEST_ECONOMY_HAMMER=1

FINEAGE_BUILD_CONFIGURATION=Debug FINEAGE_GEODATA_REQUIRED=0 ./scripts/run-local-net.sh start
```

## Load test

```bash
export FINEAGE_LOADTEST_ECONOMY_HAMMER=1
dotnet run --project tools/Fineage.LoadTest/Fineage.LoadTest.csproj -c Debug -- run \
  --scenario tools/Fineage.LoadTest/scenarios/bots-economy-hammer.yaml \
  --report-dir local-logs/loadtest/economy-hammer \
  --login-log local-logs/login-net.log \
  --zone-log local-logs/zone-net.log \
  --zone-pid "$(cat local-logs/zone-net.pid)" \
  --region-workers 0
```

## VM Debug run (2026-11-23, commit `0a0e90d4` + attribution allowlist)

Scenario: `bots-economy-hammer` — **24 bots**, **20s ramp + 90s** steady, **spawnLayout: economy** (TI hub).

| Metric | Value |
| --- | ---: |
| Bots in world / disconnects | **24** / **0** |
| Wall window | **~118s** |
| Tick p50 / p99 / max (prometheus aggregate) | **11.4 ms** / **24.7 ms** / **92.6 ms** |
| `fineage_ledger_invariant_violations_total` (peak) | **0** |
| `fineage_ledger_flush_failures_total` (peak) | **0** |
| Economy flows attempted / succeeded | **2364** / **1208** |
| Explained / unexplained economy rejects | **2788** / **0** |
| Economy oracle hard issues | **0** (UnexpectedReject cleared) |

**Note:** Overall runner may still FAIL on unrelated zone log noise (missing geodata files, zone XML warnings). Economy oracle + ledger invariants are clean on this run.

**Weights (scenario):** `economy_hub` 5%, shop buy/sell 14/10%, warehouse 8%, trade 12%, store 14%, manufacture 4/4%, manor 7%, pickup/drop 10%, move 12%.