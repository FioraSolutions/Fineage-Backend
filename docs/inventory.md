# Inventory mutations and the ItemList contract

Issue [#868](https://github.com/FioraSolutions/Fineage-Backend/issues/868). Client counterpart [FioraSolutions/Fineage#1662](https://github.com/FioraSolutions/Fineage/issues/1662).

Playtest 2026-11-22 (Nikita): shop buy, destroy, equip, and unequip duplicated items, deleted items that were still in the bag, or never showed an item that the server had already applied. The same window exists for every other bag mutation.

## Root cause

There was no single inventory mutation. `PlayerInventory` had several add, take, and equip paths with different stack and paperdoll rules, and dozens of call sites built their own `InventoryUpdate` and sent it before the bag was durable.

1. **Several mutation rules.** `Add` merged a stackable into the first row of that item id and, for a non-stackable count above 1, created every piece but returned only the first, so the client was told about one object. `AddRestored` never merged, so a database bag could hold several stacks of the same stackable while a later `Add` merged into the first only. `TryConsume` touched the first stack. `TryTake` unequipped and then walked every stack. `TryReduce` refused an equipped piece. The warehouse minted its own object ids from 2_000_000 and stored a non-stackable count above 1 as one row. One item id could therefore exist as two rows, and one object id could be minted twice.

2. **The client was told before the commit.** Shop `SendTouches`, destroy, drop, equip, give, quests, warehouse, and the other action handlers called `FanoutInventoryUpdate` and then `SaveInventory`. Under `WithDeferredInventoryPersist`, `SaveInventory` only marked the bag dirty. `FlushDirtyInventories` wrote the ledger journal and the item snapshot in two steps and, when the ledger flush threw, skipped every snapshot write. Memory and the client were already ahead of `character_items`. A relog brought the bought item back as missing and the destroyed item back as present.

3. **Drop removed the bag row before the ground spawn.** `TryDropItem` reduced the stack, notified the client, and only then spawned the ground item and committed the ledger move. A failed spawn returned `bad_request` and left the piece neither in the bag nor on the ground.

4. **Wrong change type.** Give and grant always sent Add, including when the new count merged into a stack the client already had (the client drew a second stack). Equip side effects (two-hand, full armor, formal wear) were a separate paperdoll diff. If that diff was empty the handler returned an error after the mutation and did not save.

5. **Silent loss.** `ReplaceFromStored` and `CharacterInventory.Restore` skipped a row whose template was missing. Warehouse restore did the same class of skip by allowing a non-stackable count above 1 to stay one object.

6. **Mutations that never reached `SaveInventory`.** Arrow and shot consumption posted the ledger and fanned an update without a snapshot write. Admin give had no ledger entry. Sweep loot and the Black Marketeer exchange mutated inside the ledger lambda and published from the lambda, so a failed outer flush could not roll the client back.

7. **Leave wrote the bag before the ledger.** The leave barrier persisted item rows and then flushed the journal, so a crash between them stored items the journal did not yet contain.

## Mechanism

`PlayerInventory` is the only mutator of a bag and its paperdoll (Mobius `ItemContainer` / `PcInventory`). Every add, reduce, consume, equip, unequip, enchant, and augmentation appends one coalesced journal row per object id:

| Change | Value | When |
|---|---|---|
| Add | 0 | The object id was not in the bag. |
| Modify | 1 | Count, enchant, augmentation, or paperdoll slot changed. A partial stack reduce keeps the slot. |
| Remove | 2 | The object left the bag. Count is 0. The slot is the paperdoll slot it left, or -1. |

Coalescing inside one unpublished journal: Add then Modify stays Add with the final fields; Add then Remove drops the row (the client never saw the object); Remove then Add of the same object id (pet collar evolve) becomes Modify.

Stack rule, one per container: a plain stackable (enchant, custom types, and options all zero) is exactly one stack per item id. A stackable with any of those fields set stays its own row and is not a merge target. A non-stackable is one piece with count 1. Equip and unequip are paperdoll moves in this same journal, not a second packet.

Object ids come from the zone `ItemObjectIdAllocator`. The bag and every warehouse, freight hold, and clan warehouse share that allocator. An object id is in exactly one container. Restoring a row observes its id so a later mint cannot reuse it. A duplicate id on restore throws.

`ZoneWorld.SaveInventory` / `FlushDirtyInventories` is the only commit. Order:

1. Validate and apply on the inventory (the caller's mutation, already journaled).
2. In one database transaction, insert the pending ledger rows and replace the dirty bags, warehouses, and freight holds. The transaction commits both or neither.
3. After that commit returns, drain the journal, bump the inventory version by one, and send one `InventoryUpdate`.

A failed commit leaves the bags dirty, puts the ledger rows back in the journal buffer, does not publish, and logs the character id, account, item count, and the exception. Hand-built `FanoutInventoryUpdate` calls are gone. `FanInventory` now persists and publishes the journal for the callers that still pass the old row list.

The leave barrier uses that same transaction for the leaving character and any other dirty economic rows, then discards the leaving journal so the next enter is an `ItemList` rather than a second copy of the same adds. Other characters in that commit are told with `InventoryUpdate` only after the commit returns.

Economic item writes stay on the caller that commits them. A persistence worker may move non-economic snapshots off the simulation thread. It must not move this commit, and it must not send `InventoryUpdate` before the commit returns.

The memory journal stores an entry at `Append`, so a process that never reaches Postgres has no second durable write to roll back. The Postgres journal keeps entries buffered until `CommitEconomy` inserts them on the same connection and the same transaction as `character_items`.

Unknown templates are not skipped. Bag load logs `inventory drop unknown template` with character, item id, object id, and count, and does not restore that row. A missing template on `ReplaceFromStored` or warehouse restore throws, naming the item id and object id. A collapsed duplicate stack is logged with both object ids.

## Wire contract (client)

This matches the client in [FioraSolutions/Fineage#1667](https://github.com/FioraSolutions/Fineage/pull/1667). The client keeps one inventory book keyed by object id. `ItemList` replaces that book. `InventoryUpdate` applies add (0), modify (1), and remove (2) in order, by object id. The bag, paperdoll, shortcut lookup, and adena are projections of that book. Weight stays the server load on `StatusUpdate` / `UserInfo`.

| Packet | Direction | Opcode | Body |
|---|---|---|---|
| `ItemList` | zone → client | `0x030A` | the existing item-list body, then the version tail |
| `InventoryUpdate` | zone → client | `0x0315` | the existing inventory-update body, then the version tail |
| `RequestItemList` | client → zone | `0x000F` | empty |

Each row is unchanged: object id, item id, count, enchant, equipped slot, body slot, kind, icon, weapon type, armor type, then option1 and option2 (8 bytes). Equip and unequip are modify rows (the slot, or -1). A remove row has count 0 and the paperdoll slot the piece left.

### Where the version goes

The version is **not** a field of `EncodeItemList` or `EncodeInventoryUpdate`. Those methods still round-trip the body they have always written. The zone appends **one little-endian int32** after that body (`InventoryVersionTail`).

The client detects it by re-encoding the decoded message with the current codec and comparing lengths:

| Extra bytes after the re-encoded body | Meaning |
|---|---|
| 0 | Unversioned. The cursor is not moved. Today's packets, before this tail, are this case. |
| 4 | The version, little-endian int32. A negative value is unreadable. |
| anything else | Unreadable. The book is not changed. |

A decoder that stops at the end of the body still reads the rows. A decoder that requires the tail to be inside the codec round-trip will not see it: `Encode(Decode(payload))` equals the body and leaves the four bytes as a tail. That is deliberate, so the client's length check keeps working after it picks up this contract.

### How the version increments

The cursor is per character and is not stored. A new process starts it at 0, which means "no ItemList sent yet".

- The enter `ItemList` is version **1**. The client stores 1 and replaces the book. Version 0 is not sent: the client treats 0 as "no version" and would not start tracking.
- Each committed `InventoryUpdate` adds one and sends that value. After the enter list, the first update is **2**, then 3, and so on. The client applies the rows only when the value is exactly `cursor + 1`, then stores it.
- An update whose version is less than or equal to the cursor is a replay. The client does not apply those rows and does not ask for a resync.
- An update whose version is greater than `cursor + 1` is a gap. The client does not apply those rows.
- An update with no tail (version 0) after a versioned `ItemList` is rejected and does not change the book.
- A later `ItemList` (enter again, or a resync) carries the **current** cursor, replaces the book, and sets the cursor to that value. It does not increment. The next update is that value plus one.
- A refused action sends no `InventoryUpdate` and does not increment. A drop refused because it is too far sends one modify of the unchanged row so the client redraws the slot; that modify does increment, because it is a published update.

### Resync

On a gap, an unknown change code, or an unreadable tail, the client sends `RequestItemList`.

| | This server |
|---|---|
| Opcode | `0x000F` |
| Body | empty. The handler does not read it. |
| Reply | `ItemList` (`0x030A`) at the current cursor. The cursor is not incremented. |

`0x000F` is the Interlude C2S opcode. Mobius CT_0 Interlude `ClientPackets` registers it as:

```java
REQUEST_ITEM_LIST(0x0F, RequestItemList::new, ConnectionState.IN_GAME),
```

That line is in `L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/network/ClientPackets.java`. Checked against the public tree at commit `dd4850671e3f21ba05a98b4d29b62558a74a096a` (https://github.com/tichopad/L2J_Mobius). On that same table `USE_ITEM` is `0x14`. `RequestItemList.readImpl` is empty. Later chronicles moved this request to `0x14` and used `0x0F` for movement; that table is not Interlude. The invented `0x03E1` opcode is not accepted.

