# Fineage Net (server)

PacketEnvelope login/zone path. Durable accounts use PostgreSQL by default for local Net.

## Ports

| Stack | Login | Zone |
|-------|-------|------|
| Net (`PacketEnvelope`) | 9106 | 9777 |

## Persistence

PostgreSQL is the only account store. There is no file-backed mode: `--store`, `FINEAGE_LOGIN_STORE`,
and `FINEAGE_STORE` are gone, and a JSON file is never read or written at runtime.

| Env | Meaning |
|-----|---------|
| `FINEAGE_DATABASE_URL` | Npgsql connection string (default `Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage`) |
| `FINEAGE_STATE_DIR` | Directory for world sidecars: `seven-signs.json`, `olympiad.json`, `boss-schedule.json` (default `data`) |

Login and Zone both fail fast at boot with a readable message when PostgreSQL is unreachable.

### Postgres layout (Mobius-shaped)

| Table | Role |
|-------|------|
| `store_snapshot` | Backup / import-export document only (full JSON document) — **not** runtime SoT for characters |
| `accounts` | Credentials |
| `characters` | Identity, appearance, `x/y/z`, vitals, level/exp/sp, clan flags |
| `character_items` | Durable `object_id`, `item_id`, `count`, `enchant_level`, `loc` (`inventory` / `paperdoll` / `warehouse`), `loc_data` (paperdoll slot) |
| `character_quests` + `character_quest_vars` | Quest state |
| `clans` | Still payload jsonb until a later epic |
| `store_meta` / sequences | `next_character_id`, `next_clan_id`, `next_item_object_id` |

`PostgresAccountStore.FindById` / `List` read **normalized tables** (then hydrate the in-process mirror for hot writes). Login and Zone are separate processes, and both read the same database.

Hot paths (`TrySavePresence`, vitals, inventory replace) update normalized rows without truncating the whole store. Skills are still derived at zone enter (AutoSkills), not persisted.

Repair:

```bash
docker compose up -d postgres
dotnet run --project src/Fineage.Login -- backfill-normalized
```

## Lobby roster (CharSelect)

`CharacterRoster` / `CreateResult` carry a **Mobius CharSelectionInfo-shaped** summary (same fields as V2 `CharacterSummaryMsg` keys used by the lobby UI):

- identity / appearance / level
- position `x,y,z` (saved or class spawn preview)
- vitals `hp/maxHp`, `mp/maxMp`, `curCp/maxCp` (CP from class template)
- `exp`, `sp`, `karma` (karma 0 until persisted)
- `weaponItemId` + `paperdoll[17]` item ids (not the full bag)
- `deleteSecondsLeft`, `isLastPlayed`

Skills are still derived at zone enter, not listed on CharSelect.

## Character delete / restore (lobby Net)

`DeleteCharacter` `0x0209` and `RestoreCharacter` `0x020B` take the same body — `slot` (`-1` when deleting by id) and `characterId` (`0` when deleting by slot), matching the V2 messages. `LoginService` resolves whichever identifier is supplied.

| Opcode | Dir | Payload |
|--------|-----|---------|
| `DeleteCharacter` `0x0209` | C→S | `slot` int32 (-1 = by id), `characterId` int32 (0 = by slot) |
| `RestoreCharacter` `0x020B` | C→S | same body |
| `DeleteResult` `0x020A` | S→C | `ok` byte, `code`, `message`, `slot`, `characterId` |

`FINEAGE_DELETE_CHAR_AFTER_DAYS` (default `7`) decides hard vs soft delete: `0` removes the record, `> 0` sets a delete timer and the roster keeps the row flagged `pendingDelete` with `deleteSecondsLeft`. Restore clears that timer. On success the server sends `DeleteResult` then a fresh `CharacterRoster`, so the client redraws from server truth instead of patching its list locally.

The char-select client currently treats delete/restore as a no-op on Net (`CharSelectWindow.DeletePressed`, `TODO(#131)`); it can now send these opcodes.

## EnterWorld (zone Net)

After handoff auth, `EnterWorld` runs **one** world join: `ZoneWorld.TryEnterAsync` (same Mobius path as V2 — inventory restore, vitals, AutoSkills, AOI). Net then **projects the live `ZoneEntity`** (no second store read with Human defaults):

| Order | Opcode | Payload |
|-------|--------|---------|
| 1 | `EntitySpawn` `0x0304` | self player: name, classId, **race/sex**, xyz, speeds |
| 2 | `PlayerStats` `0x0305` | level/exp/sp, hp/mp/cp |
| 3 | `ItemList` `0x030A` | bag + paperdoll rows (`objectId`, `itemId`, `count`, `enchant`, `equippedSlot`) |
| 4 | `SkillList` `0x030B` | AutoSkills (`skillId`, `level`, `reuseRemainingMs`) |
| 5 | `ShortCutInit` `0x035A` | saved hotbar: `count` + rows (`type`, `slot`, `id`, `level`) |

(`UserInfo` `0x0357` goes between `PlayerStats` and `ItemList`.)

Client: stash `PlayerInfo` from spawn → load tile → spawn; apply ItemList via `ZoneV2GearSession`, SkillList via existing skill apply services.

## Presence fanout (Net multiplayer)

V2 MessagePack flushes still go to a discarded stream on the Net path. Net peers get PacketEnvelope fanout via `NetPresenceHub`:

| Event | Opcodes |
|-------|---------|
| Enter | Self bootstrap (above). Then `EntitySpawn` for each known **player and NPC** (`IsSelf=false`; NPC `Kind=Npc`, `TemplateId=npcId`). Entrant CharInfo-shaped spawn to Net player peers. |
| Move | `MoveState` to self + stayed known Net **players**. Newly visible player/NPC → `EntitySpawn`. Forgotten → `EntityDespawn` `0x030C`. |
| NPC chase (sim) | `MoveState` to Net watchers that already know the NPC (`INetPresenceBridge`). |
| Select / Attack | `SelectTarget` `0x030D` → `TargetSelected` `0x030F`. `AttackRequest` `0x030E` → world combat; `AttackState` `0x0310` + `StatusUpdate` `0x0311` via NetBridge (incl. mob swings / hit timer). Approach uses the same MoveState bridge as player move. |
| Die / drop / despawn | `EntityDie` `0x0312` (death anim). `DropItem` `0x0313` (loot + known ground items). Corpse decay / pickup → `EntityDespawn` `0x030C`. |
| Pickup | `PickupRequest` `0x0314` → `InventoryUpdate` `0x0315` (add/modify/remove rows) + `EntityDespawn` of ground object. |
| Bag drop / gear | `DropItemRequest` `0x0316` → bag `InventoryUpdate` + world `DropItem`. `UseItem` `0x0317` / `UnequipItem` `0x0318` → `InventoryUpdate` (paperdoll modify). |
| Chat | `Say` `0x0319` → `CreatureSay` `0x031A` (ALL local / SHOUT zone). Enter + flood notices → `SystemMessage` `0x031B`. |
| Skills | `UseSkill` `0x031C` → world cast; `MagicSkillUse` `0x031D` + `StatusUpdate` (MP / heal HP). Hit damage uses existing vitals fanout. |
| Revive | `ReviveRequest` `0x031E` (village) → `Revive` `0x031F` + `TeleportToLocation` `0x0320` + vitals; AOI spawn/despawn after relocate. |
| Kill reward | Killer gets `SystemMessage` (id 95), `StatusUpdate`, and `PlayerStats` `0x0305` (exp/sp/level). |
| Buffs | Skill / playtest abnormals → `AbnormalStatusUpdate` `0x0321` (full effect list). |
| NPC dialog | `NpcTalk` `0x0322` / `BypassRequest` `0x0323` → `NpcHtml` `0x0324` (quest choice HTML reused). EntitySpawn carries `Attackable` for talk vs attack. |
| Shop | Bypass opens `BuyList` `0x0325` / `SellList` `0x0326` / `MultiSellList` `0x0327` + `ShopBalance` `0x0328`. Buy/sell/multisell → `RequestBuyItem` `0x0329` / `RequestSellItem` `0x032A` / `RequestMultiSellChoose` `0x032B` (+ inventory / system lines). |
| Warehouse | Bypass opens `WarehouseDepositList` `0x032C` / `WarehouseWithdrawList` `0x032D`. Confirm → `WarehouseDepositRequest` `0x032E` / `WarehouseWithdrawRequest` `0x032F`. |
| Quests | Journal → `RequestQuestList` `0x0330` / abort → `RequestQuestAbort` `0x0331` → `QuestList` `0x0332`. Quest HTML still via `NpcHtml`. |
| Party | Invite/answer/leave/kick → `PartyInviteRequest` `0x0333` / `PartyAnswerJoinRequest` `0x0334` / `PartyLeaveRequest` `0x0335` / `PartyKickRequest` `0x0336`. Fanout `PartyAskJoin` `0x0337` / `PartyJoinResult` `0x0338` / `PartyMemberList` `0x0339`. |
| Trade | Request/answer/add/done → `TradeRequest` `0x033A` / `TradeAnswerRequest` `0x033B` / `TradeAddItemRequest` `0x033C` / `TradeDoneRequest` `0x033D`. Fanout `TradeAsk` `0x033E` / `TradeRequestResult` `0x033F` / `TradeStart` `0x0340` / `TradeOfferUpdate` `0x0341` / `TradeConfirmState` `0x0342` / `TradeFinished` `0x0343` (+ inventory on exchange). |
| Private store | Sell: `SetPrivateStoreSell` `0x038D` / `PrivateStoreBuy` `0x0390` / `PrivateStoreListSell` `0x0392`. Buy: `SetPrivateStoreBuy` `0x0393` / `PrivateStoreSellTo` `0x0394` / `PrivateStoreListBuy` `0x0395`. Shared: `PrivateStoreQuit` `0x038E` / `PrivateStoreListRequest` `0x038F` / `PrivateStoreInfo` `0x0391`. See `docs/private-store-v2-protocol.md`. |
| Craft | `RecipeBookOpen` `0x0396` / `RecipeMakeSelf` `0x0397`. Fanout `RecipeBookList` `0x0398` / `RecipeItemMakeInfo` `0x0399`. See `docs/craft-v2-protocol.md`. |
| Manufacture | `SetRecipeShop` `0x039A` / `RecipeShopMakeInfo` `0x039B` / `RecipeShopMakeItem` `0x039C`. Fanout `RecipeShopSellList` `0x039D` / `RecipeShopItemInfo` `0x039E`. See `docs/craft-manufacture-v2-protocol.md`. |
| Friends / block | `FriendInvite` `0x039F` … `BlockRequest` `0x03A3`. Fanout `FriendInviteAsk` `0x03A4` / `FriendList` `0x03A5` / `FriendUpdate` `0x03A6` / `BlockList` `0x03A7`. See `docs/friends-v2-protocol.md`. |
| Hotbar (shortcuts) | See **Shortcuts** below. `RequestShortCutReg` `0x0358` → `ShortCutRegister` `0x035B`; `RequestShortCutDel` `0x0359` → `ShortCutDelete` `0x035C`. |
| Leave / disconnect | `EntityDespawn` of leaver to known Net peers. |

## Shortcuts (hotbar, Mobius ShortCuts)

Row (`ShortCutNetMsg`, 4 × int32 LE): `type`, `slot`, `id`, `level`.

- `slot` = client world slot = `slot + page * 12` (slot 0..11, page 0..9 → 0..119).
- `type`: 1 item (`id` = item **object id**, level 0), 2 skill (`id` = skill id, `level` = known level), 3 action, 4 macro, 5 recipe.

| Opcode | Dir | Payload | Notes |
|--------|-----|---------|-------|
| `RequestShortCutReg` `0x0358` | C→S | row | Overwrites the slot. Item must be in the bag; skill must be known (level is replaced by the known level). |
| `RequestShortCutDel` `0x0359` | C→S | `slot` int32 | Clears the slot. |
| `ShortCutInit` `0x035A` | S→C | `count` int32 + rows | Enter world, after `SkillList`. |
| `ShortCutRegister` `0x035B` | S→C | row | Server truth for one slot. Reply to an accepted reg, **and** to a rejected reg when the slot already held a saved row (client rolls back). |
| `ShortCutDelete` `0x035C` | S→C | `slot` int32 | Slot empty on the server. Reply to del, or to a rejected reg on an empty slot. |

Persistence: `ZoneEntity.Shortcuts` → `IAccountStore.TryReplaceShortcuts` on every reg/del (Mobius writes `character_shortcuts` immediately). Postgres table `character_shortcuts (character_id, slot, page, type, shortcut_id, level)` from migration `003_character_shortcuts.sql`; JSON snapshot key `shortcuts`. On enter, item rows whose object is no longer in the bag are dropped and skill rows take the current known level (Mobius `ShortCuts.restoreMe`).

Client: drag applies locally at once (optimistic), then the server echo (`ShortCutRegister` / `ShortCutDelete`) is applied as truth. `ShortCutInit` is held until the skillbar and the ItemList are applied.

AOI (who is on the knownlist):

| Env | Effect |
|-----|--------|
| `FINEAGE_ZONE_AOI_CELL` (default **3000**) | `GridAoi` with that cell size |
| `FINEAGE_ZONE_AOI_CELL=0` and `FINEAGE_ZONE_INTEREST_RANGE` &gt; 0 | `RangeAoi` |
| `FINEAGE_ZONE_AOI_CELL=0` and no interest range | `SingleCellAoi` (tiny test hosts only) |

How much of the datapack is spawned:

| Env | Effect |
|-----|--------|
| *(default)* | every `enabled` map under `data/spawns/` — the whole Interlude world (~34k NPCs) |
| `FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0` | single active map only (`ti_cedric` unless overridden) — tests / emergency |
| `FINEAGE_ZONE_MAPS=a,b` | whitelist: only those spawn maps |
| `FINEAGE_ZONE_BOUNDS=x1,y1,x2,y2[;...]` | keep only rows inside those XY boxes |

Hosted Zone defaults to the full enabled datapack with `GridAoi(3000)`, so a player only receives
NPCs around them, not all 34k. Do not pair the whole world with `SingleCellAoi`.

## Scale seams

- `IAccountStore` — Postgres (normalized character domain) now, JSON for tests
- `IHandoffStore` / `MemoryHandoffStore` — HMAC handoff; Redis later
- `IAoi` — SingleCell / Range / Grid (`FINEAGE_ZONE_AOI_CELL`)

## Local

```bash
./scripts/run-local-net.sh start
./scripts/run-local-net.sh status
```
