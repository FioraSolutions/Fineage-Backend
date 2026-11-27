# Perf PR acceptance gates (Debug)

Every perf PR that touches simulation, pathfinding, fan-out, or persistence must report numbers from a soak on **`dev`** (or the PR branch rebased onto it) using load-test reports and Prometheus peak scrapes.

| Metric | Target |
| --- | --- |
| Tick work p99 | ≤ 50 ms |
| Tick work p99.9 | ≤ 100 ms |
| Submit → apply p99 | ≤ 100 ms |
| Fanout queue age p99 | ≤ 40 ms |
| Queue depth under steady load | Flat (no growth) |
| Reliable message loss (normal load) | 0 |
| Deadlock / infinite wait | 0 |
| `bots-200-town` soak | ≥ 15–30 min green |
| `perf-aoe-multiparty` soak | ≥ 15 min (report tick + fan-out) |

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

### Trend gates (evidence, not a ceiling)

After the ramp, the report evaluates steady-state trends:

| Gate | Fail when | Defaults |
| --- | --- | --- |
| RSS growth | growth > max(10 %, 64 MiB) between first and last steady-state sample | `fineage_zone` RSS via `/proc` or `ps` |
| GC heap growth | growth > max(10 %, 32 MiB) | `dotnet-counters` `gc-heap-size` |
| Allocation rate | last-third average > 1.5× first-third average | `dotnet-counters` `alloc-rate` |
| Fan-out queue depth | early-third vs late-third max is `growing` | `fineage_fanout_queue_depth` |
| Command queue depth | early-third vs late-third max is `growing` | `fineage_command_queue_depth` |
| Command ingress depth | early-third vs late-third max is `growing` | `fineage_command_ingress_queued` |

A run shorter than 5 minutes emits these as **informative**; from 5 minutes they are hard gates and missing
evidence fails the run. A missing metric or a Zone without `--zone-pid` is named in the gate note; it is not
silently treated as zero.

### Measured versus scheduled targets

This PR adds the measurement mechanism. It does **not** claim measured soak/endurance results or a concurrency
ceiling. The only measured data quoted here is the #951 baseline (Debug, one player, full world): tick
61–83 ms with 200–415 ms bursts at 0 players. Any player ceiling must come from a recorded ramp
(`LEVELS="100 300 500 1000" ./scripts/run-load-stress.sh`), with the same hardware/commit/environment and the
trend gates green; until then the profiles are workloads, not published capacity numbers.

Reference acceptance for a recorded soak/endurance artifact:

- Runner PASS, all gates PASS, no simulation hang, no reliable-message overflow.
- RSS/GC/alloc and all three queue trends not growing over the second half of the run.
- Repro metadata: `matrix-summary.json` plus each `load-test-report.md` (commit, Zone build config/module,
  CPU/RAM/OS, .NET, GC mode, scenario, bots, ramp + duration).
