# Tick stage breakdown — 50-bot town (2026-11-21)

Full default world (~34k NPCs, 9 player regions). Load scenario: `bots-50-town`, `town` layout, region workers 0.

## Root cause

Zone overrun warnings name **`commands`** as the slow stage (~650–820 ms), not AI/regen/abnormals (`random-walk` ~10–12 ms, `aggression` ~2–3 ms). Per-move apply histograms (geo, `reconcile_knownlist`, `plan_diff`) stay **&lt;0.5 ms**; the time was **synchronous MoveState fan-out drain inside the commands stage** (mid-tick `ReleaseAppliedCommandReplies`), amplified by an earlier multi-batch apply loop that called drain repeatedly.

## Before / after (shared pipeline fix)

| Signal | Before | After |
| --- | ---: | ---: |
| Overrun slowest stage (`commands`) | 826 ms | 650 ms (single spike at bot ramp; log) |
| Other stages (p99 sample) | `random-walk` ~12 ms | ~10 ms |
| Tick p99 (metrics histogram) | 1000 ms bucket | 1000 ms bucket (1 overrun) |
| Command→fanout p50 / p99 (client) | ~184 / ~2609 ms | ~580 / ~2825 ms |
| `fanout_drain` in commands release | full TCP drain | deferred to step `RunStep` drain |

Changes:

1. One ingress batch per tick (removed wall-clock budget and multi-batch drain loop).
2. `ReleaseAppliedCommandReplies` seals command fan-out and completes handler tasks **without** draining sockets; the outer step drain ships all sealed batches once per 200 ms tick.

## Profiling

```bash
dotnet tool install -g dotnet-trace   # once
./scripts/run-local-net.sh start
dotnet-trace collect -p "$(cat local-logs/zone-net.pid)" --profile cpu-sampling --duration 00:00:45 -o /tmp/zone-50bot.nettrace
# run load test in another shell, then stop trace
```

Scrape stage histograms: `curl -s localhost:9465/metrics | rg fineage_tick_stage_ms`.

## Follow-ups

- Burst `request:enter` during ramp still dominates one `commands` tick (apply + inventory/knownlist), separate from move fan-out drain.
- Local net / load scripts default `FINEAGE_ZONE_COMMANDS_PER_TICK=24` so login bursts spread across steps without a wall-clock budget.
- Load reports now scrape overrun `slowest:` lines and `fineage_tick_step_fanout_drain_ms` p99; revert `NetMoveVisibility` timeout bumps (30→120s on PR #835) after tick p99 is back under 200 ms.
- CI `NetMoveVisibilityTests.Outsider_*` at 120 s is the same symptom under saturated runners (hand-driven ticks wait on wall clock); fix is tick cost, not AOI logic — passes in ~5 s isolated.
- Peer `StalePosition` in the oracle until AOI/command timing is tightened; self `MoveInput`→`MoveState` p99 still ~2.8 s (queue + end-of-step drain).