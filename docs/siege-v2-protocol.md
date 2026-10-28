# Zone V2 siege wire

Castle registration and ownership for Fineage issue #106. Combat, control towers, and artifact casting are not on this wire. `CaptureCastle(clanId)` is the domain hook those systems call when an attacker clan takes the castle.

Interlude rules and defaults are in `Fineage.Protocol.SiegeProtocol` and `Fineage.Zone.Siege`. This server does not copy the Java classes. Reference (L2J Mobius CT 0 Interlude, `org.l2jmobius`):

- `gameserver/mechanics/siege/Castle.java` — owner, tax percent, `hasCastle` on the clan
- `gameserver/mechanics/siege/Siege.java` — register, approve, unregister, start, end, `setNextSiegeDate`, `midVictory`
- `gameserver/managers/SiegeManager.java` — loads `Siege.ini`
- `gameserver/network/clientpackets/RequestJoinSiege.java`
- `gameserver/network/clientpackets/RequestConfirmSiegeWaitingList.java`
- `dist/game/config/Siege.ini` — cycle 2 weeks, length 120 minutes, min clan level 4, 500 attackers, 500 defenders
- `dist/game/config/SiegeSchedule.xml` — Sunday 16:00 and 20:00, five castles at once
- `dist/db_installer/sql/game/castle.sql` — nine castles, tax 0 in the inserted rows

`SiegeManager` falls back to clan level 5 when the ini key is missing. The shipped ini and the Interlude system message are level 4. This server uses 4 unless `FINEAGE_SIEGE_MIN_CLAN_LEVEL` is set.

## Opcodes

Quests use **42–45** and **1061–1063**. Skills use **50** and **1065–1066**. Character actions use **51** and **1067–1069**. This block is siege **46–49** and **1064**. Seven Signs uses **90–91** and **1090–1091**. Catacomb and necropolis access uses **92** and **1092**. Chat stays **1040–1049**. Seven Signs system lines are **120–136**, so siege lines start at **140**.

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 46 | `SiegeRegisterRequest` | `SiegeRegisterRequestMsg` |
| C → S | 47 | `SiegeDefenderAnswerRequest` | `SiegeDefenderAnswerRequestMsg` |
| C → S | 48 | `SiegeListRequest` | `SiegeListRequestMsg` |
| C → S | 49 | `AdminCaptureCastle` | `AdminCaptureCastleMsg` |
| S → C | 1064 | `SiegeInfo` | `SiegeInfoMsg` |

`AdminCaptureCastle` is ignored unless `FINEAGE_ZONE_ADMIN` is `1`, `true`, or `yes`. It calls `CaptureCastle(clanId)` for that castle. It does not run combat.

MessagePack keys are a fixarray (`0..n-1`). Extra keys may be appended. Do not renumber these.

Failures are `Error` **1099** (`0` code, `1` message). Success lines also use chat `SystemMessage` **1040**. Siege system ids start at **140**.

### `SiegeRegisterRequest` (46)

| Key | Field | Type |
|-----|--------|------|
| 0 | castleId | int |
| 1 | attacker | int |
| 2 | joining | int |

`attacker` **1** registers on the attacking side. **0** asks to defend. `joining` **1** registers. Any other value withdraws. That is Mobius `RequestJoinSiege` (`_isAttacker`, `_isJoining`).

### `SiegeDefenderAnswerRequest` (47)

| Key | Field | Type |
|-----|--------|------|
| 0 | castleId | int |
| 1 | clanId | int |
| 2 | approved | int |

**1** approves a clan that is waiting. Any other value refuses. A refused clan is removed whether it was waiting or already approved. The owner clan cannot be removed.

### `SiegeListRequest` (48)

| Key | Field | Type |
|-----|--------|------|
| 0 | castleId | int |

### `AdminCaptureCastle` (49)

| Key | Field | Type |
|-----|--------|------|
| 0 | castleId | int |
| 1 | clanId | int |

### `SiegeInfo` (1064)

| Key | Field | Type |
|-----|--------|------|
| 0 | castleId | int |
| 1 | name | string |
| 2 | ownerClanId | int |
| 3 | taxPercent | int |
| 4 | siegeUnixMs | long |
| 5 | registrationDeadlineUnixMs | long |
| 6 | registrationClosed | bool |
| 7 | inProgress | bool |
| 8 | clans | array |
| 9 | endsUnixMs | long |

`ownerClanId` **0** means an NPC castle. `taxPercent` **0** is the inserted Interlude default. The rate used by castle tax is `taxPercent / 100`. `registrationDeadlineUnixMs` is 24 hours before `siegeUnixMs`, or **0** when the slot was not accepted. `endsUnixMs` is **0** when the siege is not running.

Each clan:

| Key | Field | Type |
|-----|--------|------|
| 0 | clanId | int |
| 1 | side | int |

| Side | Meaning |
|------|---------|
| -1 | Owner. Not stored in the registration rows. Always listed first. |
| 0 | Approved defender |
| 1 | Attacker |
| 2 | Defender waiting for the owner |

## Castles

| Id | Name | Siege |
|----|------|-------|
| 1 | Gludio | Sunday 16:00 |
| 2 | Dion | Sunday 20:00 |
| 3 | Giran | Sunday 16:00 |
| 4 | Oren | Sunday 20:00 |
| 5 | Aden | Sunday 16:00 |
| 6 | Innadril | Sunday 20:00 |
| 7 | Goddard | Sunday 16:00 |
| 8 | Rune | Sunday 20:00 |
| 9 | Schuttgart | Sunday 16:00 |

Hours are UTC. At most five castles share one timestamp (`maxConcurrent` 5). A slot past that cap is scheduled but registration stays closed.

The next date follows `Siege.setNextSiegeDate`. If the stored date is already past, the base is now. That week's schedule day and hour are applied. If the result is still before now, add `SiegeCycle` weeks once (default **2**). A fresh server therefore skips the coming Sunday when that Sunday's hour is already past and lands two weeks after the Sunday of the current week.

## Rules

Checked in Mobius order.

| Rule | Result |
|------|--------|
| No clan | `siege_clan` |
| Not the leader | `siege_leader` |
| Clan is dissolving | `siege_dissolving` |
| Defender request and the castle has no owner | `siege_npc` |
| Inside 24 hours of the siege, or the slot was rejected | `siege_closed` |
| Siege already running and registration was not closed | `siege_progress` |
| Clan level below 4 | `siege_level` |
| Clan owns this castle | `siege_auto_defender` |
| Clan owns any castle | `siege_owns` |
| Already registered here | `siege_duplicate` |
| Registered at another castle on the same weekday | `siege_sameday` |
| 500 attackers | `siege_attackers` |
| 500 defenders, counting the owner, approved defenders, and waiting defenders | `siege_defenders` |

There are no alliances, so the ally-versus-owner attacker block is not applied. Privileges are not a separate mask: the leader is the only character who can register, withdraw, or approve. Withdrawing uses the same window. The owner cannot withdraw.

Waiting defenders are deleted when the window closes. Approving the clan that filled the last defender slot fails with `siege_defenders`, because Mobius counts that waiting clan toward the cap before the update.

`StartSiege` with no attackers cancels. Registrations stay, and the next date is computed. With attackers, the siege runs for **120** minutes and registration is closed. `EndSiege` clears registrations, keeps the owner, and schedules the next date.

`CaptureCastle(clanId)` requires a siege in progress and an attacker clan.

- One attacker and no defenders (NPC castle): that clan becomes the owner and the siege ends.
- Otherwise the approved defenders and the previous owner become attackers, the capturing clan becomes the owner, and the siege keeps running.

The previous owner's `hasCastle` becomes 0. The capturing clan's `hasCastle` becomes this castle id. A clan can own only one castle.

## Clan hasCastle

`ClanRecord.CastleId` is `hasCastle`. It is saved with the clan and with the castle row. Online members get `ZoneEntity.CastleId` from it, which equip conditions already read.

`CharInfo` keys **7** and **8** and `UserInfo` keys **13** and **14** stay clan id and crest. Those bits do not change when a castle changes hands, so capture does not append a castle key onto them.

## System lines (1040)

| Id | Text |
|----|------|
| 140 | Your clan has registered to attack {castle}. |
| 141 | Your clan has asked to defend {castle}. The castle owner must approve. |
| 142 | Your clan has withdrawn from the siege of {castle}. |
| 143 | The defender was approved for {castle}. |
| 144 | The defender was refused for {castle}. |
| 145 | The registration term for {castle} has ended. |
| 146 | The siege of {castle} has started. |
| 147 | The siege of {castle} has finished. |
| 148 | The siege of {castle} was cancelled because no attackers registered. |
| 149 | The attacking clan has taken {castle}. |

140–144 go to the leader who sent the request, then `SiegeInfo`. 145 is sent to players in the world when a window closes during a client request. 149, and 147 when the capture also ends the siege, go to every player in the world on the admin capture hook. `StartSiege` and `EndSiege` are domain calls. They update the saved siege and do not push 146, 147, or 148 by themselves.

## Configuration

| Variable | Default |
|----------|---------|
| `FINEAGE_SIEGE_CYCLE_WEEKS` | 2 |
| `FINEAGE_SIEGE_LENGTH_MINUTES` | 120 |
| `FINEAGE_SIEGE_MIN_CLAN_LEVEL` | 4 |
| `FINEAGE_SIEGE_MAX_ATTACKERS` | 500 |
| `FINEAGE_SIEGE_MAX_DEFENDERS` | 500 |
| `FINEAGE_SIEGE_REGISTRATION_CLOSE_HOURS` | 24 |

Unset or invalid values keep the default. The zone reads them at startup.
