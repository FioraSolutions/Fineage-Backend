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

## #1114: consistency oracle under relog, ack-before-durable, ramp handshake timeouts

Debug, reconnect-storm (200 bots, 30 s ramp, 120 s, relog 1.0, 20 % gatekeeper travellers).

| | dev 2db35263 (rs-after2 / rs-after3) | cons-1114 (cons-b) |
| --- | --- | --- |
| Consistency issues | 12,216 / 6,672 (all DuplicateSpawn, plus 2 StalePosition) | **0** |
| Runner | FAIL / FAIL | **PASS** |
| First-login handshake timeouts | 0 / 14 | 0 (no host pause in this run) |
| Disconnects | 5 / 14 | 6 (`enter_failed: character N changed while loading`) |
| Relogs ok / attempted | 10074/10079, 6429/6429 | 10553/10559 |
| Gatekeeper teleports ok / attempted, unexplained | 912/912, 0 | 916/916, 0 |
| `economy_commit_failed` replies | 0 / 0 | 0 (171 replies carried to their own commit, 0 refused) |
| tick p99 / max ms | 204 / 213, 214 / 749 | 206 / 221 |

gatekeeper-travel (150 bots): 5705/5718 teleports, 0 unexplained, runner PASS, 18 StalePosition. Before: 14 StalePosition, below the gate. These are teleport views and were left as is.

1. **DuplicateSpawn was an oracle false positive.** Every issue had a relogging observer. Travellers never relog, and they had 0. The bot tracker kept the previous zone connection's knownlist through `RelogAsync`. A new session starts with an empty client world (Mobius `RequestRestart` → `RestartResponse`, then `EnterWorld` re-adds everything in view), so each relog counted one spawn without a despawn for every object in view. The fix: a local session-boundary frame (`BotObservationLog.SessionBoundaryOpcode`) ends every life at the relog. A duplicate inside one session is still flagged. Gates are unchanged.
2. **Ack before durable.** A logout commit steals every dirty bag. When it took the bag that a step's command had just dirtied, the step found nothing to commit. It answered success while that logout batch was still in the worker. If the batch failed, it restored the bag and the client had already heard success. The world now counts characters whose economy is in a stolen, unfinished batch (`_economyInFlight`, ended exactly once by publish or restore under `_economyLock`). A step reply whose character the step touched (`_economyTouched`) and whose change sits in another commit's batch is carried (`ZoneWorldEconomyCarry`) until that batch is durable or restored and re-committed. `EconomyAckAfterDurableTests` fails on dev with an early `success`.
3. **The 14 ramp handshake timeouts happened during a box-wide freeze, not in a slow segment of the login chain.** The Zone log went silent from 19:58:08.7 to 19:59:49.0 UTC. Then the tick clock skipped 500 steps with only 178 ms of lateness. The bot launcher, auth, and the independent 1 s tick sampler stopped at the same moment (the sampler recorded no stall). The failed bots (99–112) all had their enter accepted (`enter ok` at 19:58:05.9–07.9) and still had a handshake in flight when the freeze began. On resume, their 30 s handshake budget (monotonic) had run out, so the pending read was cancelled at phase `enter_world`. A second freeze happened at 20:01:11–20:01:39: Zone LateMs 28 s, and the load-test time series is missing that window. The load test now runs a `HostPauseDetector` thread that records such spans in `report.HostPauses` and as a P1 environment bug. Every handshake timeout also lists its per-phase durations (`login_connect … enter_world, enter_settle(open)`) and the host pause inside it, so the next one names its segment directly.
