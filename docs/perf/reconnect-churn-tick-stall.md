# Reconnect churn tick stall (#1110)

## Symptom

The #990 matrix (`reconnect-storm`, 200 bots, Debug) recorded one 30.8 s freeze of the simulation tick, and
160/200 bots dropped with `relog handshake exceeded 30s` at `zone_hello`. Every step stayed under 100 ms:
the tick-time histogram never saw the freeze.

## Root cause: thread-pool starvation

- **Logout writes ran synchronously on pool threads.** `LeaveAsync` ran `PersistUnlinkSnapshot`, which goes
  through `TryCommitDirtyEconomy` → `WaitForBatch`, and `FlushCharacterSnapshots` → `CharacterPersistWorker.FlushSync`.
  These are store writes behind the single `PostgresAccountStore` write gate. Under a relog storm, stack dumps
  showed 72 pool threads blocked on that gate and 9–24 in `WaitForBatch`. The thread-pool queue was about 300.
- **The simulation loop, its step watchdog and the economy persist worker all resumed through the pool.** The loop
  is `Task.Delay`/`WaitAsync`, the watchdog is `Task.Delay`, and the worker is `WaitToReadAsync().AsTask().GetResult()`.
  With every pool thread blocked, the loop could not resume for its next tick. The economy commits it was waiting
  on could not land either. So the tick froze until the `WaitForBatch` timeouts (30 s) released threads.
  The watchdog was starved too, so it could not report the freeze.

## Shared mechanisms changed

- The simulation loop and the step watchdog run on dedicated long-running threads. They wait on the command
  ingress semaphore and on the cancellation wait handle synchronously, so they never need a pool thread to tick.
- The economy persist worker wakes on a `SemaphoreSlim` on its own thread, not through a pool continuation.
- `BlockingWorkLane` is a fixed pair of `fineage-logout-persist` threads. The leave path and the evicted-snapshot
  path hand their blocking store writes to it and await the result, so a relog storm queues on the lane instead of
  consuming the pool. Shutdown drains the lane before the snapshot worker stops.
- The `SimulationPoolStarvationTests` block every pool thread on purpose. The tick keeps advancing and an
  economy commit still lands. Both tests fail on the old code.
- `reconnect-storm` gates the longest step (≤ 1000 ms) and the longest tick-counter freeze (≤ 10 s); see
  [`acceptance-gates.md`](acceptance-gates.md).

## Numbers (Debug, local stack, `reconnect-storm`, 200 bots, 120 s)

| Run | Code | Longest freeze | Longest step | Relogs ok / attempted | Disconnects |
| --- | --- | ---: | ---: | ---: | ---: |
| before2 | dev | **32.0 s** | 85 ms | 0 / 160 | 160 |
| before3 | dev | 1.7 s | 210 ms | 5284 / 5292 | 8 |
| before4 | dev | 1.5 s | 211 ms | 635 / 794 | 160 |
| before5 | dev | **32.0 s** | 59 ms | 0 / 160 | 160 |
| before6 | dev | 4.2 s | 168 ms | 940 / 1100 | 160 |
| after1 | loop + watchdog + worker only | 2.7 s | 157 ms | 625 / 785 | 160 |
| after2 | full fix | 1.4 s | 237 ms | 7850 / 7852 | 2 |
| after3 | full fix + gates | 4.9 s | 215 ms | 10265 / 10272 | 7 |

So 2 of 5 runs on `dev` froze for 32 s, and 4 of 5 lost 160 relogs at the 30 s handshake. With the full fix, no run
froze, and relog throughput rose from at most about 5300 to about 10000 per run. The longest step grew from about
85 ms to about 215 ms because the zone now serves those relogs instead of waiting. The pre-existing consistency
oracle (#1047) and gatekeeper-travel failures are separate from this issue.

## Follow-up (#1112): enter-world reads behind the write gate, NpcTalk replies, carried economy replies

Two more reconnect-storm runs on dev after #1111 still dropped 160/200 bots at the 30 s handshake, with the tick
running. Stack samples showed 23–39 pool threads in `PostgresAccountStore.GetLanguage` → `EnterPersist`. The
enter-world handler queued a one-row read behind every write in the process. Pure reads (`GetLanguage`,
`FindPet`, `ListAllPets`, `PetNameExists`) now open their own pooled connection outside the write gate.

In those runs the gatekeeper "no dialog after 0 page(s)" failures were the same starvation: the talk reply came
after the bot's 10 s window. The server also had three ways to answer a talk with nothing. They are now refusals,
matching Mobius `setIntentionInteract` / `canInteract`:
- Out of range and sitting → `action_sitting`. Out of range and casting → `npc_busy`.
- Standing under a ledge the sphere cannot reach → `range`.
- A pending talk refused on arrival → SystemMessage 22 instead of a discarded result.

`economy_commit_failed` on teleport fees came from the step's economy batch when it could not be queued: a logout
commit had stolen those characters' dirty bags and still held them, or the worker queue was full. The batch was
already restored and was committed one step later, so the client heard a failure for a fee that landed. Those
replies now wait for the commit that carries them, and are refused only after 10 s without one
(`ZoneWorldEconomyCarry`).

The write gate itself is not the limit at this load once reads leave it. Measured in rs-after2 (Debug, 200 bots,
reconnect-storm, `fineage_persist_write_ms{kind="store_gate_*"}`):
- 61,526 writes, wait avg 0.32 ms and p99 ≤ 5 ms.
- Hold avg 0.61 ms and p99 ≤ 5 ms.
- The gate was busy about 25 % of the run.

| Run (Debug, reconnect-storm, 200 bots) | Disconnects | Relogs ok / attempted | Teleports ok / attempted | Gatekeeper unexplained | `economy_commit_failed` rejects |
| --- | ---: | ---: | ---: | ---: | ---: |
| rs-before1 (dev 84e60e76) | 160 | 8744 / 8904 | 620 / 647 | 62 | 27 |
| rs-before2 (dev 84e60e76) | 160 | 5587 / 5747 | 582 / 584 | 41 | 15 |
| rs-after2 (#1112) | 5 | 10074 / 10079 | 912 / 912 | 0 | 0 |
| rs-after3 (#1112) | 14 (initial enter, bots 99–112) | 6429 / 6429 | 596 / 599 | 0 | 0 |

`gatekeeper-travel` (150 travellers, no churn) was already clean on dev and stays clean: 5707/5725 → 5727/5738
teleports, 0 unexplained.
