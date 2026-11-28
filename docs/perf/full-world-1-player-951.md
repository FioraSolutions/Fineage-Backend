# Full world, one player — #951 tick budget

Issue #951 recorded Debug zone tick spikes with one player in the full enabled spawn
world. The simulation work that used to cause those spikes landed in earlier PRs
(#965–#970, #1036, #1040, #1045, #1094, #1110–#1117). This note is the Debug
re-measure on current `dev` and the honest gates that keep the budget from
regressing. It is not a Release-build switch and not a new tick algorithm.

## Original baseline (`b059fc17`, Debug)

| Symptom | Observed |
| --- | ---: |
| Tick average, 1 player, 34 054 NPCs, geodata on | 61–83 ms |
| 0-player idle ticks | 200–415 ms |
| `random-walk` after teleport into Giran (region wake) | 2000 ms |
| `random-walk` later | 310 ms |
| `npc.paths` | 173 ms |
| `request:move` | 112–120 ms |
| `CommitEconomy` on the simulation thread | 68 ms |

Target stated on the issue: tick p99 &lt; 50 ms, and any tick above 200 ms attributed
to a named stage.

## How to read the numbers

- **`TickOverall` / `TickSteady`** are histograms over the recorded steps.
  Overall includes world enter. Steady starts 30 s after the first scrape with
  `fineage_zone_online = 1`. These are the #951 windows.
- A time-series or peak-scrape “tick p99” is often the **cumulative** Prometheus
  p99, so a later window still includes the enter burst and falls only as more
  short ticks arrive. Do not gate #951 on that figure.
- **`fineage_tick_max_ms`** is the longest single step (including one still
  running). It does not reset.
- `tick_stage/sync_io.IAccountStore.ListAllCharacters` at 250 ms (n = 1) is a
  startup or shutdown `Step()` recorded by `SimulationSyncIoGuard`. It does
  **not** appear inside `fineage_tick_ms` / `fineage_tick_max_ms` and is not a
  mid-tick gameplay spike.
- `fineage_fanout_queue_depth` (and the command-depth twin) expose a **lifetime
  histogram high-water**. One mid-run sample of depth 4 ratchets every later
  scrape from 2 to 4. The queue-trend gate treats a move of at most 8 (one
  `CountBounds` step) as flat; 10 → 30 still fails.

## Current `dev` (commit `1c3e6ae8`, Debug)

Host: 8 cores, 16 GiB, Debian 13, .NET 10.0.15, Server GC, `FINEAGE_ZONE_REGION_WORKERS=0`.
World: every enabled spawn map, **npcs=34054**, 20 geodata regions loaded from
`data/geodata`. Recipe: `FINEAGE_BUILD_CONFIGURATION=Debug ./scripts/run-load-test.sh full-world-1-player`.
A named-scenario run can override duration with `SCENARIO_DURATION_SECONDS`
(the 300 s confirmation used 300).

Times below are Europe/Minsk (UTC+3).

### `full-world-1-player`, 120 s (04:40 Europe/Minsk)

| Window | Ticks | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Overall (enter included) | 588 | 0.9 | 0.4 | **11.9** | 88.2 | **81.7** |
| Steady (+30 s after online=1) | 426 | 0.6 | 0.4 | **1.9** | 7.9 | ≤ 10 (bucket) |

RSS +17 MiB (pass). Alloc 17.4 → 1.5 MiB/s (0.09×). GC heap +138.5 MiB on a
122 s run — **informative only** under the 5-minute trend rule; the 300 s run
is the hard heap verdict.

### `full-world-1-player`, 300 s (04:47–04:52 Europe/Minsk)

| Window | Ticks | Mean | p50 | p99 | p99.9 | Max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Overall (enter included) | 1491 | 0.7 | 0.4 | **4.6** | 32.6 | **58.7** |
| Steady (+30 s after online=1) | 1327 | 0.6 | 0.4 | **2.0** | 4.9 | ≤ 10 (bucket) |

| Trend (hard at ≥ 300 s) | Observed | Verdict |
| --- | --- | --- |
| RSS | 1101.2 → 1187.1 MiB (+86.0 MiB / 7.8 %, limit 110.1) | PASS |
| GC heap | 940.7 → 900.2 MiB (−40.5 MiB, gen2 Δ 1) | PASS |
| Alloc rate | 7.3 → 1.5 MiB/s (0.21×, limit 1.5×) | PASS |
| Fan-out depth high-water | early 2 → late 4 (one sample in the le=4 bucket; age p99 0.5 ms, drops 0) | flat after slack 8 |
| Command / ingress depth | 1 / 0, flat | PASS |

Watchdog stalls 0, reliable overflow 0, fan-out / outbound drops 0.

### Longest tick, by stage (peak-scrape histogram max)

The 58.7–81.7 ms true max is the **enter / first-wake** burst, not a steady
tick. Stages that actually moved the needle:

| Stage | 120 s max | 300 s max | Notes |
| --- | ---: | ---: | --- |
| `handoff` | 40 | 40 | First-wake / enter; p99 falls to 1 ms |
| `commands` / `commands.apply` | 25 / 20 | 25 / 20 | Enter path; steady p99 0.5 ms |
| `random-walk` | 20 | 20 | Was 2000 ms into Giran; p99 10.2 → 1.8–2 ms on the longer run |
| `clan.hall-auctions` | 10 | 10 | Periodic, not a 200 ms stall |
| `npc.paths` | 2 | 2 | Was 173 ms |
| `movement.npc` | 10 | 5 | Was coupled to the path spike |
| `spawns` | 0.5 | 0.5 | Wake/spawn budgets stayed inside the tick |
| `CommitEconomy` on the sim thread | — | — | Moved off the sim thread in earlier work |

No tick in either run exceeded 200 ms. Overall and steady p99 are both under 50 ms.

### 0-player idle, 180 s (04:56 Europe/Minsk)

No official YAML. Debug Login+Zone via `./scripts/run-local-net.sh start` with
`FINEAGE_ZONE_METRICS_PORT=9465`, geodata required, default full world, no bots,
`fineage_zone_online = 0`. 20 geodata regions, npcs=34054.

| Window | Ticks | Mean | p50 | p99 (bucket) | True max |
| --- | ---: | ---: | ---: | ---: | ---: |
| Whole scrape (includes first ticks) | 905 | 0.60 | 0.5 | 10 | **83.8** |
| After the first 30 s | 750 | **0.38** | — | — | still 83.8 (did not rise) |

The 83.8 ms true max was already present in the first four ticks (handoff 40 +
`random-walk` 10–20 + `clan.hall-auctions` 10). It never rose over the next
three minutes. Zone log `tick_ms_avg` settled at 1.12 → 0.44 → 0.33 ms. The
original 200–415 ms 0-player idle is gone.

## Gates added or kept

`full-world-1-player` (and a future `full-world-0-player-idle` profile) now fail
when any of these honest windows miss: overall p99 ≤ 50 ms, steady p99 ≤ 50 ms,
steady p99.9 ≤ 100 ms, true max ≤ 200 ms. Missing honest windows fail the run.

RSS / GC / alloc growth on a run shorter than 300 s is informative, matching
the documented 5-minute trend rule (a 120 s heap bump is not a soak failure).
From 300 s they stay hard, and missing evidence still fails a soak.
