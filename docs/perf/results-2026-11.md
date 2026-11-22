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

## Regression on `dev` (Nov 21) — fixed

Fresh Postgres + `./scripts/run-local-net.sh` after prior bot runs **without** allocator sync:

| Scenario | Layout | Workers | In world | Disconnects | `fineage_zone_online` max (sampled) | Tick p50 / p99 | Command stage | Login/zone logs |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| **Before** (repro on current `dev`) | siege | 0 | **329/500** | **171** | **135** | ~25 / **500+ ms** overrun | **200–540 ms** `commands` stage during ramp | `handler_error` on create: **`23505 character_items_pkey`** |
| **After** (allocator sync + handoff prune + `run-local-net` stop without `lsof`) | siege | 0 | **500/500** | **0** | ramp to **500** (metrics scrape lags ramp) | **10 / 500 ms**, **1** overrun | brief **~407 ms** `commands` spike at ramp peak only | **no** `23505`, **no** explicit disconnect lines |

**Root cause:** `PostgresAccountStore.Open` restored `NextItemObjectId` from stale `store_snapshot` JSON while durable `character_items` rows already held those object ids. Load-test `RegisterIfMissing` + create then failed mid-handshake; zone never reached prior online counts. Secondary: `MemoryHandoffStore` pruned consumed tickets on every `TryConsume` (O(n) under ramp). `run-local-net.sh stop` required `lsof`, so Linux agents leaked Login listeners between runs.

**Fix split across PRs:** Postgres allocator sync (character create), handoff prune throttle, `ss`/`fuser` port cleanup in `scripts/run-local-net.sh`. Re-run JSON: `local-logs/loadtest/siege-after/report.json`.

## P1 (workers=4, 499/500 stuck) — resolution (Nov 21)

**Root cause: bad evidence, not a parallel-executor hang.** The FAIL row came from `local-logs/loadtest/--w/report.json` — a batch artifact written to a bad output path (`--w`) and overwritten by later runs in the same batch, so the "499 stuck" count belongs to a partially-written/overwritten report, not to a single coherent run. Two sampling flaws explain the supporting numbers:

- The metrics sampler only covers `rampUpSeconds + durationSeconds` (~180 s), while a 500-bot siege ramp takes ~10 minutes (each bot waits for the welcome burst plus a 400 ms quiet window; ~1–2 enters/s). `fineage_zone_online ≈ 153` was a mid-ramp sample, not the state at the failure moment.
- Tick p50/p99 for the "failing" run are identical to the passing rows because they were sampled over the same early-ramp window.

**Reproduction attempts on current `dev` (both PASS, 0 stuck):** `bots-500-siege` with `FINEAGE_ZONE_REGION_WORKERS=4` against a fresh database and against a warm database (all 500 bots already registered): 500/500 in world, 0 disconnects, 0 stuck in both. Managed stack captures taken by a metrics watchdog during the runs show no parked simulation or executor threads. Static review of the parallel path (`ParallelRegionExecutor` participates in its own `Parallel.For`, region bodies isolate exceptions into `StageFailure`, directory/affinity violations throw rather than block, fan-out from the sim thread is non-blocking `TrySend`) found no wait-cycle that could park a step.

**Residual hazards fixed anyway (this is the "fix" half of the P1):**

1. `RegionBodyScope.Enter` could leak a ThreadStatic region-directory body onto a pooled thread when `RecordRegion` threw mid-enter, permanently mis-attributing that thread's later writes. Enter is now exception-safe.
2. A step that never returns was indistinguishable from a slow one: there was no step watchdog. The zone now tracks in-flight region bodies (stage, region, managed thread, start timestamp) and a step watchdog (`FINEAGE_ZONE_STEP_WATCHDOG_SECONDS`, default 30) logs a concrete error naming the tick, the elapsed time, and every in-flight region body — the exact evidence the original P1 lacked.
3. Deterministic regression coverage: `RegionParallelStressTests` replays a seeded 180-mob/24-player churn scenario through the sequential and the 4-worker executor and asserts identical world state and identical fan-out, and drives a real simulation loop under concurrent handler-shaped traffic (commands, enters, leaves) asserting no stage failures and a consistent region directory and watcher index.

### Client RTT (50-bot smoke)

| p50 | p95 | p99 | max |
| ---: | ---: | ---: | ---: |
| 0.01 ms | 0.02 ms | 0.10 ms | 6.4 ms |

(Client RTT measures local action send time; zone fanout is mostly fire-and-forget.)

### Observations

- **Sequential regions (`FINEAGE_ZONE_REGION_WORKERS=0`)** handled 50–500 bots on this VM with all bots entering the world and no mass disconnects in completed runs.
- **`FINEAGE_ZONE_REGION_WORKERS=4`** at 500 bots was reported as **499/500 “stuck”** with ~153 online; on re-analysis that report was a mid-ramp, overwritten artifact (see "P1 resolution"). Two full 500-bot workers=4 soaks on current `dev` pass with zero stuck.
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

## Zone parallel stages (Nov 22) — movement + AI

**Scheduler / directory (builds on PR #843):** region bodies may call `Relocated` on entities they own; global `GridAoi` updates from movement defer to `RegionBodyScope.Commit` (ascending region order) so parallel stages do not race the shared spatial hash. Knownlist and fan-out side effects from movement defer via `AtBarrier` to the same commit.

**Parallel stages enabled:** `movement.player`, `movement.npc`, `random-walk`, `aggression` (existing parallel: `regen.*`, `abnormals`).

**Idle full-world micro-benchmark** (34k NPCs, no players, 50 samples after warm-up):

| `FINEAGE_ZONE_REGION_WORKERS` | Tick total p99 | `random-walk` p99 | `handoff` p99 |
| ---: | ---: | ---: | ---: |
| 0 | 4.38 ms | 0.31 ms | 1.51 ms |
| 4 | 4.37 ms | 0.23 ms | 1.45 ms |

## CI load-test-smoke (100-bot acceptance)

GitHub Actions job `load-test-smoke` (`build-and-test.yml`) runs on PRs: `RegionParallelStressTests` / parallel-world oracle, then `bots-100-spread` at `FINEAGE_ZONE_REGION_WORKERS` **0** and **4** via `scripts/ci-load-test-smoke.sh`. Set `CI_LOAD_TEST_SIEGE=1` on the job to add `bots-500-siege` (0 and 4) when the runner budget allows.

| Workers | In world | Stuck | Tick p95 (ms) | Tick p99 (ms) | `commands` stage p99 (ms) | Command→fanout p99 (ms) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 100 | 0 | 100 | 1000 | 1000 | 2833 |
| 4 | 100 | 0 | 100 | 1000 | 1000 | 2818 |

_Source: GitHub Actions `load-test-smoke` artifact `summary.json` from PR #855 run [44962713883](https://github.com/FioraSolutions/Fineage-Backend/actions/runs/44962713883) (`bots-100-spread`, workers 0 then 4). Tick and `commands` p99 hit the Prometheus histogram top bucket (1000 ms) during the 30 s bot ramp; steady tick p50 was 25 ms (w0) vs 10 ms (w4). Consistency oracle: 4950 pairwise checks, 0 stuck, 100/100 in world._

Local reproduction:

```bash
export FINEAGE_BUILD_CONFIGURATION=Release
./scripts/ci-load-test-smoke.sh
```

**Regression:** `RegionParallelWorldTests`, `RegionParallelStressTests`, and `RegionSimulationDeterminismTests` pass with the parallel movement/AI stages.

## Prioritized bugs / follow-ups

| P | Issue | Repro |
| --- | --- | --- |
| P1 | ~~Parallel region executor (`FINEAGE_ZONE_REGION_WORKERS=4`) at 500 bots: mass stuck clients~~ **Resolved** — overwritten mid-ramp report, not an executor hang; see "P1 resolution" above | not reproducible on current `dev` (2/2 clean 500-bot workers=4 soaks) |
| P2 | High `packets_rejected` under combat spam (synthetic targets) | 50-bot smoke, attack-weighted scenario |
| P3 | Load-test RTT percentile helper under-reports p50 when samples are sparse | See `ReportWriter.ClientRtt` — cosmetic report issue |

## Enter burst offload (Nov 22) — `request:enter` off simulation thread

**Before (profiling on `dev`, `bots-50-town`, ~34k NPCs, sequential regions):** `commands` stage **p99 650–826 ms** during bot ramp; enter planning did NetCodec for every known entity and peer fan-out inside `request:enter` on the simulation thread.

**After (this change, Release build, same scenario — VM has no Docker/Postgres here; numbers from CI `load-test-smoke` + `bots-50-town` local gate):**

| Workers | `commands` p99 (ms) | `fineage_enter_plan_ms` p99 | `fineage_enter_known_encode_ms` p99 | Command→fanout p50 (ms) | Command→fanout p99 (ms) |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | _CI_ | _CI_ | _CI_ | _CI_ | _CI_ |
| 4 | _CI_ | _CI_ | _CI_ | _CI_ | _CI_ |

Mechanism: `PrepareEnter` loads `CharacterRecord` off-thread → `CommitEnter` (sim) builds immutable `EnterSnapshot` + AOI/knownlist → `EncodeAndDispatchEnter` runs NetCodec on a worker with shared NPC spawn payload cache. Prometheus: `fineage_enter_*` histograms on zone `GET /metrics`.

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