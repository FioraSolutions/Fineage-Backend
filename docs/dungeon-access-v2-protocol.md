# Zone V2 catacomb and necropolis gates

Entrance and exit for the eight Interlude necropolises and six catacombs. The gate reads the Seven Signs period, cabal, and seal owners from the service in `docs/seven-signs-v2-protocol.md`. Priest dialogs and the town hunting-ground menu are not on this wire.

Opcodes are the next free ids after Seven Signs **90–91** / **1090–1091**. Re-check before merging if another branch has taken **92** or **1092**.

Reference rules (not copied code): L2J Mobius CT 0 Interlude `DungeonGatekeeper` (`necro`, `cata`, `exit`), `teleporters/dungeon/*.xml` (Gatekeeper Ziggurat entrance and exit coordinates, no fee), `SevenSigns.teleLosingCabalFromDungeons`, `necro_no.htm` / `cata_no.htm`, and `Feature.ini` `AltOpenCatacumbs` / `AltOpenNecropolis` (both default false). System message ids are the Interlude client ids where one exists.

`HuntingGroundsTeleport` is the town priest menu (SevenSigns bypass 11). It charges Ancient Adena that depends on the town, with a lower price when the visitor's cabal owns the Seal of Gnosis and the visitor chose that seal. Those prices are not this packet. The Ziggurat gate in `teleporters/dungeon` has no item cost, so `data/dungeons.json` ships `feeCount` 0. A row with a positive fee is still charged before the teleport: adena **57** uses system message **279**, Ancient Adena **5575** uses **701** with the text "You do not have enough Ancient Adena.", and any other item uses **701** with "You do not have enough required items."

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C→S | 92 | `DungeonEnterRequest` | `DungeonEnterRequestMsg` |
| S→C | 1092 | `DungeonEntered` | `DungeonEnteredMsg` |

The position change is also `MoveToAck` **1004** to the mover and to anyone who still sees them. Knownlist enter and leave use the same `CharInfo` / `NpcInfo` / `SpawnItem` / `DeleteObject` path as `MoveTo`. There is no separate teleport opcode.

`DungeonEnterRequest.Action` is **1** enter or **2** exit. `DungeonEntered.Action` is **1** enter, **2** exit, or **3** kicked by a period change. `Inside` is true only after enter.

## Dungeons

`data/dungeons.json` lists ids **1–8** necropolises and **9–14** catacombs. Entrance coordinates are the inside Ziggurat point. Exit coordinates are the surface point used for a voluntary exit and for a kick. Names match the Interlude priest list.

| Id | Kind | Name | Entrance npc | Exit npc |
|----|------|------|--------------|----------|
| 1 | necropolis | Necropolis of Sacrifice | 31095 | 31103 |
| 2 | necropolis | Pilgrim's Necropolis | 31096 | 31104 |
| 3 | necropolis | Worshipper's Necropolis | 31097 | 31105 |
| 4 | necropolis | Patriot's Necropolis | 31098 | 31106 |
| 5 | necropolis | Ascetics Necropolis | 31099 | 31107 |
| 6 | necropolis | Martyr's Necropolis | 31100 | 31108 |
| 7 | necropolis | Disciples Necropolis | 31101 | 31109 |
| 8 | necropolis | Saint's Necropolis | 31102 | 31110 |
| 9 | catacomb | Heretics Catacomb | 31114 | 31120 |
| 10 | catacomb | Catacomb of the Branded | 31115 | 31121 |
| 11 | catacomb | Catacomb of the Apostate | 31116 | 31122 |
| 12 | catacomb | Catacomb of the Witch | 31117 | 31123 |
| 13 | catacomb | Catacomb of Dark Omens | 31118 | 31124 |
| 14 | catacomb | Catacomb of the Forbidden Path | 31119 | 31125 |

Necropolis of Sacrifice enters at `-41570, 209785, -5089` and leaves at `-41567, 209292, -5091`. Heretics Catacomb enters at `-53174, -250275, -7911` and leaves at `42514, 143917, -5385`.

## Who may enter

`DungeonAccess.CanEnter(character, dungeonId)` is the gate. The character cabal is the Seven Signs record, not a field on the request.

Outside seal validation (recruitment, competition, and the results tally) any registered cabal may enter. An unregistered visitor is refused.

During seal validation:

- Dawn won, and Dawn holds the seal: only Dawn may enter. Necropolises require the Seal of Avarice. Catacombs require the Seal of Gnosis. Anyone else, including a Dawn member when that seal is not Dawn's, gets **1301**.
- Dusk won, and Dusk holds that seal: only Dusk may enter. Anyone else gets **1302**.
- The score is tied: either registered cabal may enter. The seals are not consulted. An unregistered visitor is still refused.

`FINEAGE_SSQ_OPEN_CATACOMBS` and `FINEAGE_SSQ_OPEN_NECROPOLIS` skip the cabal check for that kind. Both default to false (`AltOpenCatacumbs`, `AltOpenNecropolis`).

A dead visitor who would otherwise pass is refused with `dead` and is not moved. The fee, when the row has one, is taken only after the cabal check and the dead check.

Exit does not check the cabal. It places the character on that dungeon's surface point and clears the inside flag. A dead character cannot exit.

## Refusal lines

Failures also send `Error` **1099**. When the retail gate shows a system line or the Ziggurat HTML, that text is a `SystemMessage` **1040** as well. Params are empty. `Text` is the line.

| SystemMessage id | When | Text |
|------------------|------|------|
| 1301 | Seal validation, Dawn's gate failed | Only a Lord of Dawn may use this. |
| 1302 | Seal validation, Dusk's gate failed | Only a Revolutionary of Dusk may use this. |
| 137 | Every cabal refusal, including 1301 and 1302 | Did you think you could deceive me? You may not pass. |
| 279 | Fee item is adena 57 | You do not have enough adena. |
| 701 | Any other missing fee item | You do not have enough required items. Ancient Adena uses this id with "You do not have enough Ancient Adena." |
| 138 | Kicked as results or seal validation begins | You have been teleported to the nearest town due to the beginning of the Seal Validation period. |
| 139 | Kicked as recruitment or competition begins | You have been teleported to the nearest town because you have not signed for any cabal. |

137–139 continue the Seven Signs band 120–136. 1301, 1302, 279, and 701 are the Interlude ids.

| Error code | Meaning |
|------------|---------|
| `dungeon_unknown` | Id is not in the catalog |
| `dungeon_cabal` | Not registered, and the period is not a Dawn or Dusk seal gate |
| `dungeon_dawn` | 1301 |
| `dungeon_dusk` | 1302 |
| `dungeon_fee` | Cabal passed, not enough of the fee item |
| `dead` | Cabal passed, character is dead |
| `world` | EnterWorld first |
| `bad_request` | Action is not 1 or 2, or the body was not readable |

## Kick on period change

Passing the gate sets a runtime dungeon id, the same role as Mobius `Player.setIn7sDungeon`. It is not written to `accounts.json`. The saved position is the entrance or the exit.

When the Seven Signs period actually changes, anyone whose `CanEnter` would now fail is moved to that dungeon's exit, the id is cleared, and they receive system message **138** or **139** plus `DungeonEntered` action **3**. Fee and death do not evict someone who already passed. `MoveToAck` **1004** carries the move.

Mobius `teleLosingCabalFromDungeons` also removes non-winners at the start of the results tally, while the Ziggurat still lets any cabal walk back in during that tally, and its other branch kicks a signed player (`!cabal.isEmpty()`). This server kicks only when the gate itself would now refuse them, and it uses the surface exit from `dungeons.json` because no town-restart table is loaded. The kick sentences are the Mobius ones unchanged.

Recruitment after seal validation clears cabals first, so everyone inside loses access and is kicked with **139**.
