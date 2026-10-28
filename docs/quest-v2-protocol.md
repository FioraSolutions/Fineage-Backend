# Zone V2 quest wire

Quest state follows L2J Mobius Interlude rules: `Quest` / `QuestState` phases CREATED, STARTED, and COMPLETED; `cond` plus arbitrary variables (including `__compltdStateFlags`); one-time quests stay COMPLETED; repeatable completion and `RequestQuestAbort` delete the row (`exitQuest(true)`); the active cap is 25 (`MaxQuestCount`); `QuestList` is the started set plus a 128-byte one-time mask; NPC hooks are first-talk, talk, and adv-event. `IQuestKillHook.OnKill` is the later kill entry. Melee does not call it.

Opcodes already taken: equip **15–17**, party **18–19** / **26–27**, spoil **30–31**, clan **32–38**, enchant **39–41** and **1060**, chat **1040–1049**, clan replies **1050–1059**. This block is **42–45** and **1061–1063**. Siege uses **46–49** and **1064** (`docs/siege-v2-protocol.md`). Skills use **50** and **1065–1066** (`docs/skill-v2-protocol.md`). **1067–1069** are unused.

MessagePack keys are a fixarray (`0..n-1`). Extra keys may be appended later. Do not renumber these.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 42 | `RequestQuestList` | empty |
| C → S | 43 | `RequestQuestAbort` | `RequestQuestAbortMsg` |
| C → S | 44 | `NpcTalk` | `NpcTalkMsg` |
| C → S | 45 | `QuestEvent` | `QuestEventMsg` |
| S → C | 1061 | `QuestList` | `QuestListMsg` |
| S → C | 1062 | `QuestDialog` | `QuestDialogMsg` |
| S → C | 1063 | `QuestSound` | `QuestSoundMsg` |

Enter world does not push `QuestList`. The client asks with **42**, and the server pushes **1061** again after a journal change.

`SystemMessage` stays **1040**. Quest lines use ids **30–38** (chat is 1–3, enchant is 10–20, clan is 100–114).

## Client → server

### `RequestQuestAbort` (43)

| Key | Field | Type |
|-----|--------|------|
| 0 | questId | int |

Abort only applies to a STARTED quest. It deletes the row, the same as Mobius `RequestQuestAbort` → `exitQuest(true)`, so a one-time quest can be taken again after abort. A quest with `abortable: false` is refused.

### `NpcTalk` (44)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |

Runs the first-talk hook. The first matching datapack step wins (quest id order, then file order). If none match, the NPC's default page is sent. Quest links on that page use `quest_talk:<questId>`.

### `QuestEvent` (45)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | questId | int |
| 2 | event | string |

Empty `event` is the talk hook (`onTalk`). Any other token is the adv-event hook (`onAdvEvent`), the HTML bypass. Tokens are 1..64 characters from `A-Z`, `a-z`, `0-9`, `_`, and `-`. Datapack HTML uses `<a action="quest_event:accept">`.

The player must be within **150** (3D) of the NPC. That is the talk distance used for these hooks.

## Server → client

Success order, omitting any piece that did not happen:

1. `InventoryUpdate` (1015) when the bag changed
2. `SystemMessage` (1040) lines, in step order
3. `QuestSound` (1063), one packet per sound, in step order
4. `QuestDialog` (1062) when the step has HTML
5. `QuestList` (1061) when the journal changed

A rule failure that still has a line (the 25-quest cap, a quest that cannot be aborted) sends that `SystemMessage`, then `Error` (1099). Other failures are `Error` only.

### `QuestList` (1061)

| Key | Field | Type |
|-----|--------|------|
| 0 | active | `QuestJournalEntryMsg[]` |
| 1 | completedMask | byte[128] |
| 2 | completedQuestIds | int[] |
| 3 | maxActive | int |

`active` is only STARTED quests, sorted by quest id. CREATED and COMPLETED are not in it. CREATED does not count toward the cap. COMPLETED one-time quests are in `completedQuestIds` and, when `0 <= id <= 1023`, set bit `id % 8` of byte `id / 8` in `completedMask`.

### `QuestJournalEntryMsg`

| Key | Field | Type |
|-----|--------|------|
| 0 | questId | int |
| 1 | name | string |
| 2 | state | int |
| 3 | cond | int |
| 4 | progressFlags | int |
| 5 | journalWord | int |
| 6 | repeatable | bool |

`state` is 1 (STARTED) for every row in `active`. `progressFlags` is the raw `__compltdStateFlags` variable, or 0. `journalWord` is that value when it is non-zero, otherwise `cond`. That is the 4-byte status Mobius writes in `QuestList`.

### `QuestDialog` (1062)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | npcId | int |
| 2 | questId | int |
| 3 | hook | string |
| 4 | event | string |
| 5 | html | string |

`hook` is `first_talk`, `talk`, `event`, `kill`, or `default`. `questId` is 0 on a default page.

### `QuestSound` (1063)

| Key | Field | Type |
|-----|--------|------|
| 0 | name | string |

| Name | When |
|------|------|
| `ItemSound.quest_accept` | start |
| `ItemSound.quest_middle` | `cond` increases |
| `ItemSound.quest_itemget` | item reward |
| `ItemSound.quest_finish` | complete |
| `ItemSound.quest_giveup` | abort |

## System lines (1040)

| Id | Text | Params |
|----|------|--------|
| 30 | `You have acquired the quest {name}.` | `[name]` |
| 31 | `You cannot take on any more quests.` | `[]` |
| 32 | `{name} is complete.` | `[name]` |
| 33 | `{name} has been cancelled.` | `[name]` |
| 34 | `You have earned {item} ({count}).` | `[item, count]` |
| 35 | `You have obtained {count} Adena.` | `[count]` |
| 36 | `You have earned {exp} experience and {sp} SP.` | `[exp, sp]` |
| 37 | `{name} is already finished.` | `[name]` |
| 38 | `{name} cannot be aborted.` | `[name]` |

## Errors (1099)

`code` is snake_case. `message` is the line to show when there is no system message.

| Code | When |
|------|------|
| `world` | `EnterWorld` has not finished |
| `bad_request` | Body, event token, or reward that does not fit |
| `range` | Farther than 150 from the NPC |
| `dead` | The player is dead |
| `quest_npc` | Object id is not an NPC in this zone |
| `quest_unknown` | Quest id is not in the datapack |
| `quest_limit` | Already 25 STARTED quests |
| `quest_state` | Wrong phase, or the event does not match |
| `quest_abort` | The quest is not abortable |
| `quest_item` | A `take_item` step does not have the items |

## State

| Value | Name | Counts toward 25 | In `QuestList.active` | Kept after exit |
|-------|------|------------------|------------------------|-----------------|
| 0 | CREATED | no | no | until start, abort does not remove it |
| 1 | STARTED | yes | yes | until complete or abort |
| 2 | COMPLETED | no | no | one-time quests only |

`cond` becomes 1 on start. Variables are strings, key 1..20 (`A-Z a-z 0-9 _`), value up to 255 characters. Setting `cond` is the same as `set_cond`.

One-time `complete` clears variables, stores COMPLETED, and removes quest items. Repeatable `complete` deletes the row. Abort of a STARTED quest always deletes the row and removes quest items, even when the quest is one-time.

Item and adena rewards go through the bag (`InventoryUpdate`). Adena is item **57**. Experience and SP use `data/experience.json`: the level is the highest row whose cumulative exp is still met, and exp does not pass the max-level threshold. A level change refreshes that class's HP/MP. The smoke quest grants 50 exp, which stays at level 1 (level 2 needs 68).

Rows are stored on the character (`quests`, `exp`, `sp`, `level`) and written when the journal or the reward changes, and again on leave.

## Datapack

`data/quests.json`. Steps are `first_talk`, `talk`, `event`, or `kill`. `when` is `absent`, `created`, `started`, or `completed`, with an optional exact `cond`. Ops: `create`, `start`, `set_cond`, `set`, `unset`, `give_item`, `give_adena`, `give_exp_sp`, `take_item`, `complete`, `sound`.

Smoke quest **999** `Roxxy's Message` on Talking Island: Roxxy (**30006**, object **50010**) to Bitz (**30026**, object **50011**) and back. Talks only. Reward is Scroll of Escape **736**, 25 adena, 50 exp, and 5 SP. It is one-time and abortable.

## Kill hook

```csharp
public interface IQuestKillHook
{
    QuestOutcome OnKill(QuestJournal journal, PlayerInventory inventory, int npcId);
}
```

`QuestMachine` implements it and will run `kill` steps for that npc template. `ZoneWorld` combat does not call it.
