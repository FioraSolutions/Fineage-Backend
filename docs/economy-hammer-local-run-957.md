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
dotnet tools/Fineage.LoadTest/bin/Debug/net10.0/Fineage.LoadTest.dll run \
  --scenario tools/Fineage.LoadTest/scenarios/bots-economy-hammer.yaml \
  --report-dir local-logs/loadtest/economy-hammer \
  --login-log local-logs/login-net.log \
  --zone-log local-logs/zone-net.log \
  --zone-pid "$(cat local-logs/zone-net.pid)" \
  --region-workers 0
```

## VM Debug run (2026-11-23, commit during PR work)

Scenario: `bots-economy-hammer-vm` (12 bots, 12s ramp + 45s steady, **spawn layout was `town` before loader fix** — use `spawnLayout: economy` or parent merge).

| Metric | Value |
| --- | ---: |
| Bots | 12 in world, 0 disconnects |
| Window | 57s |
| Tick p50 / p99 (aggregate scrape) | **9.3 ms** / **85.0 ms** |
| `fineage_ledger_invariant_violations_total` (peak) | **0** |
| `fineage_ledger_flush_failures_total` | **0** |
| Economy flows attempted / succeeded | 0 / 0 (scenario `economy.enabled` + weights not applied on extends child — fixed in this PR) |
| Explained economy rejects (counter) | 964 |
| Economy oracle hard issues | 82 (`unknown_opcode`, combat/pickup rejects from non-economy weights while rejecting all bot traffic) |

**Takeaway:** metrics + ledger invariants look healthy under 12-bot soak; remaining work on this PR is economy-only reject attribution, full `economy_*` weight execution with TI hub spawn, and manufacturing provisioning via `FINEAGE_LOADTEST_ECONOMY_HAMMER=1`.