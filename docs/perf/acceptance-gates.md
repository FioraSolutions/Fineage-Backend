# Perf PR acceptance gates (Debug)

Every perf PR that touches simulation, pathfinding, fan-out, or persistence must report numbers from a soak on **`dev`** (or the PR branch rebased onto it) using load-test reports and Prometheus peak scrapes.

| Metric | Target |
| --- | --- |
| Tick work p99 | ≤ 50 ms |
| Tick work p99.9 | ≤ 100 ms |
| Submit → apply p99 | ≤ 100 ms |
| Fanout queue age p99 | ≤ 40 ms |
| Queue depth under steady load | Flat (no growth). Lifetime histogram high-water may move by one CountBounds step (slack 8) without counting as growth; see [`full-world-1-player-951.md`](full-world-1-player-951.md). |
| Reliable message loss (normal load) | 0 |
| Deadlock / infinite wait | 0 |
| `bots-200-town` soak | ≥ 15–30 min green |
| `perf-aoe-multiparty` soak | ≥ 15 min (report tick + fan-out) |

### Fan-out plan (#867)

The 2026-11-22 audit defects (serial dispatcher, sim-thread drain, locked presence
lookups, per-frame mailbox, closure batches, unbounded audience intern, lying
command-to-wire metrics, sync persist on the tick, LINQ `PlanDiff`) are fixed on
current `dev`. Debug `bots-200-town` (300 s, `78aad0e6`, confirmation after the listen-wait
fix): **200/200** in world, tick p99 **27.2 ms**, true max **51.6 ms**,
command→mailboxes p99 **4.9 ms**, fan-out age p99 **4.1 ms**, drops **0**,
steady heap **−202 MiB**, RSS flat. The original 142–197 ms fan-out-age
regression is historical. Per-defect commits and tests are in
[`fanout-867.md`](fanout-867.md).


Include a before/after table in the PR description and link artifact paths (`load-test-report.md`, commit SHA, machine notes).

Stage budgets, overflow policies and the metric that measures each stage of the sim → socket chain are in
[`nonblocking-simulation.md`](nonblocking-simulation.md).

## Release soak and endurance (#990)

These runs are **Release** builds. The executable recipe is `scripts/run-load-test.sh`; every scenario gets a
fresh Login+Zone process, a bounded timeout and its own report. `matrix-summary.json` records the git commit,
build configuration, host CPU/RAM/OS, .NET version and the per-run reports.

| Profile | Workload | Bot default | Release fast (PR `perf`) | Nightly soak | Weekly endurance |
| --- | --- | ---: | ---: | ---: | ---: |
| `pve-farming` | NPC combat, skills, XP, movement near arena packs | 100 | 60 s | 4 min | 15 min |
| `party-farming` | 4 real 9-bot parties, shared XP, combat fan-out | 36 | 60 s | 4 min | 15 min |
| `raid-boss-observe` | 1 real fighter + hundreds of observers in AOI | 500 | 60 s | 4 min | 15 min |
| `reconnect-storm` | login → handoff → enter-world → relog loop | 200 | 60 s | 4 min | 15 min |
| `full-world-1-player` | full enabled spawn world with one real client | 1 | 60 s | 4 min | 15 min |
| `bots-50-town` / `bots-200-spread` | town idle / movement baseline | 50 / 200 | profile | profile | profile |
| `perf-aoe-train` / `bots-500-siege` / `bots-economy-hammer` | existing heavy/AoE, siege, economy | YAML | profile | existing jobs | existing jobs |

`./scripts/run-load-test.sh profile` runs the whole profile table (including the existing heavy/economy/siege
scenarios) once. `fast`, `soak`, `endurance` and `stability` use the same profiles with bounded duration
overrides. Scheduled modes are:

| Mode | Schedule | Target wall clock | Where |
| --- | --- | ---: | --- |
| `fast` | `perf`-labelled PR into `dev`, any PR into `main` | ~5 min scenario time | `load-test-smoke.yml` |
| `soak` | nightly `0 1 * * *` UTC, push `main`, dispatch | 20–30 min | `load-test-smoke.yml` |
| `endurance` | weekly Sunday `0 3 * * 0` UTC, dispatch | 1–2 h | `load-test-endurance.yml` |
| `stability` | manual dispatch only, self-hosted runner | 8–24 h | `load-test-endurance.yml` |

Ordinary PRs into `dev` stay on the fast unit/format/build gate; no load, soak or 8–24 h job is scheduled on
them. GitHub-hosted jobs are capped at 6 h, so the stability job refuses to start on `ubuntu-latest` instead of
pretending to measure 8–24 h.

### Reconnect churn: longest tick (#1110)

`reconnect-storm` also gates the **longest single step** (`fineage_tick_max_ms` ≤ 1000 ms; a run without that
gauge fails) and the **longest freeze of the tick counter** between scrapes (≤ 10 s). A p99 hides one slow step,
and the #990 stall was not a slow step: every step stayed under 100 ms while the counter froze for 30.8 s, and a
29 s freeze would still pass the 30 s hang gate.
The cause was the thread pool, not the step: logout writes ran synchronously on pool threads behind the store's
single write gate, and the simulation loop, its watchdog and the economy persist worker all resumed through the
pool. They now block on their own threads, logout writes run on a dedicated lane the handler awaits, and
`SimulationPoolStarvationTests` starve the pool on purpose. Debug numbers are in
[`reconnect-churn-tick-stall.md`](reconnect-churn-tick-stall.md).

### Full world, one player (#951)

`full-world-1-player` is gated on the **honest histogram windows**, not the peak-scrape /
cumulative-window tick p99:

| Gate | Limit |
| --- | ---: |
| Overall tick p99 (whole run, enter included) | ≤ 50 ms |
| Steady tick p99 (first online=1 scrape + 30 s) | ≤ 50 ms |
| Steady tick p99.9 | ≤ 100 ms |
| Longest single tick (`fineage_tick_max_ms`) | ≤ 200 ms |

Missing honest windows fail the run. Debug numbers on current `dev` (commit `1c3e6ae8`,
34 054 NPCs, 20 geodata regions, 8 cores / 16 GiB, Server GC, workers=0):

- 300 s 1-player: overall p99 **4.6 ms**, steady p99 **2.0 ms**, true max **58.7 ms**.
  Heap shrank 40.5 MiB; RSS +86 MiB inside the 10 % / 64 MiB rule.
- 180 s 0-player idle (no official YAML; same full world, online=0): mean after 30 s
  **0.38 ms**, true max **83.8 ms** on the first four ticks, never rose. The original
  200–415 ms idle is gone.

The 58–84 ms true max is the enter / first-wake burst (`handoff` 40 ms, `commands` 25 ms,
`random-walk` 20 ms). `random-walk` into Giran was 2000 ms on the #951 baseline;
`npc.paths` was 173 ms and is now ≤ 2 ms. Detail, including the short-run 120 s
profile, is in [`full-world-1-player-951.md`](full-world-1-player-951.md).

### Trend gates (evidence, not a ceiling)

After the ramp, the report evaluates steady-state trends:

| Gate | Fail when | Defaults |
| --- | --- | --- |
| RSS growth | growth > max(10 %, 64 MiB) between first and last steady-state sample | `fineage_zone` RSS via `/proc` or `ps` |
| GC heap growth | growth > max(10 %, 32 MiB) | `dotnet-counters` `gc-heap-size` |
| Allocation rate | last-third average > 1.5× first-third average | `dotnet-counters` `alloc-rate` |
| Fan-out queue depth | late-third **median** > max(early × 1.25 + 8, next CountBounds bucket) | `fineage_fanout_queue_depth` (lifetime histogram high-water; one bucket step is flat) |
| Command queue depth | late-third **median** vs the same limit | `fineage_command_queue_depth` |
| Command ingress depth | late-third **median** vs the same limit | `fineage_command_ingress_queued` (point-in-time gauge; a single scrape spike is not growth) |

A run shorter than 5 minutes emits these as **informative**, including a *measured* RSS / GC / alloc
growth that would fail a soak; from 5 minutes they are hard gates and missing evidence fails the run.
A missing metric or a Zone without `--zone-pid` is named in the gate note; it is not silently treated
as zero. A 2 → 4 ratchet on fan-out depth is one histogram bucket and is flat under the slack of 8;
10 → 30 is still growing.

### Measured versus scheduled targets

This section describes the measurement mechanism. It does **not** claim a concurrency ceiling.
The #951 Debug baseline (one player, full world, geodata on) was tick 61–83 ms with 200–415 ms
bursts at 0 players; that is historical. Current Debug numbers are in the #951 section above
and in [`full-world-1-player-951.md`](full-world-1-player-951.md). Any multi-player ceiling must
still come from a recorded ramp (`LEVELS="100 300 500 1000" ./scripts/run-load-stress.sh`), with
the same hardware/commit/environment and the trend gates green; until then the other profiles
are workloads, not published capacity numbers.

Reference acceptance for a recorded soak/endurance artifact:

- Runner PASS, all gates PASS, no simulation hang, no reliable-message overflow.
- RSS/GC/alloc and all three queue trends not growing over the second half of the run.
- Repro metadata: `matrix-summary.json` plus each `load-test-report.md` (commit, Zone build config/module,
  CPU/RAM/OS, .NET, GC mode, scenario, bots, ramp + duration).
