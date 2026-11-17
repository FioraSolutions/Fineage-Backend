# Zone V2 Seven Signs wire

> **Wire status:** Live client transport is Fineage Net (`NetOpcode` / `PacketEnvelope`). Opcode **numbers** in this file are legacy MessagePack `MsgType` ids for semantics reference only. On-wire tables: [`net-protocol.md`](net-protocol.md).


Period state, cabal membership, seal-stone scores, and the sky tint. Priest dialogs are not on this wire yet. Joining and contributing use a request packet.

Reference rules (not copied code): L2J Mobius CT 0 Interlude `SevenSigns`, `SignsPriest` (Dawn fee and castle checks), `SSQStatus`, and `Feature.ini` Seven Signs keys. The sky tint follows the Interlude SignsSky states. Mobius comments out the `SSQInfo` broadcast in the period task; this server still sends `SevenSignsSky` when the period changes and when status is requested.

Opcodes already taken elsewhere: C→S **8**, **15–19**, **26–27**, **30–38**; S→C **1040–1059**. Enchant is **68** and quests are **80**. Seven Signs uses **90–91** and **1090–1091**.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C→S | 90 | `SevenSignsStatusRequest` | `SevenSignsStatusRequestMsg` |
| C→S | 91 | `SevenSignsActionRequest` | `SevenSignsActionRequestMsg` |
| S→C | 1090 | `SevenSignsStatus` | `SevenSignsStatusMsg` |
| S→C | 1091 | `SevenSignsSky` | `SevenSignsSkyMsg` |

Period changes also send `SystemMessage` **1040**. Message ids **120–136** are the Seven Signs lines (`SevenSignsProtocol`). Chat stays **0–3**, enchant **10–20**, and clan **100–114**. **1041–1049** stay reserved for chat.

## Schedule

Server time zone is `FINEAGE_SERVER_TIMEZONE` (default `UTC`). The weekly boundary is Monday 18:00 in that zone (`FINEAGE_SSQ_START_DAY`, `FINEAGE_SSQ_START_HOUR`, `FINEAGE_SSQ_START_MINUTE`). Recruitment and results last `FINEAGE_SSQ_MINOR_MINUTES` (default 15). Competition and seal validation run until the next weekly boundary.

## Actions

`Action` 1 joins a cabal during competition. `Cabal` is 1 Dusk or 2 Dawn. `Seal` is 1 Avarice, 2 Gnosis, or 3 Strife. A base class (class level 0) is rejected. Dawn after the second class transfer costs one Lord's Certificate of Approval (6388) or 50000 adena, unless the caller is marked as a castle owner on the engine. The request packet does not accept a castle flag.

`Action` 2 contributes seal stones the character is holding: blue 6360 (3), green 6361 (5), red 6362 (10). The per-player cap is 1000000. Over-cap contributions are rejected whole.

`Action` 3 collects the contribution reward (Mobius priest `SevenSigns 9`): only during seal validation and only for a member of the winning cabal. The reward is the stones' value in Ancient Adena (5575); collecting clears it and the stone tallies, the contribution score stays. Under 3 AA, a loser, or a non-member gets `seven_signs_no_reward`; any other period gets `seven_signs_period`.

`Action` 4 sells seal stones for Ancient Adena in any period (Mobius `SevenSigns 17/18`): `BlueStones`/`GreenStones`/`RedStones` are destroyed and paid 3/5/10 AA each. "Exchange all" is the client sending every stone it holds. Missing stones → `seven_signs_stones`.

## Mammon

Merchant of Mammon (31113) and Blacksmith of Mammon (31126) spawn only during competition results and seal validation while the winning cabal also owns Avarice (merchant) or Gnosis (blacksmith). Each appears 30 s after its seal goes live and moves every hour to a random spot: the eight Necropolises (merchant) or six Catacombs (blacksmith), Mobius `random_spawn_loc` groups 11/12. Their talk pages are NPC HTML (`seven_signs/mammmerch_1.htm`, `seven_signs/mammblack_1.htm`); with `FINEAGE_SSQ_STRICT` (default true) only a member of the winning cabal that holds the matching seal may talk (system 1301 / 1302, or 1176 when there is no winner). Shops are the datapack multisells 311132501 and 311262501–311262512 (release/re-seal A and S armor and jewels, special abilities, upgrade/exchange weapons). Black Marketeer of Mammon (31092, town spawns) swaps Ancient Adena for adena 1:1 via the HTML bypass `SevenSigns 7 <amount>`.

## Status and sky

`SevenSignsStatus` is one record for the four Interlude SSQStatus pages: period, cycle, the caller's cabal and seal, stone points, festival points, the 500-point stone share plus festival score, seal owners, member and seal-choice counts, and the next boundary in unix milliseconds.

`SevenSignsSky.State` is 0 normal, 1 dusk, 2 dawn. It is dusk or dawn only during seal validation, and only when that cabal leads. A tie stays 0.

## Errors

Failures use `Error` **1099** with snake_case codes: `seven_signs_period`, `seven_signs_cabal`, `seven_signs_seal`, `seven_signs_class`, `seven_signs_castle`, `seven_signs_fee`, `seven_signs_contrib`, `seven_signs_stones`, `seven_signs_no_reward`, plus `world` and `bad_request`.

## Persistence

`seven-signs.json` sits beside the account store. It keeps the period, scores, seal owners, and each character's cabal, seal, and contribution. `FINEAGE_SSQ_LAZY_UPDATE` defaults to true: the file is written on period change, on leave, and on `Save`. Set it to false to write every accepted join, contribution, and festival score.

Catacomb and necropolis gates read this state. See `docs/dungeon-access-v2-protocol.md`.
