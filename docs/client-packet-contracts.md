# Client packet contracts (Mobius CT 0 Interlude wire)

Reference for **blocked or not-yet-emitted** Interlude S2C/C2S layouts the retail client expects. Fineage Net opcodes may differ; field order here follows Mobius `ServerPackets` / `writeImpl` (Interlude hex opcodes in parentheses).

**Source of truth for behavior:** `.cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/network/`

---

## Monster Derby (Monster Race Track)

**Server feature:** `MonsterRaceManager`, `DerbyTrackZone`, Race Manager NPC (`30995`), ticket item, lane bets via HTML bypass.  
**Fineage today:** `data/spawns/others_monster_race_track_npcs.json` only — no cycle, bets, or `MonRaceInfo`.

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

## Implementation queue (audit)

| Order | Item | Server PR target | Client packets |
|-------|------|------------------|----------------|
| 15 | Swamp / damage / no-landing zones | zone simulation | none |
| 16 | Boats + walking NPCs | `BoatManager` parity | vehicle table above |
| **16a** | **Monster Derby Track** | `MonsterRaceManager` + ledgers + config | **`MonRaceInfo` `0xDD`** |

See `docs/mobius-gap-audit-2026-11-21.md` Top 20 status table.