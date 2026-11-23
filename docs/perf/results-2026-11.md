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

## Post-audit load run (Nov 23) — Debug, after #867

Local Debug soak of the fan-out path after #904 (timeline + honest queue age), #907 (compact `WorldFanoutBatch`), and #912 (one outbound mailbox per connection). #907 and #912 were not on `dev` when the processes started; both merged later the same morning (`84015b44`, `8daf4209`). The measured tree is local commit `b396efbf` (50-bot run: `ba94b966`, same tree with only `FanoutDispatch` whitespace differing). Diff vs the merged mailbox is where queue-depth samples are recorded (`NetConnection` vs inside `OutboundMailbox`); the wait, the pump, and the stripe enqueue are the same.

**Not pushed.** The combination commit stays local. This section is the only change on the results branch.

Machine: 4 cores, 16003 MiB, Ubuntu 24.04, .NET 10.0.15, `FINEAGE_BUILD_CONFIGURATION=Debug`. `DOTNET_GCDynamicAdaptationMode` and `DOTNET_TieredPGO` unset. Zone project sets `ServerGarbageCollection` (thread names are `.NET Server GC`); the load-test report's "Workstation" line only checks the env var and is wrong for this binary. World: 34054 NPCs, sequential regions, `aoi=grid:3000`, `outboundQ=512`. Postgres is the local cluster on 127.0.0.1:5432 (dockerd is down). No `FINEAGE_ZONE_MAPS` / `FINEAGE_ZONE_BOUNDS`.

`bots-200-town.yaml` is not in the repo. The 200-bot run used the `bots-50-town` weights (move 0.22, attack 0.16, skill 0.22, say/sit/pickup 0.06, social 0.05, trade/party/teleport 0.03, pvp 0.04, relog 0), `botCount` 200, ramp 60 s, duration 90 s, class 25. `bots-1000-town` is the repo file: 1000 bots, ramp 90 s, duration 120 s, town, default weights (skill 0.10, relog 0.02) and default class 0.

Percentiles below are linear interpolations inside the Prometheus bucket. A p99 that falls in the `le="0.5"` bucket is written `< 0.5`. The `1500` bucket is the top occupied tick bucket, not a measured maximum beyond it. `fineage_command_to_wire_ms` is not exported on this tree (the load-test parser still looks for it and gets 0). The server phases that replaced it are in the second table. The runner's "command→mailboxes" gate number is the client consistency oracle (`BroadcastLatency`), not `fineage_command_to_mailboxes_ms`.

### Server histograms (end-of-run scrape)

50-bot source is the peak-ingress scrape (`metrics-peak.txt`, 491 completed ticks, run finished PASS). 200 and 1000 are the scrape taken after the simulation step had already wedged (`metrics-stuck.txt`). The wedged tick itself is not in `fineage_tick_ms`: the histogram is recorded only when `StepSimulation` returns. One earlier tick in each run sits in the 1000–1500 ms bucket (`fineage_tick_overruns_total` = 1).

| Scenario | In world | Tick p50 | Tick p99 | Tick max bucket | cmd→mailboxes p99 | Socket write p99 | Fanout queue age p99 | Outbound queue age p99 | Drops | RSS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| bots-50-town | 50/50 | 15.7 ms | 65.4 ms | 1500 ms (1 sample) | 3.9 ms | < 0.5 ms | < 0.5 ms (max bucket 1 ms) | 4.3 ms | 0 | 601 MiB (HWM 641) |
| bots-200-town | 200/200 | 16.1 ms | 49.2 ms | 1500 ms (1 sample) | 14.8 ms | < 0.5 ms | < 0.5 ms (35 samples ≥ ~200 ms, max bucket 5000) | 7.0 ms | 7 | 463 MiB at stall (HWM 601) |
| bots-1000-town | 1000/1000 entered | 20.3 ms | 58.8 ms | 1500 ms (1 sample) | 9.8 ms | < 0.5 ms | < 0.5 ms (33 samples ≥ ~180 ms, max bucket 5000) | 20.9 ms | 0 | ~860 MiB at stall (counter max 863) |

Counts behind those p99s: mailboxes 75 / 719 / 1781; socket writes 10954 / 256885 / 1831259; fanout-age samples 1232 / 6002 / 18848; outbound-age samples 1302 / 90404 / 431533. Fan-out drops and fan-out overflow stayed 0. Packet rejects (synthetic combat targets): 105 / 4191 / not re-counted for 1000. Client consistency oracle, which the gate prints as command→mailboxes: 50-bot p50 600.5 / p99 2608.5 ms; 200-bot p50 704.8 / p99 2870.3 ms; 1000-bot runner never wrote a report.

### Command timeline (same scrapes)

| Phase | 50 p99 | 200 p99 | 1000 p99 |
| --- | ---: | ---: | ---: |
| `submit_to_dequeue` | 624 ms (mean 363, n=700) | 2193 ms (mean 1615, n=13312) | 9943 ms (mean 7372, n=21128) |
| `dequeue_to_apply` | 38.7 ms | 28.7 ms | 36.6 ms |
| `apply_to_seal` | < 0.5 ms | < 0.5 ms | < 0.5 ms |
| `seal_to_fanout_enqueue` | < 0.5 ms | < 0.5 ms | < 0.5 ms |
| `fanout_enqueue_to_mailbox` | 3.9 ms | 14.8 ms | 9.8 ms |
| `mailbox_to_socket_write` | 4.3 ms | 7.0 ms | 20.9 ms |

`commands` stage p99 on the same scrapes: 40.2 / 38.3 / 39.0 ms (1000: n=6643). `movement.npc` p99 stayed ~4.9 ms at 50 and 200. `enter_try` p99: 32.5 ms (n=50) and 2.0 ms (n=200). Pre-audit worst `request:enter` log line was 494.5 ms; that log line was not re-measured here.

Default command apply budget is 48 cost units per 200 ms tick (`FINEAGE_ZONE_COMMAND_APPLY_BUDGET`). That is the queue the first row is waiting in. Apply after dequeue is a few tens of milliseconds.

### Before this audit (from the tables above, not re-measured)

| | Before | This Debug run |
| --- | --- | --- |
| bots-50-town tick p50 / p99 | 25 / 500 ms, RSS 629 MiB, runner PASS | 15.7 / 65.4 ms, RSS 601 MiB, runner PASS |
| command→fanout p99 | ~2833 ms (CI `bots-100-spread`), 2932 ms (#862 `bots-50-town`) | server `submit_to_dequeue` p99 624 ms at 50 bots; client oracle still 2608 ms. Not the same series as #862's command→fanout histogram |
| `request:enter` worst log | 494.5 ms | `enter_try` histogram p99 32.5 ms (50) / 2.0 ms (200); no new worst-line capture |
| bots-1000-town | not completed; ramp exceeded the VM window | 1000/1000 entered (~0.9/s, ~20 min), then the simulation step wedged ~2 min into the 120 s behavior window |
| command→mailboxes, socket write, dispatcher age, outbound age, alloc/GC | not measured | table above |

### Allocations, GC, CPU, hot methods

`dotnet-counters collect --format csv` for `System.Runtime`, 12–15 s, plus `dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler`. The in-process `dotnet-counters monitor` output is not the CSV the load-test parser expects, so the runner reports "no parseable samples"; the numbers here are the separate collect. 50-bot had no CSV (the monitor held the session). Samples are from steady state before the wedge: 200 at 06:58:39Z, 1000 at 07:26:45Z (all 1000 already in town).

| | 200 steady (15 s) | 1000 steady (12 s) |
| --- | ---: | ---: |
| alloc rate | avg 36.5 MB/s, max 46.0 | avg 64.1 MB/s, max 195 |
| GC pause (`pause.time` per 1 s) | avg 1.6 ms, max 7.7 ms | avg 1.9 ms, max 12.8 ms |
| gen0 / gen2 collections per 1 s | 0.14 / 0.36 | 0.09 / 0.36 |
| gen2 heap / LOH | 164 MB / 83 MB | 218 MB / 118 MB |
| committed / working set | ~321 MB / max 461 MB | max 747 MB / max 863 MB |
| CPU (`cpu.time` user + system) | 0.185 + 0.019 s/s | 0.295 + 0.059 s/s |
| lock contentions | avg 4.1/s, max 13/s | avg 12.6/s, max 20/s |
| thread-pool queue length rate | 0 | avg 0.09, one sample 330 |

Per-thread CPU over 15–20 s (`/proc/<pid>/task/*/stat`, no cutoff). 200-bot steady top: character persist 2.3%, four thread-pool workers 1.3–2.1%, BGC 2.1% and 1.1%. 1000-bot steady top: thread-pool worker 5.5%, character persist 5.0%, thread-pool workers 4.4% and 4.0%, BGC 2.4%. Nothing was a single saturated core. Four cores, so the zone was under ~10% of the machine.

Exclusive CPU sample (the rest of the profile is threads parked on the pool):

| Rank | 200-bot steady | 1000-bot steady |
| ---: | --- | --- |
| 1 | `LowLevelLifoSemaphore.WaitForSignal` 55.3% | same, 55.7% |
| 2 | `WaitHandle.WaitOneNoCheck` 17.2% | 17.8% |
| 3 | `Monitor.Wait` 11.2% | 11.4% |
| 4 | Missing symbol 5.8% | 6.2% |
| 5 | `GC.RunFinalizers` 3.3% | `LowLevelLifoSemaphore.Wait` 0.64% |
| 6–10 | `Thread.PollGC` 0.33%, `Monitor.Enter_Slowpath` 0.20%, `LowLevelLifoSemaphore.Wait` 0.15%, `WorkerThreadStart` 0.12%, `ZoneEntity.get_Kind` 0.10% | `Thread.PollGC` 0.58%, `Monitor.Enter_Slowpath` 0.36%, `WorkerThreadStart` 0.28%, `ZoneWorld.Players` 0.14%, `ZoneEntity.get_Kind` 0.11% |

`ZoneWorld.Players` is also 0.09% exclusive at 200. The managed hot path during these windows is not the tick. The multi-second delays are the command queue and, once the mailbox fills, threads blocked in `Monitor.Wait` rather than burning CPU.

### Why 200 and 1000 did not finish the soak

Both runs got every bot into the world (200 in ~5 min, 1000 in ~20 min). Enter is serial: `LoadTestRunner` awaits `ConnectAndEnterAsync` before starting the next bot, and enter waits for the welcome burst plus a 400 ms quiet window. That is ~0.9 bots/s and is why the pre-audit 1000-bot row died during ramp. This time the enters completed.

The behavior window then killed the zone. First watchdog line:

- 200: `07:00:24Z`, tick `8977085970` already 30.6 s in `StepSimulation`, "no region bodies in flight". Seven log lines just before it: `outbound mailbox is full (frames=512/512 bytes=18143/65536)`, opcode 511, first at `06:59:44Z`.
- 1000: `07:29:00Z`, tick `8977094550` already 30.7 s, same text. No mailbox-full line; the stripe path waits instead of dropping, and `fineage_outbound_dropped_total` was still 0.

Mini-dump `clrstack -all` is the same cycle on both:

- Simulation thread: `StepSimulation` → `TickSpawns` → `FanoutNewNpc` → known-list diff → `TrySendCritical` → `NetFanoutStripes.Enqueue` → `WriteAsync().GetResult()` (stripe channel full).
- Fan-out dispatcher: `ExecuteBatch` → `FanoutDeliveryScope.Flush` → `EnqueueBatch` → the same `Enqueue`.
- All four stripe workers: `ProcessStripeAsync` → `DeliverDrain` → `SubmitOrderedBatch` → `OutboundMailbox.AppendOrdered` → `Wait` (`Timeout.Infinite`, `OutboundMailbox.cs` line 219).

`SubmitOrderedBatch` waits forever for mailbox room. Room only appears when that connection's pump calls `Take`. The 200-bot socket engine thread was idle in `WaitForSocketEvents` at the dump, so the pumps were not blocked inside a socket write at that instant. The pump state was not read back (heap `dumpobj` segfaulted in `dotnet-dump`). What the stacks show is enough to explain the watchdog: the step cannot return while `FanoutNewNpc` is stuck in a synchronous stripe enqueue, and the stripe consumers cannot drain while they sit in an infinite mailbox wait.

The 200-bot runner still printed PASS (200/200 in world, 0 disconnects, 0 stuck, 19900 consistency checks, 0 issues) because the wedge landed as the 90 s behavior window ended. The 1000-bot runner never flushed a report; after the zone was stopped it kept a thread at ~137% CPU and ~2.5 GiB RSS, and it was killed so the soak could be closed. Server numbers for 1000 are the Prometheus scrape and the counters/trace taken before the watchdog line.

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