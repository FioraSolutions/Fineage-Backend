# Zone V2 NPC services

Gatekeeper teleports, Newbie Helper supplemental magic, and the private warehouse. Talk and bypass still use `docs/npc-html-v2-protocol.md` (`BypassRequest` 95, `NpcHtml` 1094). This page is the service result that those bypasses apply.

Opcodes already taken include NPC HTML **95** / **1094**. Shops are reserved at **96–99** / **1095–1098** and system messages **180–189**. Olympiad is reserved at **60–64** / **1070–1073** and system messages **160–179**. This block is **105–109**, **1105–1109**, and system messages **190–209**.

MessagePack keys are a fixarray (`0..n-1`). Do not renumber these.

## Mobius sources

Rules and numbers were transcribed, not copied, from L2J Mobius CT 0 Interlude:

- `dist/game/data/teleporters/**/*.xml` and `TeleporterData` / `TeleportHolder` (lists, 0-based location id, fee, castle id)
- `PlayerConfig.MAX_FREE_TELEPORT_LEVEL` default **40** (`Player.ini` `MaxFreeTeleportLevel`)
- `TeleportHolder.calculateFee`: normal lists are free at level ≤ 40 (unless a subclass is active). Monday and Tuesday, hour ≥ 20 on the zone clock, charge `fee / 2` (integer). `OTHER` lists always use the listed fee. Noblesse lists are not executed.
- `handlers/bypass/npc/SupportMagic.java` and `CategoryData.xml` `BEGINNER_MAGE`
- skill templates `4322`–`4331` and `4338` in `dist/game/data/stats/skills/04300-04399.xml` (abnormal level and 3600s). Life Cubic is a summon there; this server records the same id as a buff row.
- `handlers/bypass/npc/SupportBlessing.java` and skill `5182` (7200s). Refused when level > 39 or class level ≥ 2.
- `Player.ini` warehouse slots **120** dwarf / **100** other, inventory slots **100** / **80**, `SendWareHouseDepositList` fee **30** adena per requested line
- `Creature.getMaxLoad`: `floor(CON bonus * 69000 * AltWeightLimit)` with `AltWeightLimit` 1

`tools/import_teleports.py` rewrites `data/teleports.json` from a local Mobius tree. `data/newbie-buffs.json` and `data/item-weights.json` are the same kind of transcription.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 105 | `TeleportSelectRequest` | `TeleportSelectRequestMsg` |
| C → S | 106 | `WarehouseDepositRequest` | `WarehouseDepositRequestMsg` |
| C → S | 107 | `WarehouseWithdrawRequest` | `WarehouseWithdrawRequestMsg` |
| C → S | 108 | `SupportMagicRequest` | `SupportMagicRequestMsg` |
| C → S | 109 | `GiveBlessingRequest` | `GiveBlessingRequestMsg` |
| S → C | 1105 | `TeleportList` | `TeleportListMsg` |
| S → C | 1106 | `WarehouseDepositList` | `WarehouseDepositListMsg` |
| S → C | 1107 | `WarehouseWithdrawList` | `WarehouseWithdrawListMsg` |
| S → C | 1108 | `NewbieBuffResult` | `NewbieBuffResultMsg` |
| S → C | 1109 | `BlessingResult` | `BlessingResultMsg` |

The same NPC, range (150), and alive checks as `BypassRequest` apply. Business refusals are `SystemMessage` 1040, not `Error`, except a malformed body (`bad_request`), range, death, or a missing NPC.

## Bypass commands

| Command | Result |
|---------|--------|
| `showTeleports` or `showTeleports <list>` | `TeleportList` and `NpcHtml`. Default list is `NORMAL`. |
| `teleport <list> <id>` | Move, or a refusal. `<id>` is the 0-based index. |
| `goto <id>` | `teleport NORMAL <id>` |
| `goto <list> <id>` | `teleport <list> <id>` |
| `DepositP` | `WarehouseDepositList` and a short `NpcHtml` |
| `WithdrawP` | `WarehouseWithdrawList` and a short `NpcHtml` |
| `SupportMagic` | Buff rows, `NewbieBuffResult`, `AbnormalStatusUpdate`, `MagicSkillUse` |
| `SupportBlessing` | `default/BlessingOfProtection.htm` |
| `GiveBlessing` | Blessing row or a refusal |
| `showNoblesSelect` | Still the teleport stub |
| `DepositC`, `WithdrawC`, `DepositF`, `WithdrawF`, `package_deposit`, `package_withdraw` | Still the warehouse stub |

`TeleportList.feeCount` is what this player pays now (0 when the trip is free). The HTML omits the price when the fee is 0, matching Mobius.

A destination is on a loaded map when any spawned NPC is within **8000** (2D). Talking Island village and Cedric cover the island gatekeeper spots once those spawns are loaded. Gludin and the other towns are outside that radius today, so the server replies with system message **191** and does not move the player or take the fee. Movement uses the existing relocate path (`MoveToAck` and AOI). `FINEAGE_TELEPORT_OFFSET` defaults to Mobius `MaxOffsetOnTeleport` **50**. An offset that would leave the loaded neighborhood is dropped.

If a destination lists castle ids and that siege is in progress, message **192** and no move.

## Buffs

The first character created on an account is a newbie (`accountCharNumber == 0` at creation). Later characters are not. Levels **8–25** are eligible. Each buff has its own inclusive window (Wind Walk 8–24, Shield 11–23, and so on through Life Cubic 16–19). Fighter and mage sets differ. Orc mystics are not in `BEGINNER_MAGE`, so they receive the fighter set. Effects that the skill catalog does not implement are still stored on the character and sent as `AbnormalStatusUpdate` (1017) plus `MagicSkillUse` (1066). The Life Cubic summon and the Blessing of Protection damage rule are not simulated.

## Private warehouse

Persisted on the character, separate from the bag. Dwarves (race 4) have 120 warehouse slots and 100 inventory slots. Everyone else has 100 and 80. Deposit fee is 30 adena per line. Adena that is being deposited does not pay the fee; the adena left behind must cover it. Equipped items are refused. A stack already in the warehouse does not take another slot. Withdraw refuses a full inventory (**195**) or a load above `floor(CON bonus * 69000)` (**196**). Success sends `InventoryUpdate` (1015) and system message **198**.

Item weights live in `data/item-weights.json`. An id that is missing weighs 0.

## System messages (1040)

| Id | When |
|----|------|
| 190 | Teleport fee, not enough adena (or the fee item) |
| 191 | Destination is not on a loaded map |
| 192 | Destination castle is under siege |
| 193 | Teleport completed |
| 194 | Warehouse is full |
| 195 | Inventory is full |
| 196 | Weight limit |
| 197 | Warehouse fee, not enough adena |
| 198 | Warehouse transaction completed |
| 199 | Supplemental magic, level below 8 |
| 200 | Supplemental magic, level above 25 |
| 201 | Not a new character |
| 202 | Third class transfer (class level 3) |
| 203 | Blessing refused |
| 204 | Unknown teleport list or index |
| 205 | Noblesse list |
| 206 | Item cannot be stored |
| 207 | Reserved (freight stub stays HTML) |
| 208 | Level 25: inside the window, no buff left |
| 209 | Enchant window is open |

## Deferred

Noblesse teleports (`showNoblesSelect`, `NOBLES_ADENA`, `NOBLES_TOKEN`), clan warehouse, freight, subclass free-teleport exception (no subclasses), cursed-weapon and karma gates, the Life Cubic creature, and the Blessing of Protection PK damage rule. Castle-ground teleporter HTML during a siege is not selected; a destination castle id still blocks the move.
