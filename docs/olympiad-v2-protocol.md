# Zone V2 Olympiad wire

Registration, matchmaking, stadium teleport, and noble points for Fineage issue #92. Damage, death, and the blow-by-blow fight are not on this wire. `ReportOlympiadResult(matchId, winnerObjectId)` is the hook combat calls when the fight is decided. `winnerObjectId` 0 is a draw.

Interlude rules and defaults are in `Fineage.Protocol.OlympiadProtocol` and `Fineage.Zone.Olympiad`. This server does not copy the Java classes. Reference (L2J Mobius CT 0 Interlude, `org.l2jmobius`):

- `gameserver/mechanics/olympiad/Olympiad.java` — month period, validation, weekly points, noble register and unregister
- `gameserver/mechanics/olympiad/OlympiadManager.java` — 22 stadiums, classed and non-classed pairing
- `gameserver/mechanics/olympiad/OlympiadGame.java` — port, disconnect, default, point transfer, gate pass
- `gameserver/mechanics/olympiad/OlympiadGameTask.java` — countdown steps and the 6 minute battle
- `gameserver/mechanics/olympiad/OlympiadStadium.java` — stadium slot
- `gameserver/mechanics/olympiad/CompetitionType.java` — classed and non-classed
- `gameserver/config/OlympiadConfig.java` and `dist/game/config/Olympiad.ini`
- `dist/game/data/zones/olympiad_stadium.xml` — stadium polygons, stored as `data/olympiad-stadiums.json`

`OlympiadGame` in this chronicle is the classed and non-classed match. There is no separate `OlympiadGameNormal` / `OlympiadGameClassed` / `OlympiadGameNonClassed` type. Hero promotion is not implemented.

## Opcodes

Siege uses **46–49** and **1064**. Skills use **50–51** and **1065–1066**. Character actions use **51** and **1067–1069**. Seven Signs uses **90–91** and **1090–1091**. Catacombs use **92** and **1092**. Bosses use **93–94** and **1093**. NPC HTML uses **95** and **1094**. Chat stays **1040–1049**. Siege lines are **140–149**. Olympiad lines are **160–179**.

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 60 | `OlympiadStatusRequest` | empty |
| C → S | 61 | `OlympiadRegisterRequest` | `OlympiadRegisterRequestMsg` |
| C → S | 62 | `OlympiadUnregisterRequest` | empty |
| C → S | 63 | `OlympiadLeaveRequest` | empty |
| C → S | 64 | `AdminReportOlympiadResult` | `AdminReportOlympiadResultMsg` |
| S → C | 1070 | `OlympiadStatus` | `OlympiadStatusMsg` |
| S → C | 1071 | `OlympiadMatch` | `OlympiadMatchMsg` |
| S → C | 1072 | `OlympiadResult` | `OlympiadResultMsg` |
| S → C | 1073 | `OlympiadCountdown` | `OlympiadCountdownMsg` |

`AdminReportOlympiadResult` is ignored unless `FINEAGE_ZONE_ADMIN` is `1`, `true`, or `yes`. It calls `ReportOlympiadResult(matchId, winnerObjectId)`. It does not run combat.

MessagePack keys are a fixarray (`0..n-1`). Extra keys may be appended. Do not renumber these.

Failures are `Error` **1099** (`0` code, `1` message). The same attempt also sends `SystemMessage` **1040** when a retail line exists. Success sends the line, then `OlympiadStatus`.

### `OlympiadRegisterRequest` (61)

| Key | Field | Type |
|-----|--------|------|
| 0 | classed | int |

**1** joins the classed queue. **0** joins the non-classed queue.

### `AdminReportOlympiadResult` (64)

| Key | Field | Type |
|-----|--------|------|
| 0 | matchId | int |
| 1 | winnerObjectId | int |

`winnerObjectId` **0** is a draw. Any other value must be one of the two participants.

### `OlympiadStatus` (1070)

| Key | Field | Type |
|-----|--------|------|
| 0 | period | int |
| 1 | cycle | int |
| 2 | inCompetition | bool |
| 3 | competitionStartUnixMs | long |
| 4 | competitionEndUnixMs | long |
| 5 | olympiadEndUnixMs | long |
| 6 | validationEndUnixMs | long |
| 7 | nextWeeklyUnixMs | long |
| 8 | registrationOpen | bool |
| 9 | noble | bool |
| 10 | points | int |
| 11 | competitionsDone | int |
| 12 | won | int |
| 13 | lost | int |
| 14 | drawn | int |
| 15 | queue | int |
| 16 | matchId | int |
| 17 | stadiumId | int |
| 18 | classId | int |

`period` **0** is the competition month. **1** is the 24 hour validation period. `queue` **0** is none, **1** is classed, **2** is non-classed. `stadiumId` **-1** means this character is not in a match. Stadium **0** is a real stadium.

### `OlympiadMatch` (1071)

| Key | Field | Type |
|-----|--------|------|
| 0 | matchId | int |
| 1 | stadiumId | int |
| 2 | zoneId | int |
| 3 | classed | bool |
| 4 | phase | int |
| 5 | playerOneId | int |
| 6 | playerTwoId | int |
| 7 | playerOneName | string |
| 8 | playerTwoName | string |
| 9 | instanceId | int |

`phase` is **0** countdown, **1** the 5 second pause after the port, **2** the 60 second fight countdown, **3** fighting, **4** the 40 second return countdown. `instanceId` is the stadium zone id. The open world is **0**.

Player one stands 900 units east of the stadium center. Player two stands 900 units west. Both use the center Z.

### `OlympiadResult` (1072)

| Key | Field | Type |
|-----|--------|------|
| 0 | matchId | int |
| 1 | outcome | int |
| 2 | winnerObjectId | int |
| 3 | pointsOne | int |
| 4 | pointsTwo | int |
| 5 | deltaOne | int |
| 6 | deltaTwo | int |
| 7 | rewardItemId | int |
| 8 | rewardCount | int |
| 9 | competitionsDoneOne | int |
| 10 | competitionsDoneTwo | int |

| Outcome | Meaning |
|---------|---------|
| 0 | Tie. Each noble loses `points / 5`, capped. |
| 1 | Player one won. Gate pass is granted. |
| 2 | Player two won. Gate pass is granted. |
| 3 | Player one disconnected after the port. Player two gains the points. No gate pass. |
| 4 | Player two disconnected after the port. |
| 5 | Both disconnected. Both lose the shared diff. |
| 6 | Player one defaulted before the port. |
| 7 | Player two defaulted before the port. |
| 8 | Both defaulted before the port. |

A default does not increment competitions done. A crash, a tie, and a decided win do.

### `OlympiadCountdown` (1073)

| Key | Field | Type |
|-----|--------|------|
| 0 | matchId | int |
| 1 | kind | int |
| 2 | seconds | int |

`kind` **1** is the move to the stadium, **2** is the fight start, **3** is the return to town. The same second is system line **177**. The steps are the Mobius loop: stadium waits announce 120, 60, 30, 15, 10, 5, 4, 3, 2, 1 for the default 120 second wait; the fight countdown is 60, 50, 40, 30, 20, 10, 5, 4, 3, 2, 1; the return countdown is 40, 30, 20, 10, 5, 4, 3, 2, 1.

The zone tick is 3 seconds, so several of those lines can arrive in one flush when the clock jumps a step.

## Schedule

Server time zone is `FINEAGE_SERVER_TIMEZONE` (default `UTC`). Competition starts at 18:00 and lasts 6 hours (`21_600_000` ms). It runs every day unless custom period settings are on.

The competition month ends at 12:00 on the first day of the next month. Validation then lasts 24 hours. When validation ends the cycle increments, noble point rows are deleted, and a new month starts. Noblesse itself is kept. Heroes are not computed.

Weekly points are `+3`, applied once when `nextWeeklyUnixMs` is reached, then scheduled 7 days out. The stored cap is `competitionsDone * 10 + 3 * 4`. A noble who has not fought is stored at 18 and becomes 12 on the next load or the next weekly add. That is the Mobius clamp.

Registration closes when fewer than 10 minutes remain in the competition window. The queue is cleared when the window ends. Matches already started run to the end.

## Rules

Noblesse is `GrantNoblesse(characterId)`. The noblesse quest is not on this wire. A character who is not noble is rejected.

Checked in the Mobius `registerNoble` order.

| Rule | Result |
|------|--------|
| Olympiad disabled or the competition window is closed | `olympiad_period` |
| Not noble | `olympiad_noble` |
| Current class is not the base class | `olympiad_subclass` |
| Zariche (8190) or Akamanah (8689) in the bag | `olympiad_cursed` |
| Inventory at least 80% of the limit (80, or 100 for a dwarf) | `olympiad_inventory` |
| Under 10 minutes left | `olympiad_late` |
| Already on a classed list | `olympiad_classed` |
| Already on the non-classed list or already in a match | `olympiad_registered` |
| Classed and under 3 points, or non-classed and under 5 | `olympiad_points` |

The first registration inserts the noble row at 18 points before the point check, so a configured start value below the floor still creates the row.

There is no subclass flow yet, so the subclass check passes while both class ids are the character's class. There is no event queue, so the "registered on an event" refusal is not applied.

Unregister is refused once the noble has been paired (`olympiad_selected`). Mobius checks the empty queue first, so that line is unreachable there. This server returns the selected line.

Matchmaking runs every 30 seconds during the window, or every 60 seconds while no noble rows exist. A classed match needs 5 nobles of that class waiting. A non-classed match needs 9. The minimum is the gate for the pass. The pass then takes pairs until fewer than 2 remain, even when a queue drops below the minimum. Stadiums 0–10 prefer non-classed. Stadiums 11–21 prefer classed. Each new match in a pass starts one second after the previous one.

## Points and rewards

`pointDiff = min(min(pointsOne, pointsTwo) / divisor, 10)`. The divisor is **3** for classed and **5** for non-classed.

| Result | Points | Gate pass |
|--------|--------|-----------|
| Decided win | Winner gains `pointDiff`. Loser loses it. | 6651 × 50 classed, × 30 non-classed |
| Disconnect after the port | Same transfer. Competitions done increases. | None |
| Both disconnect | Both lose `pointDiff`. | None |
| Tie, including the battle timer | Each loses `ownPoints / 5`, capped at 10. | None |
| Default before the port | The defaulter loses `ownPoints / 3`, capped at 10. Competitions done stays. | None |

The battle lasts 360000 ms. If combat has not called `ReportOlympiadResult` when that timer ends, the tie row is applied. That is the HP-less tie. This server does not read HP or damage. Combat owns the comparison and should report the winner, or 0 when damage is equal, before the timer.

Leaving the zone during the countdown is a default. Leaving after the port is a disconnect loss, and both nobles are moved back at once. A disconnect uses the same two paths.

## Stadiums

`data/olympiad-stadiums.json` has the 22 Interlude centers and the zone polygons (zone ids 11070–11091). The pair is teleported into that zone's instance. Knownlist is limited to the same instance, so the town does not see the match. The saved character position stays the point they left. When the return countdown ends they are moved back there. A crash return is immediate.

The move is `MoveToAck` **1004**, the same path as a dungeon teleport.

## System lines (1040)

| Id | Text |
|----|------|
| 160 | The Grand Olympiad Games are not currently in progress. |
| 161 | Only noblesse can participate in the Olympiad. |
| 162 | You cannot participate in the Grand Olympiad Games with a character in a subclass. |
| 163 | You cannot participate in the Olympiad while you possess a cursed weapon. |
| 164 | Inventory is too full (80 percent). |
| 165 | Registration is refused in the last 10 minutes. |
| 166 | Already on the classed waiting list. |
| 167 | Already on the non-classed list, or already in a match. |
| 168 | Registered for a classed match. |
| 169 | Registered for a non-classed match. |
| 170 | Removed from the waiting list. |
| 171 | Already selected for a match. |
| 172 | Not on a waiting list. |
| 173 | Not enough points. The param is the minimum. |
| 174 | Competition started, or a new Olympiad period started. Param `comp` or `period`. |
| 175 | Competition ended, or the period ended and validation began. Param `comp` or `period`. |
| 176 | Registration period ended. |
| 177 | Countdown. Params are kind and seconds. |
| 178 | Points earned or lost. Params are name, amount, and `earned` or `lost`. |
| 179 | Win, tie, or a cancelled match. Param is the winner name, `tie`, or `cancel`. |

174 and 175 go to every player in the world. 177–179 go to the two participants. Spectators are not implemented.

## Configuration

| Variable | Default |
|----------|---------|
| `FINEAGE_OLY_ENABLED` | true |
| `FINEAGE_OLY_START_HOUR` | 18 |
| `FINEAGE_OLY_START_MINUTE` | 0 |
| `FINEAGE_OLY_COMPETITION_MS` | 21600000 |
| `FINEAGE_OLY_BATTLE_MS` | 360000 |
| `FINEAGE_OLY_WEEKLY_MS` | 604800000 |
| `FINEAGE_OLY_VALIDATION_MS` | 86400000 |
| `FINEAGE_OLY_START_POINTS` | 18 |
| `FINEAGE_OLY_WEEKLY_POINTS` | 3 |
| `FINEAGE_OLY_CLASSED_MIN` | 5 |
| `FINEAGE_OLY_NONCLASSED_MIN` | 9 |
| `FINEAGE_OLY_REWARD_ITEM` | 6651 |
| `FINEAGE_OLY_CLASSED_REWARD` | 50 |
| `FINEAGE_OLY_NONCLASSED_REWARD` | 30 |
| `FINEAGE_OLY_MAX_POINTS` | 10 |
| `FINEAGE_OLY_WAIT_SECONDS` | 120 |
| `FINEAGE_OLY_CUSTOM_PERIOD` | false |
| `FINEAGE_OLY_PERIOD` | MONTH |
| `FINEAGE_OLY_PERIOD_MULTIPLIER` | 1 |
| `FINEAGE_OLY_COMPETITION_DAYS` | 1,2,3,4,5,6,7 |

`FINEAGE_OLY_WAIT_SECONDS` must be 120, 60, 30, 15, or 5. Anything else stays 120. Competition days use Java's numbering: 1 Sunday through 7 Saturday. They apply only when the custom period flag is on.

## Persistence

`olympiad.json` sits beside the account store. It keeps the cycle, the period, the month end, the validation end, the next weekly add, the noblesse ids, and each noble's points and competition counts. Queues and live matches are not stored. The return town point is what the account file keeps while the match is running, so a crash does not leave the character inside the stadium.

## Deferred

Combat (issue #95 in the client roadmap, and the zone combat owner) still has:

- HP, damage, and death inside the stadium
- Calling `ReportOlympiadResult` when one side reaches 0 HP, or with 0 when damage is equal
- The pre-fight buffs (Wind Walk, Haste, Acumen) and the full heal
- Removing hero skills, clan skills, summons, cubics, and party membership for the fight
- Olympiad restricted items, the enchant limit, and blessed spiritshots
- Spectators
- Hero assignment at the end of the month
