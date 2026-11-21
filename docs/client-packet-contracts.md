# Client packet contracts (Mobius CT 0 Interlude wire)

Reference for **blocked or not-yet-emitted** Interlude S2C/C2S layouts the retail client expects. Fineage Net opcodes may differ; field order here follows Mobius `ServerPackets` / `writeImpl` (Interlude hex opcodes in parentheses).

**Source of truth for behavior:** `.cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/network/`

---

## Monster Derby (Monster Race Track)

**Server feature:** `MonsterRaceManager`, `DerbyTrackZone`, Race Manager NPC (`30995`), ticket item, lane bets via HTML bypass.  
**Fineage today:** `MonsterRaceBoard` / `MonsterRaceActor`, Race Manager HTML (30995), ticket item 4443, `MonRaceInfo` Net opcode `0x03CA` (Interlude body `0xDD`). Config: `data/monster-race.json`.

### `MonRaceInfo` — S2C `0xDD`

| Field | Type | Notes |
|-------|------|--------|
| `unknown1` | int32 | Phase: `-1` + `unknown2=0` init; `0` + `15322` race start; `13765` + `-1` mid-race; `-1` + `0` end |
| `unknown2` | int32 | Paired with `unknown1` (see Mobius `MonsterRaceManager.CODES`) |
| `runnerCount` | int32 | Always `8` |
| ×8 runners | | |
| → `npcObjectId` | int32 | Runtime object id of spawned runner |
| → `npcId` | int32 | `template.displayId + 1_000_000` |
| → `originX`, `originY`, `originZ` | int32×3 | Track lane start (Mobius hard-coded around 14107, 181875±lane) |
| → `endX`, `endY`, `endZ` | int32×3 | Lane finish |
| → `collisionHeight`, `collisionRadius` | float64×2 | From template |
| → `unknown` | int32 | `120` in Mobius |
| → `speeds[20]` | byte×20 | Per-tick speed segments when `unknown1 == 0`; else `0` |
| → `padding` | int32 | `0` |

**Broadcast:** to players in derby broadcast regions during `ACCEPTING_BETS` / `STARTING_RACE` / `RACE_END`; often paired with `PlaySound` (`S_Race`, `ItemSound2.race_start`).

### Related (not fully specified here)

| Packet | Opcode | Role |
|--------|--------|------|
| `DeleteObject` | (standard) | Despawn runner NPCs after race |

**C2S / HTML:** bet placement via Race Manager dialog (`scripts` / bypass — lane index + adena); payout and history are server-side + system messages.

**Economy:** ticket purchase and payouts must use Fineage auditable ledgers (`ConsumeLedger` / `GrantLedger`), not silent adena mutation.

---

## Boats (ship routes)

**Server feature:** `BoatManager`, `Boat` actor, `BoatAI`, routes from datapack.  
**Fineage today:** not implemented.

### `VehicleInfo` — S2C `0x59`

| Field | Type | Notes |
|-------|------|--------|
| `boatObjectId` | int32 | |
| `x`, `y`, `z` | int32×3 | Current position |
| `heading` | int32 | |

Sent on boat spawn / enter range; also on stop (speed 0) in Mobius `BoatAI`.

### `VehicleDeparture` — S2C `0x5A`

| Field | Type | Notes |
|-------|------|--------|
| `boatObjectId` | int32 | |
| `moveSpeed` | int32 | |
| `rotationSpeed` | int32 | |
| `destX`, `destY`, `destZ` | int32×3 | |

### `VehicleCheckLocation` — S2C `0x5B`

Boat position correction (read Mobius `VehicleCheckLocation.java` when implementing).

### `GetOnVehicle` — S2C `0x5C`

Player seated on boat (object ids + seat offsets).

### `GetOffVehicle` — S2C `0x5D`

Player left boat at disembark point.

### `MoveToLocationInVehicle` — S2C `0x71`

| Field | Type | Notes |
|-------|------|--------|
| `playerObjectId` | int32 | |
| `boatObjectId` | int32 | |
| `destX`, `destY`, `destZ` | int32×3 | |
| `originX`, `originY`, `originZ` | int32×3 | |

### `StopMoveInVehicle` — S2C `0x72`

Player stop inside boat.

### `ValidateLocationInVehicle` — S2C `0x73`

Position validate while on boat.

### `VehicleStarted` — S2C `0xC0`

Boat run state (depart/arrive).

### C2S (client → server)

| Opcode | Name | Role |
|--------|------|------|
| `0x42` | `RequestGetOnVehicle` | Board at dock |
| `0x43` | `RequestGetOffVehicle` | Leave at dock |
| `0x5C` | `RequestMoveToLocationInVehicle` | Move intent on deck |
| `0x5D` | `CannotMoveAnymoreInVehicle` | Arrival at deck waypoint |

---

## Freight warehouse (account packages)

**Server feature:** Mobius `PlayerFreight`, `RequestPackageSend` / `RequestPackageSendableItemList`, bypass `package_deposit` / `package_withdraw`.  
**Fineage Net opcodes:** `PackageToList` `0x03C6`, `PackageSendableList` `0x03C7`, `PackageSendRequest` `0x03C8`, `PackageSendableListRequest` `0x03C9` (Mobius Interlude wire: S2C `0xC2` / `0xC3`, C2S `0x9F` / `0x9E`).

### `PackageToList` — S2C Mobius `0xC2` / Fineage `0x03C6`

| Field | Type | Notes |
|-------|------|--------|
| `count` | int32 | Account characters excluding the sender |
| ×`count` | | |
| → `objectId` | int32 | Recipient character object id |
| → `name` | string | Character name |

### `PackageSendableList` — S2C Mobius `0xC3` / Fineage `0x03C7`

| Field | Type | Notes |
|-------|------|--------|
| `recipientObjectId` | int32 | Selected alt on the account |
| `adena` | int32 | Sender's current adena |
| `itemCount` | int32 | Sendable bag rows |
| ×`itemCount` | | Mobius uses `AbstractItemPacket` layout; Fineage Net uses `ItemListEntryNet` (same as warehouse deposit list) |

### `RequestPackageSend` — C2S Mobius `0x9F` / Fineage `PackageSendRequest` `0x03C8`

| Field | Type | Notes |
|-------|------|--------|
| `recipientObjectId` | int32 | Must belong to sender's account (not self) |
| `count` | int32 | Lines to ship |
| ×`count` | | |
| → `objectId` | int32 | Bag object id |
| → `count` | int32 | Stack count |

Fee: `count × ALT_FREIGHT_PRICE` (default **1000** adena per line) to `SystemAccount.Fees`. Capacity: **200** freight slots (`MaximumFreightSlots`).

### `RequestPackageSendableItemList` — C2S Mobius `0x9E` / Fineage `0x03C9`

| Field | Type | Notes |
|-------|------|--------|
| `recipientObjectId` | int32 | Selected recipient before the send UI |

Withdraw uses existing `WarehouseWithdrawList` / `WarehouseWithdrawRequest` with `warehouseType = 4` (`FREIGHT`).

---

## Item augmentation (variation / life stones)

**Server feature:** `AugmentationData`, `RequestRefine` / cancel / gem confirm.  
**Fineage today:** `AugmentationRules` + `FINEAGE_ALLOW_AUGMENTATION` (default **off**); no Net or persistence.

### C2S (`0xD0` extended — Mobius `ExClientPackets`)

| Sub | Name | Payload (ints) |
|-----|------|----------------|
| `0x2A` | `RequestConfirmRefinerItem` | targetObjId, refinerObjId |
| `0x2B` | `RequestConfirmGemStone` | targetObjId, refinerObjId, gemstoneObjId, gemCount |
| `0x2C` | `RequestRefine` | same four as gem confirm |
| `0x2E` | `RequestRefineCancel` | targetObjId |

### S2C (`0xFE` extended)

| Sub | Name | Fields |
|-----|------|--------|
| `0x50` | `ExShowVariationMakeWindow` | (empty) |
| `0x51` | `ExShowVariationCancelWindow` | (empty) |
| `0x52` | `ExPutItemResultForVariationMake` | itemObjId, itemId, `1` |
| `0x53` | `ExPutIntensiveResultForVariationMake` | refinerObjId, itemId, refinerId, `1` |
| `0x54` | `ExPutCommissionResultForVariationMake` | gemstoneObjId, refinerItemId, gemstoneCount, `1` |
| `0x55` | `ExVariationResult` | option12, option34, `unk` (1 success / 0 fail) |
| `0x56` | `ExPutItemResultForVariationCancel` | targetObjId, itemId, `0`/`7` |
| `0x57` | `ExVariationCancelResult` | refundAdena |

Inventory updates must carry augmentation options on weapon/armor (`option1` / `option2` in Mobius item instance).

---

## Cursed weapons (Zariche / Akamanah)

**Server feature:** `CursedWeaponsManager`, `data/CursedWeapons.xml`.  
**Fineage today:** PvP level-20 rules + Olympiad inventory gate; `CursedWeaponsState` + `FINEAGE_ALLOW_CURSED_WEAPONS` (default **off**).

### `ExCursedWeaponList` — S2C `0xFE` `0x45`

| Field | Type | Notes |
|-------|------|--------|
| `count` | int32 | Active blade count |
| ×`count` | int32 | Cursed **item id** (8190 Zariche, 8689 Akamanah) |

### `ExCursedWeaponLocation` — S2C `0xFE` `0x46`

| Field | Type | Notes |
|-------|------|--------|
| `count` | int32 | Rows (Mobius writes `0` twice when empty) |
| ×row | | `itemId`, `activated` (0/1), `x`, `y`, `z` |

Related: `SystemMessage` broadcast on spawn, drop on death, and disappearance timers.

---

## Skill enchant (+)

**Server feature:** `RequestExEnchantSkill`, `EnchantSkillTreeData`.  
**Fineage today:** learned level in `KnownSkills` / character_skills (`AccountModels`); `SkillEnchantRules` + `FINEAGE_ALLOW_SKILL_ENCHANT` (default **off**).

### C2S (`0xD0` extended)

| Sub | Name | Payload |
|-----|------|---------|
| `0x06` | `RequestExEnchantSkillInfo` | skillId |
| `0x07` | `RequestExEnchantSkill` | skillId, skillLevel |

### S2C (`0xFE` extended)

| Sub | Name | Fields |
|-----|------|--------|
| `0x17` | `ExEnchantSkillList` | count × (`skillId`, `nextLevel`, `sp`, `exp` int64) |
| `0x18` | `ExEnchantSkillInfo` | skillId, level, spCost, xpCost int64, rate, reqCount × (type, id, count, unk) |

On success Mobius updates skill level in DB and rebroadcasts `SkillList` / shortcuts.

---

## Live doors (castle + clan hall)

**Server:** `DoorStatusNetMsg` on Net opcode `DoorStatus` (`0x03B7`). Agit gates use the same packet as castle gates; `hallId` is set when `castleId` is 0.

| Field | Type | Notes |
|-------|------|--------|
| `objectId` | int32 | Runtime door entity (NpcType `Door`, npcId 0) |
| `doorId` | int32 | Mobius `Doors.xml` id (client static mesh) |
| `open` | bool | Live geo + mesh state |
| `curHp`, `maxHp` | int32×2 | Siege damage only when `attackable` |
| `attackable` | bool | Castle siege (non-wall) or clan-hall siege |
| `wall` | bool | |
| `castleId` | int32 | Castle gates; 0 for agit |
| `hallId` | int32 | Clan hall gates; 0 for castle |
| `upgradeRatio` | int32 | Chamberlain ratio (castle only today) |

**Unity:** Bind `doorId` to the static door prefab; track `objectId` for target/siege HP. Subscribe to `DoorStatus` after `EntitySpawn` for door NPCs.

---

## Clan hall escape and death restart

**Data:** `data/clan-hall-spawns.json` — Mobius `zones/clan_hall.xml` owner_restart (+ optional chaotic). Consumables **1829** / **5858** (`escapeClanHall`, skills **2040** / **2177**).

| Direction | Opcode / message | Field additions |
|-----------|------------------|-----------------|
| C→S | `ReviveRequest` `0x031E` | `action = 5` (`DeathProtocol.ReviveToClanHall`) — restart inside owned clan hall |
| S→C | `EntityDie` | Optional tail `restartFlags` **bit2** (`4`) = clan hall button when `clan.hideoutId > 0` |
| S→C | `TeleportToLocation` | Same as village restart after `Revive` when action 5 succeeds |

Errors: `revive_no_clanhall`, `escape_no_clan_hall` (scroll use without hideout).

---

## Implementation queue (audit)

| Order | Item | Server PR target | Client packets |
|-------|------|------------------|----------------|
| 15 | Swamp / damage / no-landing zones | zone simulation | none |
| 16 | Boats + walking NPCs | `BoatManager` parity | vehicle table above |
| **16a** | **Monster Derby Track** | `MonsterRaceManager` + ledgers + config | **`MonRaceInfo` `0xDD`** |

See `docs/mobius-gap-audit-2026-11-21.md` Top 20 status table.