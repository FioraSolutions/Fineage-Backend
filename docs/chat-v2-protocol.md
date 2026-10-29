# Zone V2 chat wire

Local say shows up as `CreatureSay` for the speaker and for nearby players. Shout reaches the whole zone. Everything else is a system line to the speaker. Tell, party, and clan are not delivered.

Opcodes already in use elsewhere (do not reuse): zone presence **1005–1007**, combat **1008–1010** and **1017**, inventory **1014–1016**, party **1027–1029**, lobby **1020–1026** and **1030–1033**. Chat replies sit in **1040–1049**. **1041–1049** are reserved and unused. **1019** is intentionally left free.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 8 | `Say` | `SayMsg` |
| S → C | 1012 | `CreatureSay` | `CreatureSayMsg` |
| S → C | 1040 | `SystemMessage` | `SystemMessageMsg` |

MessagePack keys are a fixarray (`0..n-1`). Extra keys may be appended later. Do not renumber these.

### `Say` (8)

| Key | Field | Type |
|-----|--------|------|
| 0 | text | string |
| 1 | chatType | int |

There is no whisper-target key. `Tell` is rejected like the other undelivered types.

### `CreatureSay` (1012)

| Key | Field | Type |
|-----|--------|------|
| 0 | objectId | int |
| 1 | name | string |
| 2 | text | string |
| 3 | chatType | int |

The speaker is included. `chatType` is the type the client sent (`0` or `1`).

### `SystemMessage` (1040)

| Key | Field | Type |
|-----|--------|------|
| 0 | id | int |
| 1 | params | string[] |
| 2 | text | string |

`text` is the line to show. `id` plus `params` are there when a client catalog wants to format the same line. `id` **0** means “show `text` as written.”

| Id | Meaning | Params | Text |
|----|---------|--------|------|
| 1 | Welcome, sent once at the end of enter world | `[name]` | `Welcome, {name}.` |
| 2 | Chat type is not delivered | `[chatType]` | `not supported yet` |
| 3 | Flood throttle | `[]` | `Please do not spam the chat.` |
| 2264 | Melee or offensive skill hit (Mobius `YOU_HIT_S1_FOR_S2_DAMAGE`) | `[targetName, damage]` | `You hit {targetName} for {damage} damage.` |

Combat damage lines go only to the attacking player, after `Attack` / `StatusUpdate` (or skill cast + target vitals). Observers still get combat animation and HP packets, not this system line.

Enter world order is unchanged up through knownlist (`UserInfo`, `ItemList`, `PlayerSpawned`, then `NpcInfo` / `SpawnItem` / `CharInfo`). `SystemMessage` welcome is the last packet of that burst.

## Chat types

Numbers match the client `ChatTypes` table.

| Id | Name | Delivery |
|----|------|----------|
| 0 | All | Speaker, plus players in the speaker's knownlist within **1250** (2D) |
| 1 | Shout | Every player in the zone, including outside knownlist |
| 2 | Tell | System message 2 |
| 3 | Party | System message 2 |
| 4 | Clan | System message 2 |
| 5–18 | Gm, petition, trade, alliance, hero, … | System message 2 |

Knownlist is the existing AOI. A player inside 1250 but outside interest does not hear `All`. A player inside interest but farther than 1250 does not hear `All` either. Shout does not use either check.

## Validation

Applied in order, before delivery:

1. Not in the world → `Error` `world` (no system message).
2. Null, empty, longer than **100** characters, a control character (`< U+0020` or `U+007F`, including a newline trim would remove), or empty after trim → `Error` `bad_request`. Trimmed text is what gets broadcast. The length check uses the raw string, so padding past 100 is rejected even if trim would fit.
3. Another accepted say on that session inside **1000** ms → `SystemMessage` 3. The window is not extended by the rejection.
4. Type other than All or Shout → `SystemMessage` 2. That attempt starts the flood window.

An unreadable `Say` body is still `Error` `bad_request`.
