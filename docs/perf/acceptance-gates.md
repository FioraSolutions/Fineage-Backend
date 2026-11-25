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