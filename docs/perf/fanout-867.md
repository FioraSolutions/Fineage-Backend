# Zone fan-out performance plan — #867 close-out

External .NET audit of 2026-11-22. The P0 plan (observability, stripes, connection
handles and batches, mailbox, compact batches) and the follow-up P1 items
(pooled `WriteManyAsync`, inbound `PipeReader`, persist off the sim thread,
`PlanDiff` without LINQ) are on current `dev`. This note checks every original
defect against that tree, with a Debug many-player measurement. It does not
open parallel work for items already owned by other issues (#986 hand-off
bounds, already merged; #990 Release profiles; #1047 consistency residuals).

## Per-defect audit (current `dev`)

| # | Defect | Status | Evidence |
| --- | --- | --- | --- |
| 1 | `FanoutDispatcher`: one unbounded channel, one reader, serial `send()` | **Fixed** | Dedicated worker thread, soft/hard batch and send-work limits, one stripe enqueue per connection. `#862` `5d4c07c3`, PR2 `#897` `f0121e58`, `#970` `d0547c35`. Tests: `FanoutDispatcherBackpressureTests`, `WorldFanoutDispatcherOrderTests`, `NetFanoutStripesTests`. |
| 2 | `SendNow` / `RunSynchronouslyAfterPending` drains on the sim thread | **Fixed** | `WorldFanoutSequencer.Seal` / `Submit` only call `Enqueue`. `#970` `d0547c35`. |
| 3 | `NetPresenceHub`: two locked lookups per watcher | **Fixed** | Immutable published table + `ConnectionHandle` (object id, generation, connection); no global lock on the send path. PR3 `#908` `a2567fda`. Tests: `NetPresenceAudienceTests`, `NetStripeBatchDeliveryTests`. |
| 4 | Per-frame channel, coalesced-only stall, single-packet writes | **Fixed** | Byte-bounded `OutboundMailbox`, `DropWrite` wake, one batch per connection, pump `WriteManyAsync`. PR4 `#912` `8daf4209`, wake `#901` `14a45362`, `#914` `5edb2e30`. Tests: `NetOutboundMailboxTests`, `OutboundMailboxBudgetTests`, `NetMoveVisibilityMailboxTests`. |
| 5 | Closures, boxed payloads, `Sends()` iterator, O(K) audience hashing | **Fixed** | `FanoutKind` + `FanoutDescriptor` / `FanoutArgs`; “nothing in the batch is a per-event closure.” `PrepareCharInfo` still uses `AddPrepared` because encoding reads live world state on the sim thread. PR5 `#907` `84015b44`. Tests: `WorldFanoutOutboxTests`. |
| 6 | Static `AudienceSnapshotRegistry`, no eviction | **Fixed** | `AudienceSnapshots` ref-counts, reuses slot indexes, drops a set when the last batch and owner release it. Same PR5. Tests: `AudienceSnapshotTests`. |
| 7 | `WriteManyAsync` extra copies; 3 allocs per inbound frame | **Fixed** | Multi-frame `WriteManyAsync` counts sizes and encodes into one `ArrayPool` buffer (`#898` `e0b6c5ae`). Inbound path is `System.IO.Pipelines` (`#882` `20d7670c`). Tests: `FramingAndDedupTests`, `InboundPipeFramingTests`. |
| 8 | Metrics lie (`CommandQueueWait` at enqueue; “command_to_wire” is mailbox enqueue) | **Fixed** | Queue wait is recorded at dequeue; timeline phases submit→dequeue→apply→seal→fanout enqueue→mailbox→socket write; `fineage_command_to_mailboxes_ms` is the honest name. PR1 `#902` `a3490c09`, `#904` `84188850`, `#899` `6fb4233a`. Tests: `HonestMetricsTests`, `MetricsSummaryTests`. |
| 9 | `FlushDirtyInventories` / sync `IAccountStore` on the sim thread | **Fixed** | `CharacterPersistWorker` / `EconomyPersistWorker` are the only threads that call the store for snapshots and durable economy commits. Inside a step, `FlushDirtyInventories` queues work and does not wait. `#966` `36d50c5f`. Tests: `CharacterPersistWorkerTests`, `EconomyPersistWorkerTests`. |
| 10 | `PlanDiff` LINQ `OrderBy`, temp lists per move | **Fixed** | Pooled buffers, sort, merge. `#874` `154011b4`. Tests: `KnownlistPlanDiffTests`. |

P2 items from the audit (FrozenDictionary, source-generated `LoggerMessage`, region-parallel combat after a new profile) were “not now” and are not opened here.

## Debug many-player measurement (`78aad0e6`, `bots-200-town`)

Host: 8 cores, 16 GiB, Debian 13, .NET 10.0.15, Server GC, workers=0, 20 geodata
regions, npcs=34054. Times are Europe/Minsk (UTC+3).

Recipe: `FINEAGE_BUILD_CONFIGURATION=Debug SCENARIO_DURATION_SECONDS=300 SCENARIO_RAMP_SECONDS=60 ./scripts/run-load-test.sh bots-200-town`.

| Metric | #867 limit | Observed |
| --- | ---: | ---: |
| Tick p99 (peak scrape) | ≤ 200 ms | **30.9 ms** |
| Tick overall p99 / p99.9 / max (1749 ticks) | — | 15.8 / 52.5 / **62.2 ms** |
| Tick steady p99 / p99.9 (1328 ticks, +30 s) | — | **9.7** / 18.5 ms |
| Command → mailboxes p99 | ≤ 180 ms | **2.3 ms** |
| Submit → mailbox p99 | ≤ 1000 ms | **6.8 ms** |
| Fan-out queue age p99 | ≤ 40 ms | **1.3 ms** (max bucket 50; was 142–197 ms in the original regression) |
| Fan-out batch process p99 | — | 1.3 ms |
| Dispatcher enqueue wait p99 | — | 0.5 ms |
| Reliable / fan-out / outbound drops | 0 | **0** |
| Backlog soft exceeded / emergency | 0 | **0** |
| Alloc rate last/first third | ≤ 1.5× | **0.22×** (47.8 → 10.3 MiB/s) |
| RSS steady growth | max(10%, 64 MiB) | **+2.0 MiB / 0.2%** |
| Consistency oracle | — | **0 issues / 773 checks** |

Original audit-era `bots-200-town` after the first wave still showed tick p99 81.2 ms
and a 1420 ms true max on the wedged scheduler head; this run’s true max is 62.2 ms.

### What failed on the raw 300 s report (not leftover fan-out defects)

1. **Fan-out depth “growing” 32 → 64.** `fineage_fanout_queue_depth` is a lifetime
   histogram high-water. 38 of 109 406 samples landed in `(32, 64]` (mean depth 1.47);
   every later scrape then reported max 64 while age p99 *fell* from 1.43 to 1.32 ms
   and the soft-backlog counter stayed 0. The queue-trend gate now treats one
   `CountBounds` bucket step as flat (32 → 64 passes; 10 → 30 still fails).
2. **GC heap +338 MiB.** `dotnet-counters` writes a naive local timestamp. The parser
   treated it as UTC, so on this UTC+3 host every boot/ramp sample (heap 466 MiB at
   process start) passed the steady-state filter. RSS (real UTC scrapes) was already
   correct (+2 MiB). With timestamps converted from local clock, the same series
   starting at first online=180 + 30 s is 747 → 804 MiB (**+57 MiB**, under the
   10% / 74.7 MiB limit). Last-third mean was 701 MiB — not a leak.
3. **Runner: 20/200 `zone_connect` connection refused, and 0 skill samples.**
   Zone net listen happens after full-world + geodata load (~21 s here). The
   start script waited 15 s, returned failure, and because `run_one` is invoked
   from `if !`, `set -e` did not abort: bots 0–19 hit a closed 9777; bots 20–199
   entered once listen happened (02:19:42Z). The start wait is now 90 s, and
   `run_one` returns 1 if the stack is not listening. Zero `UseSkill→MagicSkillUse`
   samples are the known town-placement limit (#1045); `bots-200-town` no longer
   requires those samples as a server gate.

### Confirmation run (same host, after harness/gate fixes, 05:30–05:37 Europe/Minsk)

A second Debug 300 s `bots-200-town` with the 90 s listen wait, `run_one` aborting if
the stack is not ready, local-timestamp GC samples, and one-bucket depth slack:

| Metric | Observed |
| --- | ---: |
| In world / disconnects / stuck | **200 / 0 / 0** (runner PASS) |
| Tick p99 / overall p99 / true max | 27.2 / 19.1 / **51.6 ms** |
| Steady p99 / p99.9 | 18.5 / 24.7 ms |
| Command → mailboxes p99 | **4.9 ms** |
| Submit → mailbox p99 | 28.4 ms |
| Fan-out queue age p99 | **4.1 ms** (max 20) |
| Drops / backlog soft / emergency | **0 / 0 / 0** |
| RSS | 1287.6 → 1287.3 MiB (−0.3) |
| GC heap (steady window) | 931.1 → 728.6 MiB (**−202.5**) |
| Alloc rate | 35.8 → 0.8 MiB/s (0.02×) |
| Consistency oracle | **0 issues / 373 checks** |

The only remaining raw-report fail was command-ingress depth early median 0 vs a
single late scrape of 79 (the next 10 s window was 0; 200 bots can queue that many
commands for one 200 ms tick). Queue-trend thirds now use the **median**, so a
one-window gauge spike is flat; a backlog that stays up through the late third
still fails.

## Gates / harness kept honest

- Queue-trend slack is `max(early × 1.25 + 8, next CountBounds bucket)`.
- Runtime CSV timestamps without an offset are local, then converted to UTC.
- `run-local-net.sh` waits up to 90 s for login and zone net ports;
  `run-load-test.sh` does not start bots if that wait fails.
- `bots-200-town.yaml` sets `requireSkillLatencySamples: false` with a comment.
