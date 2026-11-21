**Verdict: not OK yet, and do not turn on `ParallelRegionExecutor`.** I made no code changes, commits, pushes or PRs.

The structure of the refactor is sound. Every currently enabled step is implemented, and it holds up well with the sequential executor (one worker): 
- The tick scheduler, region containers, per-region AOI and border snapshots all work as designed.
- Seeded random streams and replay of the command log work.
- The parallel executor's own mechanics are correct.

But there are two latent production crashes, and the auditable economy ledger can be left half-applied. On top of that, one crash is all but certain after the first restart, and every graceful restart can silently lose player saves.

**Tests:** Game 446, Net 47, Login 115 and Zone 1385 all pass. I ran the Zone suite three times with no failures and none skipped. One caveat: the Postgres journal tests "pass" without a database, so CI never runs them (see P1-14).

**Is the parallel executor safe to enable?** Its mechanics are correct for today's parallel bodies (player regen, NPC regen and abnormals):
- Shared dictionaries are only read while bodies run.
- Fan-out goes into per-region batches.
- Party updates are deferred to the barrier.
- Damage-over-time cannot kill inside a body.
- Ledger moves are refused inside bodies.

It is still blocked, for four reasons:
- The abnormals body throws for olympiad fighters (P0-2). This also happens with the sequential executor.
- The only guard against touching another region's state is on directory writes. Stat reads write to the entity, and the social actors have no guard at all (P1-11).
- No test runs the risky bodies (duel, olympiad, toggles) under the parallel executor, and CI never runs that mode.

Turn it on only after P0-2, P1-11 and P1-12 are done.

---

## P0: must fix

**P0-1. The ingress wake-up semaphore grows without bound**
- **Files:** `src/Fineage.Zone/World/ZoneCommandIngress.cs`, `src/Fineage.Zone/World/ZoneWorldCommands.cs`
- **Problem:** Every submission increments a semaphore that the production loop never waits on.

```12:12:src/Fineage.Zone/World/ZoneCommandIngress.cs
    readonly SemaphoreSlim _submitted = new(0);
```

```42:42:src/Fineage.Zone/World/ZoneCommandIngress.cs
        _submitted.Release();
```

  Only `WaitForSubmissionAsync` (line 92) consumes it, and only `PumpCommandsAsync` calls that. That path is used by tests alone (`NetZoneSession.cs:63`, `WorldAffinityTests.cs:26,52`). `SimulationLoopAsync` in `ZoneWorldRegen.cs` never waits. After 2³¹ submissions, `Release` throws `SemaphoreFullException` inside `Submit`, so every command and request fails from then on.
- **Fix:** Use a wake-up signal that cannot accumulate. Either keep one `TaskCompletionSource` that is swapped under `_lock` and completed once, or use `SemaphoreSlim(0, 1)` and only release it when `CurrentCount == 0` (under the lock).
- **Acceptance:**
  - A unit test submits 10,000 commands with no pump attached. The internal signal count stays at 1 or below, exposed through an `internal` test property.
  - The existing pump tests still wake up once per batch.

**P0-2. The abnormals region body reaches the olympiad actor through the stat pipeline**
- **Files:** `src/Fineage.Zone/World/ZoneWorldStats.cs`, `src/Fineage.Zone/Olympiad/OlympiadActor.cs`, `src/Fineage.Zone/World/ZoneWorldAbnormals.Tick.cs`
- **Problem:** When a buff expires or ticks, `FanoutAbnormal` (`ZoneWorldAbnormals.Tick.cs:151-170`) runs this chain: `SelfUserInfo` → `BuildUserInfo` → `BuildUserInfoVitals` → `PlayerStats` → `GearBases`.

```120:120:src/Fineage.Zone/World/ZoneWorldStats.cs
            _olympiadArena.Contains(player.ObjectId) ? _olympiad.Board.Settings.EnchantLimit : -1);
```

  The `Board` getter calls `RequireActorThread()` (`OlympiadActor.cs:20-24`), which throws whenever a region body is current, on either executor. `RegionStage.RunRegion` has no per-entity isolation (`Simulation/IRegionStage.cs:38-42`), so one throw skips abnormals for every remaining entity in that region for that tick. This was introduced in a54f5b43 (step 9).
- **Fix:** Bodies must not read the olympiad enchant limit through the actor. Either copy it into a world field when the settings load, or store it on the entity when the fighter enters the arena. Then grep for every other actor getter that the stat and user-info pipeline can reach (siege, clans, seven signs) and fix them the same way.
- **Acceptance:**
  - A test puts a player in an olympiad arena with a timed buff, advances past its expiry, and asserts that there are no stage failures and that the user-info packet is sent.
  - The test runs with both the sequential executor and `ParallelRegionExecutor(4)`.

**P0-3. The item id allocator is not seeded on boot (known gap, raised to P0 because of its consequences)**
- **Files:** `src/Fineage.Zone/World/ZoneWorld.cs:222`, `src/Fineage.Zone/Program.cs:~80`, `src/Fineage.Game/ItemObjectIdAllocator.cs`, the account store (its next item id), `src/Fineage.Game/Economy/EconomyLedger.cs`
- **Problem:** `_itemIds = itemIds ?? new ItemObjectIdAllocator()` starts at `PlayerInventory.FirstObjectId = 100_000`, which is the same base the store uses. The journal is replayed on boot, so the `_owners` map contains every instance id that has ever moved. After a restart:
  1. A non-stackable loot drop gets an id that is already owned (`SpawnGroundStack`, `ZoneWorldGround.Members.cs:268`).
  2. The ledger returns `InstanceTaken` (`EconomyLedger.cs:245`).
  3. `LedgerMove.Commit` throws `LedgerReconciliationException` (`LedgerMove.cs:103-104`).
  4. That exception fires inside the kill-handling world stage, after the ground item already exists (`ZoneWorldGround.Members.cs:230-238`).
- **Fix:**
  - At boot, seed the allocator with `max(store.NextItemObjectId, ledger.MaxInstanceId + 1)`. Add `MaxInstanceId` to `EconomyLedger`.
  - Fail the boot with a clear message if the store cannot provide its next id.
  - Remove the default `new ItemObjectIdAllocator()` from the production constructor path.
- **Acceptance:**
  - A test boots a world on a journal that contains instance N owned by an offline character, kills a mob that drops a non-stackable item, and asserts that the loot commit succeeds with an id other than N.
  - Booting with a store that cannot report its next id fails with a message naming what is missing.

**P0-4. Ledger moves change world state before the ledger accepts them**
- **Files:** `src/Fineage.Game/Economy/LedgerMove.cs`, `src/Fineage.Game/Economy/EconomyLedger.cs`, `src/Fineage.Zone/World/ZoneWorldTrade.More.cs`, `src/Fineage.Zone/World/ZoneWorldLedger.cs`, `src/Fineage.Zone/World/ZoneWorldGround.Members.cs`
- **Problem:** Every caller mutates first and commits afterwards. `Commit` throws if the ledger rejects the move or the result does not match.

```151:157:src/Fineage.Zone/World/ZoneWorldTrade.More.cs
            TransferOfferRow(requestor.Inventory, partner.Inventory, row, requestorChanges, partnerChanges);
            ...
        move?.Commit(LedgerHoldingsOf);
        ...
        SaveInventory(requestor.Session, requestor.Inventory);
```

  If a trade is rejected, both bags have already changed, `SaveInventory` is skipped, and the clients are not updated. The same pattern appears in:
  - `RecordBagChange`: `change()` at line 135, then `Commit` at line 142.
  - `CommitShopMove` (line 187).
  - Pickup and drop (lines 92 and 185).
  - Loot (lines 230-238).
- **Fix:**
  - Split `EconomyLedger.Apply` into a pure `Check(tx)` and an `Apply` step.
  - In trade, private store, shop, warehouse, pickup, drop and loot, the postings are known in advance, so build the transaction and call `Check` before mutating. On rejection, refuse the command and change nothing, logging the transaction id and the rejection.
  - Keep the after-state comparison as a bug detector.
  - For loot, check before `SpawnGroundStack`.
  - Do the quest and craft path of `RecordBagChange` in a follow-up PR that snapshots the bag and restores it on rejection.
- **Acceptance:**
  - A test forces `InstanceTaken` on a trade. Both bags are unchanged, a refusal is sent, and no exception escapes.
  - The same test exists for shop buy and for loot: no ground item spawns, and the stage does not fail.

---

## P1: needed before calling the refactor complete

**P1-5. Value moves that bypass the ledger, beyond the known gaps.** Today every one of these is silently absorbed as an `Adjustment` against `Untracked` the next time the account is observed (`LedgerMove.cs:30-31, 64-76`). Do one PR per group. Each PR adds a ledger kind, a system account and a test that checks the postings exactly.
- **P1-5a, castle treasury and manor:** treasury deposit and withdraw, door and wall upgrades, circlet grants, manor seed purchases, and crop sales into the treasury.
- **P1-5b, scheduled payouts:** lottery ticket purchases and prizes, seven signs and mammon exchanges, and olympiad rewards and points-to-item exchange.
- **P1-5c, gathering and items:** fishing rewards, sweeper (spoil) results, pet-to-owner item transfers and pet feeding, enchant results (failure crystals, destroyed items), and crystallization.
- **P1-5d, fee sinks:** teleport, dungeon entry, clan level-up and clan skill costs, skill books, henna (henna fees are also never charged at all, which must be fixed at the same time), and class-change items.
- **P1-5e, GM item grants and the starter kit:** `CreateStarterInventory(..., _itemIds)` runs off the simulation thread in `ZoneWorld.More.Enter.cs:50`.
- **P1-5f, consumption:** potions, soulshots, arrows, recipes, summon upkeep, and item costs on skills. Post these under a `Consume` kind instead of relying on the alignment adjustment.

**P1-6. Adjustments leave no trace.** `Observe` → `Alignment` posts an `Adjustment` against `Untracked` with no log or metric, so bypasses cannot be seen in production.
- **Fix:** Log a warning that names the account, item, amount and move kind, and add a metric `ledger_adjustments_total{move_kind}`. Add a `strict` option that throws instead, and turn it on in tests once P1-5 lands.
- **Acceptance:** A test triggers a known bypass and asserts both the log line and the metric. The strict-mode tests fail on any adjustment.

**P1-7. Silent fallback for ground holdings.** In `ZoneWorldLedger.cs:47-52` (new in ea26cc56), when the ground entity is missing, `LedgerHoldingsOf` substitutes `new LedgerHoldings()`. Every other account kind throws.
- **Fix:** Throw with the ground object id in the message, or post an explicit `Expire` move when the ground item despawns.
- **Acceptance:** A test removes a ground entity without a ledger move and asserts the error names its id.

**P1-8. Ground accounts outlive the process.** Ground stacks that still exist at shutdown remain in the journal as held value. After a restart, a new ground stack that reuses an id observes "empty" and wipes the old balance through an adjustment. Ground accounts are keyed by object id (`ZoneWorldGroundLedger.cs`, `PostOntoGround`).
- **Fix:** At boot, after the journal replay, post one `Expire` per non-empty ground account into a system account.
- **Acceptance:** A test runs boot, drop, a simulated restart, and boot again. The journal shows the expiry and no adjustment.

**P1-9. Journal durability.** Three problems:
- `PostgresLedgerJournal` writes its buffer in its own transaction, separate from the inventory saves (`ZoneWorld.Members.Persist.cs`: `_ledger.Flush()`, then bags, then warehouses).
- When a flush fails, the batch is re-queued, but commands keep being answered OK.
- `Program.cs:156-159` ends with `shutdown.Cancel(); bosses.Save();` and never flushes the ledger at the end.

  **Fix:** Add a final ledger flush on shutdown. Log failed flushes with the batch size and count them in a metric. Document the ordering in the doc: journal first, then snapshots, so recovery has a defined direction.

  **Acceptance:**
  - A journal test fails the first flush and succeeds on the second. No entries are lost and a metric is recorded.
  - A shutdown test asserts that the buffer is empty after the shutdown path runs.

**P1-10. Shutdown loses leaving players silently.** The order is:
1. The simulation loop calls `_ingress.Close(...)` on cancel (`ZoneWorldRegen.cs:52-57`).
2. Leaves are then refused, and `SessionLifecyclePacketHandler.Enter.cs:136-139` swallows the failure in `catch { }`.
3. `NetTcpServer` handlers are fire-and-forget and never awaited.

  Online players' state since their last save is lost on every restart. Part of this is pre-existing (handlers were never awaited), and step 12 made it worse.

  **Fix:** Shut down in order. Stop accepting connections, run one final request that detaches and saves every online player, flush, close ingress, and only then exit. Replace `catch { }` with a log line that names the session and the exception.

  **Acceptance:** A test with two online players runs shutdown and asserts both saved snapshots and the empty ledger buffer.

**P1-11. Writes that happen during reads, and the missing guard.**
- `PlayerStatMods` calls `RefreshGearSkills(player)` (`ZoneWorldStats.cs:138, 182, 190`), which rewrites `player.GearSkills` on every stat read. A body reading another region's player would race with that player's own body.
- The social actors also have no `RequireActorThread`. The doc says "Actors keep their internal locks", but they have none.

  **Fix:** Rebuild gear skills only on equip changes. Add the actor-thread guard to the social actors, and a debug assertion that bodies never write to entities outside their own region.

  **Acceptance:** A test reads the stats of a player in another region from a body under `ParallelRegionExecutor(4)` and finds no mutation. A test proves the social actors throw when called from a body.

**P1-12. Parallel coverage and a CI gate.** `RegionParallelWorldTests` compares sequential and parallel runs, but has no olympiad, duel, toggle or fake-death entities. `NetTestKit` accepts a `regionExecutor` but ignores `FINEAGE_ZONE_REGION_WORKERS`.
- **Fix:** Add those entity states to the comparison test. Make the test kit honour the environment variable. Add a CI job that runs the Zone suite with 4 workers.
- **Acceptance:** That CI job passes, and the comparison test covers each of the listed states.

**P1-13. Determinism gaps on paths that draw random numbers.**
- **(a) Iteration order:** multi-target rules iterate `_byId.Values` or `Players()`, which follow dictionary insertion order, while drawing from seeded streams. Examples: area skills, skill effects, channelled skills, NPC skills and auto-attacks. The doc requires ascending-id order (§4.2).
  - **Fix:** Use one shared ordered enumeration helper.
  - **Acceptance:** Two worlds with the same entities inserted in different orders produce identical results.
- **(b) Unlogged requests:** enter runs as an unlogged request but calls `SpawnFor` (`ZoneWorld.More.Enter.cs:152`), which draws from `_spawnRandom`. The starter kit allocates ids off the simulation thread.
  - **Fix:** Record enter in the simulation log, or make the spawn choice a pure function of the character id. Allocate starter ids on the simulation thread.
  - **Acceptance:** A replay test with an enter in the middle of the recording reproduces the final state exactly.

**P1-14. The Postgres journal test never really runs.** `tests/Fineage.Login.Tests/PostgresLedgerJournalTests.cs` does `return;` when there is no database, and `.github/workflows/build-and-test.yml` has no Postgres service.
- **Fix:** Add a Postgres service to CI. Without a database, mark the test explicitly skipped with a reason instead of passing it.
- **Acceptance:** The CI log shows those tests executed against Postgres.

**P1-15. Torn position reads in the movement handler.** `MovementPacketHandler.cs:83-125` reads `zoneSession.X/Y/Z` after `await ExecuteAsync`, while the simulation thread may be writing them.
- **Fix:** Return the position from the command result, the same way `ExecuteAroundAsync` returns a plan.
- **Acceptance:** The handler no longer reads session coordinates after the await. A test asserts the reply matches the committed position.

**P1-16. Silent default journal.** `ZoneWorld.cs:260` uses `ledgerJournal ?? new MemoryLedgerJournal()` (new in 4e425a2a). A misconfigured production boot would keep an in-memory ledger that vanishes on restart.
- **Fix:** Make the journal required in production construction, and pass the memory journal explicitly in tests.
- **Acceptance:** Constructing the world without a journal fails with a message naming the missing journal.

---

## P2: cleanup and hardening

**P2-17. Leftovers from the old `_gate` design.**
- There are 139 `using (_affinity.Step())` sites, most of them `*ForTest`.
- These public APIs have no production callers and change state outside a recorded step, which means an unpinned clock, no recorded batch and no ledger: `TryGrantItemAsync`, `TryAdminKillPlayerAsync`, `AdvanceOlympiadAsync`, `AdvanceSevenSignsAsync`, `TrySetNoble`, `TrySetPlayerDead`, `TrySetEquipStanding`, `PlayerDungeonId`, `CompleteDueDissolutions`.
- `TryAdminSpoilAsync` has zero callers.

  **Fix:** Route test hooks through a single `RunStepForTest` helper that pins the clock and records the batch. Make the test-only APIs `internal`, and delete `TryAdminSpoilAsync`.

  **Acceptance:** No public mutating API runs outside a step. The `Step()` call-site count drops to the helper alone.

**P2-18. Naming and dead code.**
- `*Locked` / `*Unlocked` method names (for example `KillBossLocked`, `TryGetLivePlayerUnlocked`).
- A `SimulationLog` comment that still says "took the world gate".
- `_lastPresenceSaveMs` is still a `ConcurrentDictionary`.
- `IEconomyLedger.SubmitAsync` is unused (`EconomyLedger.cs:9, 70`).

  **Acceptance:** None of these remain.

**P2-19. Other silent fallbacks.**
- `FINEAGE_ZONE_TICK_MS` silently falls back or clamps in `ZoneSimulationSettings.ReadInt` (pre-existing).
- `QuestMachine.cs:21` defaults to `Random.Shared.NextDouble`.
- `ZoneGameTime.cs:20` uses the local `DateTimeOffset.Now` offset.
- `PlaceMammons` (`ZoneWorldMammon.cs:61-66`, new in a54f5b43) defaults any unknown id to the blacksmith slot and silently skips a missing template.

  **Fix:** Each should throw or log a concrete error naming the missing setting, template or id.

**P2-20. Ledger hygiene.**
- **Unchecked arithmetic:** `LedgerHoldings.Add` (`LedgerHoldings.cs:22`) and `LedgerMove.Compact` use plain `+`. Make it `checked`.
- **Transaction ids:** they are (tick, sequence), not derived from the command as the doc says (§3.8). Either derive them from the client request or fix the doc.
- **Append-only:** add a database trigger or grant that blocks `UPDATE` and `DELETE` on the journal table (migration `016_economy_ledger.sql`).
- **Boot cost:** boot replays the entire journal through `ReadAll`. Add checkpoints.

  **Acceptance:** Each part has a unit test, and an `UPDATE` against the journal table fails.

**P2-21. Drop random stream choice.** Ground drops pick their stream by position region (`ZoneWorldGround.Members.cs:209`), while spoil picks it by owner region. Unify the two.

**P2-22. Doc drift in `docs/architecture/zone-simulation.md`.**
- Line 3 says steps 9 and later are proposals, but the table shows 9-12 done.
- The §2 diagram still shows `_gate`.
- The claim about actor locks is wrong for the social actors.
- The transaction-id derivation differs from the code.

**P2-23. Weak test assertions.**
- `RegionHandoffWorldTests:95` asserts `>= before + 1`.
- `NetPrivateStoreTests:277-278` asserts `>=`, and package sales are not checked against the ledger.
- `RegionTickSchedulerTests` uses `ParallelRegionExecutor(Environment.ProcessorCount)`, which can be 1 on CI runners.
- `WorldAffinityTests` has only 2 tests.

  **Fix:** Use exact counts, check the ledger postings, use a fixed degree of parallelism of 4, and add affinity tests for the request and command paths.

The known raid-boss combat-stat bug was left out of scope, as you asked. I have not changed any code.
