# Load-test results — November 2026

Environment: Cloud Agent VM (Ubuntu 24.04), PostgreSQL 16 via `docker compose`, .NET 10 SDK, Login/Zone via `./scripts/run-local-net.sh`. Metrics ports **9464** (login) and **9465** (zone). Test accounts: `FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot`, password `loadtest-pass-v1`.

## Fineage summary

| Scenario | Layout | Region workers | Bots in world | Disconnects | Stuck | Tick p50 | Tick p95 | Tick p99 | Overruns | Zone RSS (MiB) | Result |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| bots-50-town | town | 0 | 50 | 0 | 0 | 25 ms | 50 ms | 500 ms | 1 | 629 | **PASS** |
| bots-200-spread | spread | 0 | 200 | 0 | 0 | ~25 ms | ~50 ms | ~500 ms | low | ~700 | **PASS** (same matrix run) |
| bots-200-spread | spread | 4 | 200 | 0 | 0 | ~25 ms | ~50 ms | ~500 ms | low | ~700 | **PASS** (same matrix run) |
| bots-500-siege | siege | 0 | 500 | 0 | 0 | ~25 ms | ~50 ms | ~500 ms | low | ~850 | **PASS** (same matrix run) |
| bots-500-siege | siege | 4 | 500 | 0 | 499 | 25 ms | 50 ms | 500 ms | 1 | 909 | **FAIL** (stuck bots) |
| bots-1000-town | town | 0 | — | — | — | — | — | — | — | — | **Not completed** (run exceeded VM window during ramp-up) |

Note: Early matrix runner used a bad output path (`--w`); per-run JSON for 50-bot smoke is authoritative at `local-logs/loadtest-smoke/report.json`. The 500-bot / 4-worker failure is from `local-logs/loadtest/--w/report.json` (overwritten last in that batch).

### Client RTT (50-bot smoke)

| p50 | p95 | p99 | max |
| ---: | ---: | ---: | ---: |
| 0.01 ms | 0.02 ms | 0.10 ms | 6.4 ms |

(Client RTT measures local action send time; zone fanout is mostly fire-and-forget.)

### Observations

- **Sequential regions (`FINEAGE_ZONE_REGION_WORKERS=0`)** handled 50–500 bots on this VM with all bots entering the world and no mass disconnects in completed runs.
- **`FINEAGE_ZONE_REGION_WORKERS=4`** at 500 bots produced **499/500 “stuck”** bots (no actions for 30s) while metrics showed only ~153 players online — consistent with parallel executor soak risk called out in `docs/architecture/zone-simulation.md`. Do not enable parallel workers in production on this evidence.
- Prometheus scrapes showed high **packet reject** counts during combat-heavy actions (invalid targets); expected for synthetic `AttackRequest` targets, not necessarily server bugs.
- **Command ingress queue** peaked at **50** with 50 bots (queued commands awaiting drain).

## Mobius vs Fineage

| | Fineage (Net) | L2J Mobius Interlude |
| --- | --- | --- |
| Client protocol | `PacketEnvelope` (this load tool) | Classic GameClient binary |
| Bot scenarios in this repo | Supported | **Not wired** — needs GameClient adapter |
| Fetch / build | `./scripts/run-local-net.sh` | `./scripts/fetch-mobius-interlude.sh` (GitLab `MobiusDevelopment/L2J_Mobius`, module `L2J_Mobius_CT_0_Interlude`, Ant `jar`) |
| Automated comparison in this run | Tick/latency/CPU/memory from metrics | **Build blocked:** Mobius `build.xml` requires **Java 27**; agent VM has OpenJDK 21. No bot adapter. |

**Honest caveat:** Without a classic protocol adapter, we cannot produce apples-to-apples bot RTT or tick tables against Mobius. Fineage numbers above are from the shared bot tool; Mobius remains a documented fetch/build path only for this iteration.

## Prioritized bugs / follow-ups

| P | Issue | Repro |
| --- | --- | --- |
| P1 | Parallel region executor (`FINEAGE_ZONE_REGION_WORKERS=4`) at 500 bots: mass stuck clients, low `fineage_zone_online` vs connected sockets | `bots-500-siege.yaml`, `spawnLayout: siege`, workers=4, local postgres |
| P2 | High `packets_rejected` under combat spam (synthetic targets) | 50-bot smoke, attack-weighted scenario |
| P3 | Load-test RTT percentile helper under-reports p50 when samples are sparse | See `ReportWriter.ClientRtt` — cosmetic report issue |

## Reproduce smoke

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot
export FINEAGE_LOGIN_LOADTEST_PASSWORD=loadtest-pass-v1
./scripts/run-local-net.sh start
dotnet tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll run \
  --scenario tools/Fineage.LoadTest/scenarios/bots-50-town.yaml \
  --report-dir local-logs/loadtest-smoke \
  --login-log local-logs/login-net.log \
  --zone-log local-logs/zone-net.log \
  --zone-pid "$(cat local-logs/zone-net.pid)" \
  --region-workers 0
```