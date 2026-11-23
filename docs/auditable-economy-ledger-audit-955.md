# Auditable economy ledger — data consistency audit (#955)

Branch `dev`, snapshot 2026-11-23. Mobius CT0 Interlude is the rules reference. Issue [#955](https://github.com/FioraSolutions/Fineage-Backend/issues/955).

This is **PR 1 of N**: mutation-path map, bugs found at audit time, and shared **check-before-mutate** fixes on the auditable economy ledger. Follow-up PRs: concurrency fuzz per subsystem, live invariant hooks, load-test hammer scenario.

---

## Executive summary

Fineage routes most adena/item movement through `EconomyLedger` + `LedgerMove` (`src/Fineage.Game/Economy/`). Zone handlers project inventories, warehouses, freight, ground stacks, treasuries, and pet bags from journal balances (`ZoneWorld*Ledger.cs`).

**Shared mechanism (this PR):** any handler that builds `LedgerMove` postings must call `CheckCommit()` before changing bags, ground entities, or stores — same pipeline as bag drop (`ZoneWorldGround.Members.cs`).

**New shared tool:** `EconomyInvariantChecker` (`src/Fineage.Game/Economy/EconomyInvariantChecker.cs`) — ledger-only invariants with concrete entity ids; no silent repair.

---

## Mutation map (economy scope)

| Subsystem | Primary API | Ledger files | Persist barrier |
|-----------|-------------|--------------|-----------------|
| Adena / stackable grants | `RecordBagChange`, `TryGrantItem` | `ZoneWorldInventory.cs`, `ZoneWorldGrantLedger.cs` | `TryCommitDirtyEconomy` |
| Shop buy/sell/multisell | `BeginShopMove` | `ZoneWorldLedger.cs` | same |
| Player trade | `LedgerTxKind.TradeSettle` | `ZoneWorldTrade.More.cs` | same |
| Private store | `BeginStoreSale` | `ZoneWorldPrivateStore.*` | same |
| Ground drop/pickup/destroy | `BeginLedgerMove` + `PostOntoGround*` | `ZoneWorldGround*.cs` | same |
| Player death drop | `LedgerTxKind.DeathDrop` | `ZoneWorldDeathDrop.cs` | same |
| Warehouse / clan warehouse | `WarehouseDeposit` / `Withdraw` | `ZoneWorldNpcServices.*` | same + warehouse dirty |
| Freight | `LedgerTxKind.Freight*` | `ZoneWorldFreight.cs` | offline freight queue |
| Mob loot to ground | `LedgerTxKind.Loot` | `ZoneWorldGround.Members.cs` | same |
| Mob death / spoil / sweep | `DeathDrop`, `SweepLoot`, `Gathering` | `ZoneWorldDeathDrop.cs`, `ZoneWorldSpoil.Members.cs`, `ZoneWorldGatheringLedger.cs` | same |
| Craft / manufacture | `Craft` | `ZoneWorldCraft.cs`, `ZoneWorldConsumeLedger.cs` | same |
| Quest / scheduled payouts | `QuestReward`, lottery, SS | `ZoneWorldQuests.*`, `ZoneWorldSevenSigns.cs`, … | same |
| Castle / manor treasury | `Treasury*`, `Manor*` | `ZoneWorldCastleLedger.cs`, `ZoneWorldManor.cs` | treasury in siege board |
| Consume (potions, shots) | `RecordConsumeOne` | `ZoneWorldConsumeLedger.cs` | same |
| Crystallize / enchant / augment | `RecordPlayerBagChange` | `ZoneWorldCrystallize.cs`, `ZoneWorldEnchant.cs`, … | observe-after-mutate* |
| Pet bag moves | `CommitPetOwnerTransfer` | `ZoneWorldGatheringLedger.cs`, `ZoneWorldPets.cs` | same |
| Character movement | `SavePresence` | *(no ledger)* | position throttle map |
| NPC spawn/presence | `ZoneWorldNpcMaterialize` | *(no ledger)* | see zone-consistency review |

\* `RecordPlayerBagChange` / `RecordBagChange` run the bag lambda before `Commit`; they rely on `LedgerMove.Observe` alignment adjustments. New code should prefer explicit `CheckCommit` then apply (P0-4 pattern in `LedgerCheckBeforeMutateTests`).

**Persistence:** `FlushDirtyInventories` → `TryCommitDirtyEconomy` (`ZoneWorld.Members.Persist.cs`) commits journal + bag rows in one `IAccountStore.CommitEconomy` call; failure restores dirty maps (regression: `ZoneConsistencyRegressionTests.Ledger_flush_failure_blocks_deferred_inventory_replace`).

---

## Bugs found (audit) — root cause → shared fix

| ID | Location | Root cause | Fix in this PR |
|----|----------|------------|----------------|
| **E1** | `ZoneWorldDeathDrop.cs:68-102` | `DeathDrop` removed bag rows and spawned ground **before** `CheckCommit` / `Commit` | Plan postings → `CheckCommit` → spawn → remove → `Commit` (matches bag drop pipeline) |
| **E2** | `ZoneWorldWedding.cs:200-206` | Divorce adena transfer mutated both bags before `CheckCommit` | Postings first, `CheckCommit`, then `Take`/`Add`, then `Commit` |
| **E3** | `ZoneWorldMounts.Wyvern.cs:60-66` | Wyvern fee `TryReduce` before `CheckCommit`; rejection left stack depleted | Postings + `CheckCommit` before `TryReduce` |
| **E4** | `ZoneWorldPrivateStore.Members.cs:74-87` | Buy-list purchase applied adena/items before ledger validation (sell path in `.More.cs` already checked) | Mirror buyer path: `CheckCommit` before transfers |
| **E5** | `ZoneWorldFestival.Drops.cs:17` | Festival blood offering `Inventory.Add` with **no** ledger posting | **Open** — document; needs `LedgerTxKind` + Mobius parity pass |
| **E6** | `ZoneWorldConsumables.cs:158-161` | Potion `restorationRandom` grants items after consume without extra ledger rows | **Open** — likely bundled in consume transaction; verify |
| **E7** | Cross-cutting | `RecordPlayerBagChange` mutates before commit | **Open** — migrate hot paths to check-before-mutate |

Cross-player / AOI issues from the 2026-11-22 review (`docs/zone-consistency-review-2026-11-22.md`) remain tracked there; they are visibility/presence, not ledger conservation.

---

## Invariants (ledger-only, implemented)

`EconomyInvariantChecker.Check(ledger)`:

- Non-system accounts have no negative stack counts.
- Every piece in holdings has `OwnerOf(instance)` pointing at that account.
- Every `OwnerOf` entry has a matching instance in holdings.

World-level checks (every ground account has live `GroundItem`, every NPC maps to spawn data, movement authority) — **PR 3** with load-test oracle extension.

---

## Verification

```bash
dotnet test --filter "FullyQualifiedName~EconomyInvariant|LedgerCheckBeforeMutate|ZoneDeathDrop|NetPrivateStore|ZoneConsistencyRegression"
```

Tests use wall-clock timeouts via existing kit patterns (#910): integration tests cancel after 20–30s where they touch net I/O.

---

## Document history

| Date | Note |
|------|------|
| 2026-11-23 | Initial audit for #955, PR1 check-before-mutate + `EconomyInvariantChecker`. |