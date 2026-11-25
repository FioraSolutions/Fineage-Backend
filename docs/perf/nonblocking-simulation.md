# Non-blocking simulation → socket pipeline

Issue [#986](https://github.com/FioraSolutions/Fineage-Backend/issues/986). This document is the budget and
overflow contract for every stage between the simulation step and the client socket, plus the audit of the
locks the step touches. It complements [`docs/architecture/zone-simulation.md`](../architecture/zone-simulation.md)
§3.6 (fan-out semantics) and [`docs/perf/acceptance-gates.md`](acceptance-gates.md) (the numbers a perf PR must
report).

**Rule:** no stage in this chain may block the simulation thread, and no stage may hold an unbounded buffer. A
stage that cannot accept work sheds what the client can lose, or refuses the connection; it never waits for the
downstream stage to catch up.

## 1. Stages

```
┌──────────────────────────────────────────────────────────────────────────┐
│ 1. Simulation step  (RegionTickScheduler, 200 ms tick)                   │
│    commands → stages → region bodies → seal one WorldFanoutBatch         │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │ Enqueue(sealed batch)  — non-blocking
┌───────────────────────────────▼──────────────────────────────────────────┐
│ 2. FanoutDispatcher  (one dedicated thread "fanout-dispatcher")          │
│    ConcurrentQueue + WorkerWakeup; policy-bounded by batches and work    │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │ FanoutDeliveryScope: one enqueue per connection
┌───────────────────────────────▼──────────────────────────────────────────┐
│ 3. NetFanoutStripes  (N dedicated threads, connectionId % N)             │
│    bounded Channel<WorkItem> per stripe                                  │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │ SubmitOrderedBatch / SubmitSnapshot — non-blocking
┌───────────────────────────────▼──────────────────────────────────────────┐
│ 4. OutboundMailbox  (per connection, byte bounded, ordered lanes)        │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │ Take() — pump is the only reader
┌───────────────────────────────▼──────────────────────────────────────────┐
│ 5. Socket pump  (per connection, async writes ≤32 frames / 64 KiB)       │
└──────────────────────────────────────────────────────────────────────────┘
```

The simulation thread only ever executes stages 1 and the `Enqueue` call of stage 2. Everything downstream runs
on its own thread and reaches the sim thread only through queues.

## 2. Budgets per stage

| # | Stage | Buffer | Bound | Env knob | Overflow policy |
| --- | --- | --- | --- | --- | --- |
| 1 | Simulation step | per-player command queue | `FINEAGE_ZONE_COMMAND_QUEUE_DEPTH` (default 64) + oldest-command age `FINEAGE_ZONE_COMMAND_QUEUE_MAX_AGE_MS` (default 2000) | same | reject the submit with a named reason; the handler answers the client |
| 1 | Simulation step | command apply time | `FINEAGE_ZONE_COMMAND_APPLY_BUDGET_FRACTION` of the step (default 0.35 = 70 ms) + optional cost cap | same | stop the slice at the tick deadline, keep the deficit for the next slice |
| 2 | FanoutDispatcher | sealed batches | soft 2048 / hard 8192 | `FINEAGE_ZONE_FANOUT_BACKLOG_SOFT` / `_HARD` | hard: shed visuals, then count an emergency |
| 2 | FanoutDispatcher | recipient send work | soft 20 000 000 / hard 80 000 000 | `FINEAGE_ZONE_FANOUT_BACKLOG_SOFT_WORK` / `_HARD_WORK` | hard: drop visual-only batches, shed visual writes |
| 2 | FanoutDispatcher | per-batch send work | 200 000 recipients | `WorldFanoutBatch.DefaultSoftSendWork` | seal time: shed the oldest visual writes |
| 3 | NetFanoutStripes | work items per stripe | 64 … 1 000 000, default 4096 | `FINEAGE_NET_FANOUT_STRIPE_QUEUE` | reject the item; if it carries a must-deliver opcode, close the connection |
| 3 | NetFanoutStripes | stripe count | 1 … 64, default 4 | `FINEAGE_NET_FANOUT_STRIPES` | — |
| 4 | OutboundMailbox | soft bytes | 64 KiB (`NetConnection.MaxBytesPerBatch`) | — | movement coalesced, visual shed, snapshot replaced |
| 4 | OutboundMailbox | soft frames | `max(64 KiB / 14, FINEAGE_ZONE_OUTBOUND_QUEUE)` | `FINEAGE_ZONE_OUTBOUND_QUEUE` (default 256) | same as soft bytes |
| 4 | OutboundMailbox | hard bytes | 1 MiB | `FINEAGE_NET_OUTBOUND_HARD_BYTES` | reliable frame that still does not fit: close the connection |
| 5 | Socket pump | one write | 32 frames / 64 KiB | `NetConnection.MaxFramesPerBatch` / `MaxBytesPerBatch` | none needed: it is a read of the mailbox |

### 2.1 Send work instead of bytes at the hand-off

`WorldFanoutBatch.SendWork` is the size of the hand-off: **one unit per recipient** for a descriptor event
(`AudienceSnapshots.CountOf(audience)`) and **one unit per prepared closure**. A batch with 100 000 recipients costs
100 000 units; a batch with two costs 2.

Why recipient count and not encoded bytes: the frame is encoded on the dispatcher worker, not on the simulation
thread. Counting bytes at seal time would mean encoding there — exactly the work #971 moved off the tick. The
hand-off's memory is dominated by descriptors, payload references and audience slots, all proportional to the
recipient count, so recipient work is the correct proxy for what the queue holds. Encoded bytes are measured
where they exist, as `fineage_fanout_queue_bytes` when the batch leaves the dispatcher.

## 3. Overflow policies

### 3.1 Seal time — per-batch budget (`WorldFanoutBatch`)

When `AddOrdered` pushes a batch past `DefaultSoftSendWork` (200 000 recipients), the batch sheds its **oldest**
visual writes until it is back inside the budget, keeping the newest one (the client is about to see it).
`FanoutKinds.IsVisual` is the same classification `OutboundLanePolicy` uses for the mailbox: `AttackState`,
`MagicSkillUse`, `MagicSkillLaunched`, `MagicSkillCanceled`, `AttackStance`, `SocialAction`, `TargetSelected`,
`TargetUnselected`.
Everything else — knownlist, death, drop, inventory, shop, quest — is never shed at seal time.

### 3.2 Hand-off — backlog budget (`FanoutDispatcher.Enqueue`)

`Enqueue` never blocks and never delivers inline. Before queueing it checks the hard limits; if the incoming batch
would cross them:

1. A batch that carries **only** visual writes is dropped whole (`TryDropVisualOnlyBatch`) — its audiences are
   released and `fineage_fanout_overflow_shed_total` increments. The client loses an animation, not state.
2. A mixed batch sheds its visual writes down to the room left under the hard limit (`ShedVisualsForOverflow`).
   Reliable writes stay queued even if the batch is then over the limit.
3. If reliable writes alone cross the hard limit, the batch is still queued and the enqueue is counted on
   `fineage_fanout_backlog_emergency_total`. **That counter must be 0 in normal runs.** Losing a reliable frame is
   worse than a long backlog: the alternative is a client that never learns a mob died.

The hard limits are a policy bound, not a queue bound: `ConcurrentQueue` cannot refuse a write. The dispatcher
therefore counts what it accepts and reacts when the count is exceeded, which is what keeps memory bounded in
practice. A reliable-only flood at the hard limit is the one case where the count keeps growing; the emergency
counter and the per-second error log are the signal that the worker is losing to the world.

### 3.3 Stripe — per-connection queue (`NetFanoutStripes`)

`TryWrite` on a bounded channel. A full stripe rejects the item and counts it on `fineage_outbound_lane_blocked_total`
with the lane label. If the rejected item carries a must-deliver opcode, the connection is closed
(`FailOutbound`) so the client re-enters and gets a fresh knownlist; otherwise only the lane's `Full` counter moves.
Ordering per connection is preserved because a connection always hashes to one stripe.

### 3.4 Mailbox — per-connection byte budget (`OutboundMailbox`)

Four lanes, cheapest-to-lose first:

| Lane | Examples | Policy |
| --- | --- | --- |
| `Movement` | `MoveState`, `ChangeMoveType`, `PositionCorrection`, `StatusUpdate`, `AbnormalStatusUpdate` | coalesced to the newest per entity; `StatusUpdate` merges attributes so nothing is lost |
| `Visual` | `AttackState`, `MagicSkillUse`, `MagicSkillLaunched`, `MagicSkillCanceled`, `AutoSoulShot`, `SocialAction`, `TargetSelected` | shed oldest first, or dropped when the newest does not fit |
| `Snapshot` | `EntitySpawn`, `EntityDespawn`, `RelationChanged`, `ChangeWaitType` | newest snapshot replaces the pending one |
| `Reliable` | everything else | never dropped; a reliable frame that does not fit under the hard limit closes the connection |

The first admission on an empty mailbox may exceed the soft limit (`burst`) — one enter-world projection is one
batch, and refusing it would make entering the world impossible. Bytes already handed to the pump still count, so a
slow socket cannot make the mailbox look empty.

### 3.5 Socket pump

`Take()` drains the mailbox into a bounded list and writes at most 32 frames / 64 KiB per socket write. Writes are
async; the pump never blocks a producer. A write failure closes the connection.

## 4. Telemetry per stage

| Stage | Metric | Type | Meaning |
| --- | --- | --- | --- |
| 1 | `fineage_tick_ms` | histogram | step duration |
| 1 | `fineage_tick_stage_ms` | histogram (`stage`) | per-stage duration |
| 1 | `fineage_command_queue_depth` / `fineage_command_queue_age_ms` | histogram | ingress backlog |
| 1 | `fineage_command_rejected_total` | counter (`reason`) | admission refusals (`depth`, `age`, `closed`) |
| 2 | `fineage.fanout.dispatcher.enqueue_wait.ms` | histogram (OTel only) | time the sim thread spent handing a batch over |
| 2 | `fineage_fanout_queue_depth` / `fineage_fanout_queue_work` | histogram | batches and recipient work waiting on the dispatcher |
| 2 | `fineage_fanout_queue_oldest_age_ms` | histogram | **fan-out queue age — the ≤ 40 ms gate** |
| 2 | `fineage_fanout_dispatch_wait_ms` | histogram | per batch: enqueue → worker start |
| 2 | `fineage_fanout_batch_process_ms` | histogram | per batch: worker execution |
| 2 | `fineage_fanout_events_per_batch` / `fineage_fanout_recipients_per_event` | histogram | batch shape |
| 2 | `fineage_fanout_queue_bytes` | histogram | encoded bytes leaving the worker |
| 2 | `fineage_fanout_shed_visual_total` | counter | visual writes shed at seal time or on overflow |
| 2 | `fineage_fanout_overflow_shed_total` | counter | hand-offs that hit the hard limit and shed |
| 2 | `fineage_fanout_backlog_soft_exceeded_total` | counter | enqueues above the soft limit |
| 2 | `fineage_fanout_backlog_emergency_total` | counter | enqueues above the hard limit — **must be 0** |
| 2 | `fineage_fanout_overflow_total` | counter | batches refused because the dispatcher was closed |
| 3 | `fineage.fanout.stripe.enqueue_wait.ms` | histogram (`stripe`, OTel only) | time the worker spent writing into a stripe |
| 3 | `fineage_outbound_lane_blocked_total` | counter (`lane`) | full stripe |
| 3 | `fineage_outbound_lane_full_total` | counter (`lane`) | mailbox refused the item |
| 4 | `fineage_outbound_queue_frames` / `_bytes` | histogram | mailbox depth at admission |
| 4 | `fineage_outbound_queue_oldest_age_ms` | histogram | age of the oldest frame in a socket write |
| 4 | `fineage_outbound_lane_coalesced_total` / `_evicted_total` | counter (`lane`) | movement coalesced, stale frame evicted |
| 4 | `fineage_outbound_shed_bytes_total` | counter (`lane`) | bytes shed by lane |
| 4 | `fineage_outbound_slow_client_disconnect_total` | counter | connections closed for a full mailbox |
| 5 | `fineage_socket_write_ms` / `_bytes` / `_frames` | histogram | socket write shape |
| 5 | `fineage_fanout_enqueue_to_socket_write_ms` | histogram | end-to-end hand-off → first byte of the write |

The gate in [`acceptance-gates.md`](acceptance-gates.md) is `fineage_fanout_queue_oldest_age_ms` p99 ≤ 40 ms.

## 5. Lock audit of `Step()`

Scope: every `lock`, `Monitor`, `SemaphoreSlim`, `.Wait`, `.Result`, `.GetAwaiter().GetResult()` and synchronous I/O
reachable from `ZoneWorld.RunStep` → `RegionTickScheduler.Step` → region bodies → `FanoutDispatcher.Enqueue`.
"Shared" means another thread can hold the same object while the simulation thread wants it.

| Lock | Where | Held by | Shared with fan-out worker / net pump? | Verdict |
| --- | --- | --- | --- | --- |
| `WorldAffinity._turns` | `WorldAffinity.Step` | simulation thread; also request/logout paths taking a turn | **No.** Net pumps and the fan-out worker never take a world turn; `WorldAffinity.Step` throws inside a region body. | safe — request paths queue behind the step, not the reverse |
| `ZoneCommandIngress._lock` | `Submit`, `BeginApply`, `FinishApply`, `Record`, `Take`, `Close` | sim thread and handler/pump threads submitting | Partly. A pump can hold it while the sim thread opens a slice. | safe — the critical sections are dictionary/list mutations only; all logging and histogram recording happen **after** the lock is released (the `QueueSample`/`RecordSample` split from #971 step 2) |
| `ZoneCommandIngress._submitted` (`SemaphoreSlim`) | `Submit` → `Release`, `WaitForCommandAsync` | sim thread waits | **No.** Only ingress producers release; the sim loop is the only waiter. | safe — it is the sim's own wake signal, not a mutual exclusion |
| `MetricsCollector._gate` | `Snapshot`, `Buffer`, `DrainLoop`, `DrainAllLocked` | metrics drain thread + scrape endpoint | **No.** Recording threads append to a thread-local lock-free block; the step never takes `_gate`. | safe — #971 step 1 moved recording off the collector lock |
| `AudienceSnapshots.Gate` | `Capture`, `Adopt`, `AddRef`, `Release`, `CountOf`, `Watchers` | sim thread and the **fan-out worker** | **Yes.** `CountOf` at seal time and `Watchers` in `FanoutDispatch.Invoke` both take it. | safe but real: the critical section is a dictionary lookup and an array-reference read, no I/O and no allocation. It is the one lock genuinely shared with the worker; keep it that way. |
| `TrackedLock _economyLock` ("economy") | `ZoneWorld.Members.Persist`, `Members.Leave`, `Regen`, `Ledger`, `NpcHelpers` | sim thread only | **No.** No net or fan-out code takes it. | safe |
| `CharacterPersistWorker._gate` | `Enqueue`, `Loop`, `Drain`, `FlushSync` | sim thread (`Enqueue`) and the `fineage-character-persist` thread | **No.** The worker thread never touches the world. | safe — `Enqueue` only appends and pulses; the worker does the I/O |
| `EconomyPersistService._queue` (bounded channel) | `TryEnqueue` | sim thread; consumer is the persist thread | **No.** | safe — `TryEnqueue` is non-blocking and returns a refusal instead of waiting |
| `NetPresenceHub._writeLock` | `Register`, `Unregister` | handler / enter-world path | **No.** The send path reads the published immutable table without the lock. | safe |
| `ZoneWorldNpcThink._thinkRescheduleLock`, `_privatesPendingLock` | think reschedule, private spawn | sim thread and region bodies | **No.** | safe |
| `CharacterPersistWorker`/`EconomyPersistService` `.Wait`/`Join` | shutdown and tests only | — | **No.** Guarded by `EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread` and `StepWaitRegistry`. | safe |
| `SimulationSyncIoGuard._gate` | `Record` | sim thread only, and only when `_depth > 0` | **No.** | safe |

### 5.1 Guard rails that keep the audit true

- `StepWaitRegistry` throws when the simulation thread blocks on a name containing `channel`, `fanout-dispatcher`
  or an explicitly registered wait. Registering a new blocking wait on the step path is therefore a test failure,
  not a silent regression.
- `EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread` throws when a step waits on the economy store.
- `SimulationThreadMarker` marks the step thread; `WorldAffinity.Require` throws when a non-step thread touches
  the world.
- `TrackedLock` records its holder and waiters so `StepHangDiagnostics` can name a stuck lock in a watchdog report.

### 5.2 Known residuals

- `AudienceSnapshots.Gate` is shared with the fan-out worker (see the table). It is safe today because the section
  is a lookup; a future change that computes or encodes inside it would put the sim thread behind the worker.
- The dispatcher's hard limits are enforced by counting, not by a refusing queue. A reliable-only flood can still
  exceed them; the emergency counter is the alarm, and the fix would be a refusing queue, not a bigger bound.

## 6. Sustained-lag behaviour

`FanoutDispatcher.BatchDelayForTest` lets a test hold the worker without blocking producers.
`FanoutDispatcherBackpressureTests.Sustained_lag_keeps_the_backlog_bounded_and_the_enqueue_never_blocks` floods the
dispatcher with visual-only batches while the worker is artificially slow and asserts:

- every `Enqueue` returns without blocking,
- `Backlog` stays at or below the hard batch limit,
- `BacklogWork` stays at or below the hard send-work limit,
- `OverflowDrops` grows (the shed is visible, not silent).

The same test class covers the per-batch seal shed, the "never shed gameplay" rule, the visual-only drop and the
mixed-batch shed.

## 7. Verification

| Check | Where |
| --- | --- |
| Backlog by batches and send work | `FanoutDispatcherBackpressureTests` |
| Seal-time and overflow shedding | `FanoutDispatcherBackpressureTests` |
| Sustained worker lag | `FanoutDispatcherBackpressureTests` |
| Fan-out queue age p99 ≤ 40 ms | `docs/perf/acceptance-gates.md`, measured on `bots-200-town` and `perf-aoe-multiparty` |
| Lock audit | this document, §5 |

Heavy scenarios (`load-test-smoke`, `zone-simulation-perf`) run on push to `main`, on `main` PRs, nightly and on a
`dev` PR labelled `perf`. An ordinary `dev` PR only runs the fast suite, so a perf PR must add the label to get the
numbers.
