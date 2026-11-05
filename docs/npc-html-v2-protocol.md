# Zone V2 NPC HTML

Talk to a non-attackable NPC, and follow the links in its window. Quest steps from `docs/quest-v2-protocol.md` stay on `NpcTalk` (44), `QuestEvent` (45), and `QuestDialog` (1062). This page is the Mobius `NpcHtmlMessage` / `RequestBypassToServer` path those quest packets do not cover.

Opcodes already taken include Seven Signs **90–91** / **1090–1091**, dungeon access **92** / **1092**, boss status **93** / **1093**, admin boss schedule **94**, siege info **1064**, skills **1065–1066**, and character actions **1067–1069**. This block is the next free pair: **95** and **1094**. **1099** stays `Error`.

MessagePack keys are a fixarray (`0..n-1`). Extra keys may be appended later. Do not renumber these.

## Choice

`QuestDialog` stays the quest window. A new server packet, `NpcHtml`, carries the NPC's own page. Generalizing `QuestDialog` would mix quest id, hook, and event into every merchant window, and the quest client already depends on 1062. `NpcTalk` is still the only talk request. It runs the quest first-talk hook, and sends `NpcHtml` only when no quest step matches.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 44 | `NpcTalk` | `NpcTalkMsg` (unchanged) |
| C → S | 95 | `BypassRequest` | `BypassRequestMsg` |
| S → C | 1062 | `QuestDialog` | quest step, or the Quest bypass list |
| S → C | 1094 | `NpcHtml` | `NpcHtmlMsg` |

## `NpcTalk` (44)

Same body as the quest wire: key 0 is the NPC object id.

The player must be in the world, alive, and within **150** (3D) of a non-attackable NPC. That distance is `NpcHtmlProtocol.InteractionDistance` and `QuestProtocol.TalkRange`. Mobius Interlude `Npc.INTERACTION_DISTANCE` is 250 (`Npc.canInteract` is 3D, and the `npc_` bypass check is 2D). This server keeps 150 so quest talk and the HTML window share one distance.

An attackable (a gremlin, any monster) is refused with `npc_attackable`. Combat, death, and respawn are unchanged.

When a datapack `first_talk` step matches, the reply is still `QuestDialog`, then `QuestList` when the journal changed. Roxxy and Bitz keep that path. When no step matches, the server sends `NpcHtml` for page 0 of that NPC type. The generated "I have nothing for you" page is not that reply. It is the Quest bypass list, below.

## `NpcHtml` (1094)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | npcId | int |
| 2 | html | string |
| 3 | itemId | int |

`itemId` is 0. Mobius `NpcHtmlMessage` has the same field for item windows. It stays 0 until item HTML exists.

The HTML is resolved before send. Tokens are literal replacements:

| Token | Value |
|-------|--------|
| `%objectId%` | NPC object id |
| `%npcId%` | template id |
| `%npcname%` | NPC name |
| `%playername%` | player name |
| `%player_name%` | player name |
| `%festivalMins%` | `0` (festival clock is not on this wire) |

## HTML paths

Page 0 is `{folder}/{npcId}.htm`. Page N is `{folder}/{npcId}-{N}.htm`. A missing file uses `default/` with the same name, then `npcdefault.htm`. If that file is absent too, the body is `I have nothing to say to you.`

| `npcType` | Folder |
|-----------|--------|
| `Merchant` | `merchant` |
| `Warehouse` | `warehouse` |
| `Teleporter` | `teleporter` |
| `Guard` | `guard` |
| `Trainer` | `trainer` |
| `Fisherman` | `fisherman` |
| `Adventurer` | `adventurer_guildsman` |
| `PetManager` | `petmanager` |
| `VillageMaster*` | `villagemaster` |
| `Doorman`, `CastleDoorman`, `ClanHallDoorman` | `doorman` |
| `SignsPriest`, `DawnPriest`, `DuskPriest` | `seven_signs` |
| anything else, including `Folk` | `default` |

`Folk` covers the newbie helper, the blacksmith on Talking Island (Altran is Folk, not Merchant), and the other villagers. Olympiad nobles and heroes are not branched yet: 31688 and 31690 use `default/` like any other NPC whose type does not override the folder. Karma deny pages (`{id}-pk.htm`) are not selected.

Files live in `data/html`. Talking Island village pages are the Interlude datapack text. See `data/html/SOURCE.txt`.

## `BypassRequest` (95)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | command | string |

`command` is at most 512 characters and has no control characters. A leading `bypass -h` or `bypass` is ignored. These forms are the same command:

- `Chat 1` with key 0 set to the NPC
- `npc_61000_Chat 1`
- `bypass -h npc_61000_Chat 1`

When both object ids are present they must match. The NPC must be in range, alive on the player side, and not attackable. The same rules as `NpcTalk`.

The first word selects a handler. `quest_talk:` and `quest_event:` select the Quest handler even though they have no space.

### Handled

| Command | Result |
|---------|--------|
| `Chat` or `Chat 0` | Same as `NpcTalk` (first talk, else page 0) |
| `Chat N` | Page N, no quest hook |
| `Link <path>` | `data/html/<path>` after substitution |
| `Quest` | `QuestDialog` hook `default`, the quest list (`quest_talk:<id>` links) |
| `Script` | Same list. Interlude HTML uses `bypass Script` for the Quest button |
| `questlist` | Same list. The adventurer guild uses this |
| `Quest <id>` | Talk hook for that quest (`QuestEvent` with an empty event) |
| `Quest <id> <event>` | Adv-event hook |
| `quest_talk:<id>` | Talk hook |
| `quest_event:<id>:<event>` | Adv-event hook |
| `Script <name>` | Readable not-implemented page. Village-master names such as `FirstClassTransferTalk` land here |
| `Buy <listId>` | `BuyList` (1095) when that list names this NPC. See `docs/shop-v2-protocol.md` |
| `Sell` | `SellList` (1096) for a merchant or fisherman |
| `Multisell <listId>` | `MultiSellList` (1097), full list |
| `exc_multisell <listId>` | `MultiSellList` filtered to the bag. Enchant is kept when the list says so |
| `Exchange <listId>` | Same window as `exc_multisell` |

`Link` rejects an empty path, a path that is not `.htm` or `.html`, and any segment `.` or `..`. A `teleporter/` path from an NPC that is not a Teleporter is the nothing-to-say page. A legal path that is not in the catalog says `My Text is missing`.

A Mobius quest script name that is not a number (`Quest Q0001_LettersOfLove`) returns HTML: `That quest is not available.` The numeric quest machine is unchanged.

### Stubs

These handlers return HTML `{service} is not implemented yet.` Registering another handler for the same command replaces the stub. `ZoneWorld.Bypasses` is the registry.

| Service | Commands | Later work |
|---------|----------|------------|
| Wear | `Wear` | preview window. `RequestPreviewItem` (99) answers with system message 189 |
| Teleport | `showNoblesSelect` | noblesse lists stay on this stub. `goto`, `teleport`, and `showTeleports` are live. See `docs/npc-services-v2-protocol.md` |
| Warehouse | `DepositC`, `WithdrawC`, `DepositF`, `WithdrawF`, `package_deposit`, `package_withdraw` | clan and freight. `DepositP` and `WithdrawP` are the private warehouse |

Any other command returns `That action is not implemented yet.` It does not close the window with `Error`. `TerritoryStatus` is in that set until a residence wire exists.

`IBypassHandler.Handle` runs while the zone lock is held and must not call back into the world. It returns a `BypassReply`. The world sends the HTML, replays talk, runs the quest hook, opens a shop window (`docs/shop-v2-protocol.md`), or applies a service intent (`docs/npc-services-v2-protocol.md`).

## Errors (1099)

| Code | When |
|------|------|
| `world` | `EnterWorld` has not finished |
| `bad_request` | Body, command, object-id mismatch, or illegal link |
| `range` | Farther than 150 from the NPC |
| `dead` | The player is dead |
| `quest_npc` | Object id is not an NPC in this zone |
| `npc_attackable` | The NPC is attackable |

Quest failures (`quest_unknown`, `quest_state`, and the rest) still use the quest codes when a Quest bypass reaches the quest machine.
