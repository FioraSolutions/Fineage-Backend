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

## NPC movement hot path (Nov 22) — region sleep + deferred paths

**Symptom (Debug playtest, 1 player in Giran Death Pass, ~34k NPCs):** simulation tick **260–410 ms** (budget 200 ms); **`movement.npc` 180–300 ms**; **`random-walk` ~12 ms**; **`thinking` ~2729** with **`playerRegions=9`**.

**Profile / root causes:**

| Cost | Before |
| --- | --- |
| `RefreshNpcWatchers` | Full `Players()` scan per moving NPC every **250** L2 units |
| `AdvanceMove` | `GetHeight` every tick; `sqrt` per step; environment hooks on NPCs |
| Inactive regions | Idle wanderers in `Moving` set still advanced every tick off-screen |
| Pathfinding | `FindPath` inline inside `StartNpcMove` on the movement/think thread |
| Diagnostics | `TickNpcDiagnostics` scanned all `_byId` NPCs on overrun logs |

**Mobius-aligned runtime (this change):**

- **`NpcRegionAwake`**: one predicate (player-active tile + neighbours, live watcher, aggro/return-home, drifted beyond 2× `MaxDriftRange`).
- **Sleep:** `SuspendNpcRegion` stops think + idle movement (state/XY preserved); combat/chase/return-home/watched mobs stay awake.
- **Movement registry only:** stages iterate `ActiveSets.Moving` / `Thinking`, not the full spawn table.
- **`npc.paths` stage (before `movement.npc`):** MoveCheck on order; blocked legs enqueue A* with **48/tick budget** + LRU waypoint cache; no inline `FindPath` in `StartNpcMove`.
- **Incremental move:** grid-cell knownlist refresh; watcher-index AOI updates; geo height only on cell change; no per-tick `Players()` scan.

**`ZoneHuntingMovementPerfTests` (full enabled maps, player at Death Pass, 300 samples after 120-step warm-up):**

| Configuration | Tick p50 | Tick p99 | `movement.npc` p50 | `movement.npc` p99 | `random-walk` p99 | NPCs |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| **Debug** (primary gate) | 6.6 ms | **7.8 ms** | 0.05 ms | **0.10 ms** | 0.65 ms | 34 133 |
| **Release** (secondary) | 3.4 ms | **5.7 ms** | 0.05 ms | **0.11 ms** | 0.49 ms | 34 133 |

Debug budgets enforced in CI perf job: tick p99 **&lt; 50 ms**, `movement.npc` p99 **&lt; 15 ms** (Release: **&lt; 20 ms** / **&lt; 5 ms**).

**Behavior locks:** `ZoneNpcRegionSleepBehaviorTests` (teleport into sleeping tile → mobs at spawn, then fight; chase within forget range while aggroed; drifted return-home).

`run-local-net.sh` stays **Debug** for playtest logs (`BUILD_CFG=${FINEAGE_BUILD_CONFIGURATION:-Debug}` unchanged).

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

## Command-commit fan-out dispatcher (Nov 22) — local baseline

Branch `cursor/command-fanout-dispatcher-2d42`: replaces end-of-step synchronous `WorldFanoutSequencer.Drain()` (PR #852) with **sealed batches enqueued at command commit** to `FanoutDispatcher`, classed ingress (`FINEAGE_ZONE_COMMAND_APPLY_BUDGET`), `AudienceSnapshotId`, expanded histogram buckets, and `fineage_command_to_wire_ms`.

| Scenario | Workers | Before (dev CI #855) command→fanout p99 | After (#862 CI run 45000441053) | Notes |
| --- | ---: | ---: | ---: | --- |
| bots-50-town | 0 | ~2833 ms (`bots-100-spread`) | **2932 ms** | tick p99 1500; `commands` stage p99 1500 |
| bots-50-town | 4 | ~2818 ms | **2540 ms** | tick p99 750; `commands` stage p99 750 |
| Unit oracle | — | — | `RegionParallelStressTests` + determinism **pass** (CI `zone-parallel-regions`) | 50/50 in world, 0 stuck both workers |

**Merge gate (#862):** command→fanout p99 is **not** in the 100–200 ms target and workers=0 is **not** clearly below the ~2833 ms baseline → **no squash-merge yet**; investigate command→wire vs load-test oracle next.

**Expected movement:** command handler replies complete after flush + enqueue (not after full step drain); tick `commands` stage p99 should drop vs #852’s end-of-step drain regression (p50 ~580 ms reported).

## Prioritized bugs / follow-ups

| P | Issue | Repro |
| --- | --- | --- |
| P1 | ~~Parallel region executor (`FINEAGE_ZONE_REGION_WORKERS=4`) at 500 bots: mass stuck clients~~ **Resolved** — overwritten mid-ramp report, not an executor hang; see "P1 resolution" above | not reproducible on current `dev` (2/2 clean 500-bot workers=4 soaks) |
| P2 | High `packets_rejected` under combat spam (synthetic targets) | 50-bot smoke, attack-weighted scenario |
| P3 | Load-test RTT percentile helper under-reports p50 when samples are sparse | See `ReportWriter.ClientRtt` — cosmetic report issue |

## Enter burst offload (Nov 22) — `request:enter` on the commands stage

**Profile baseline (`dev`, `bots-50-town`, ~34k NPCs in world):** `commands` stage **p99 650–826 ms** during bot ramp (microprofile).

**Mechanism:** `request:enter` is a `ZoneRequest` drained in the **`commands` stage** (not the handler thread). `PrepareEnter` (async store read) → sim `TryEnter` + `CommitEnter` → off-thread `EncodeAndDispatchEnter`.

### Per-phase histograms @ peak command-ingress scrape (50 enters, finer buckets)

Workers **0**, `local-logs/bots-50-town-diag-v3/w0/report.json` (`metrics-peak.txt` beside it):

| Metric | mean (ms) | p50 (ms) | p99 (ms) | count |
| --- | ---: | ---: | ---: | ---: |
| `fineage_enter_try_ms` (TryEnter + hydration) | 5.0 | 1 | **250** | 50 |
| `fineage_enter_try_aoi_bind_ms` | 0.1 | 0.5 | 2 | 50 |
| `fineage_enter_self_projection_ms` (CommitEnter self) | 10.4 | 0.5 | **650** | 50 |
| `fineage_enter_known_snapshot_ms` | 0.2 | 0.5 | 2 | 50 |
| `fineage_enter_known_encode_ms` (off-thread) | 0.1 | 0.5 | 5 | 50 |
| `fineage_enter_peer_fanout_ms` (off-thread) | 0.0 | 0.5 | 0.5 | 50 |
| `fineage_tick_stage_ms` stage=`commands` (all ticks @ scrape) | 3.2 | 0.5 | 25 | 378 |

**Slow-tick evidence (zone log):** `commands stage … total=732ms: request:enter=731.9ms` — lock/wait is **not** dominant; a single enter request consumes the commands slice.

**Interpretation**

- Coarse scrape “commands p99” across the whole soak **under-reports** enter spikes (many fast ticks dilute the histogram); use **per-enter histograms** + zone slow-tick lines.
- After known-list **encode offload** + **NPC spawn build on encoder thread** + **AOI bind without `OrderedVisible` sort**, known snapshot/encode are **~2–5 ms** p99; the residual cost is **`TryEnter` hydration (~250 ms p99)** and **`CommitEnter` self packets (~650 ms p99)** (quests/clan/skills/stats), not NetCodec for ~34k NPCs.
- **Cmd→fanout p50** vs pre-split control: **483 ms** (w0) vs **532 ms** (~9% better); merge gate on **`commands` per-enter still &gt; 650 ms** (732 ms worst tick) — **do not merge** until a follow-up trims sim-side `TryEnter` / self projection.

Later iteration (`bots-50-town-diag-v4`): item-list prebuild off-thread; worst enter log **~724 ms** — still above gate.

**After sim trim (`bots-50-town-diag-v6`, worker 0):** skip `RefreshPlayerActiveRegions` inside `TryEnter` (NPC think tick already refreshes regions); build **ItemList** + **SkillList** on the enter encoder thread. Zone slow-tick log worst `request:enter` **495.3 ms** (`commands` total **495.3 ms**) — meets the **&lt; 650 ms** merge gate on per-enter evidence.

| Run | Worst `request:enter` (zone log) |
| --- | ---: |
| diag-v3 (pre-trim) | ~732 ms |
| diag-v5 (region skip + item encode offload) | 651.7 ms |
| diag-v6 (+ skill encode offload) | **494.5 ms** |

Source artifacts: `local-logs/bots-50-town-diag-v3/` … `diag-v6/`; helper: `scripts/print-enter-phase-stats.py <report.json>`.

## Command ingress scheduler (Nov 23, Debug)

Before numbers are the post-audit Debug run (PR #913): one global 48-cost apply budget (~240 cost/s) against ~800 commands/s at 200 bots. After numbers are this branch, still Debug, 4 cores, sequential regions, `aoi=grid:3000`, `outboundQ=512`, zone GC **Server** (from `Fineage.Zone.runtimeconfig.json`). Histogram p99 is the cumulative scrape. `submit→mailbox` is end-to-end; `fanout_enqueue→mailbox` is the series the old report called cmd→mailboxes.

| | bots-50-town before | bots-50-town after | bots-200-town before | bots-200-town after |
| --- | ---: | ---: | ---: | ---: |
| tick p50 / p99 | 15.7 / 65.4 | 16.7 / 58.6 | 16.1 / 49.2 | 40 / 80 (bucket) |
| tick histogram max / true max | 1500 / (not recorded) | 1500 / **1131** | 1500 / (not recorded) | 10000 bucket / **1884** |
| submit→dequeue p99 (mean, n) | 624 (363, 700) | **218** (p50 189, 19352) | 2193 (1615, 13312) | **220** (175, 81658) |
| dequeue→apply p99 | 38.7 | **1.9** | 28.7 | **2.0** |
| fanout_enqueue→mailbox p99 | 3.9 | 4.6 | 14.8 | 20 (n=644) |
| submit→mailbox p99 / p99.9 | (not recorded) | **220 / 243** | (not recorded) | **220 / 250** |
| commands stage p99 | 40.2 | 39.4 | 38.3 | 60 (bucket) |
| queue depth p99 / age p99 | (not recorded) | 1 / 205 | (not recorded) | 1 / 200 |
| coalesced / rejected | (not recorded) | 0 / 0 | (not recorded) | 0 / 0 |
| outbound drops / fanout drops | 0 / 0 | 0 / 0 | 7 / 0 | 0 / 0 |
| watchdog stalls | (not recorded) | 0 | (not recorded) | **1** (step never returned) |
| client oracle p50 / p99 | 600.5 / 2608.5 | 406 / 2457 | 705 / 2870 | (runner killed while wedged) |
| real CPU % avg / max | <10% of 4 cores | **4.8 / 39.8** | <10% of 4 cores | **4.6 / 46.5** |
| zone RSS | 601 MiB | 570 MiB | (wedged) | 470 MiB (HWM 651) |
| result | PASS | runner PASS; fanout-enqueue gate PASS (4.6 ≤ 150); submit→mailbox 220 ≤ 1000 | wedged | **wedged** after ~90s of behavior |

50 bots: ramp 20s + 90s, enter concurrency 16, all 50 in world, 0 disconnects. The 1131 ms true max is one `request:enter` (910 ms) inside the apply slice; the timebox does not preempt a command already running.

200 bots: ramp 60s + 90s, all 200 entered (`Bot0199` at ~65s). Histogram figures in that column are the bucket upper bound that contains the quantile; the runner was killed while the step was wedged, so it did not write `load-test-report.md`. Behavior ran until tick `8977109007` stuck with **no region bodies in flight** (world stage / collect / barrier). Watchdog reported the stall from 30s and it was still growing past 4 minutes when the runner was stopped. That hang is the outbound/world-stage deadlock owned by the other change; this branch does not touch `NetFanoutStripes`, `OutboundMailbox`, or the outbound pump. Ingress during the healthy window no longer queues for seconds: submit→dequeue p99 is one tick (220 ms) instead of 2193 ms, queue depth p99 stays 1, and nothing was coalesced or rejected because the queues were drained.

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