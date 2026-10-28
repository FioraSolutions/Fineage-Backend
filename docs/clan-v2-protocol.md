# Zone V2 clan wire

Basic pledge lifecycle for issue #103. Client UI is #104.

Interlude rules and defaults are in `Fineage.Protocol.ClanProtocol` (Mobius `ClanTable.createClan`, `L2Clan.checkClanJoinCondition`, `L2Clan.getMaxNrOfMembers`, `RequestJoinPledge`, `RequestAnswerJoinPledge`, `RequestWithdrawalPledge`, `RequestOustPledgeMember`, `L2Clan.removeClanMember`, `L2Request` 15s timeout, `Character.properties` day counts). This server does not copy those classes.

## Opcodes

Clan requests sit after admin spoil/sweep **30–31** and party **26–27**. **20–25** stay login-only. Equip stays **15–17**. Chat replies stay **1040–1049**. Clan server packets use **1050–1059** (**1054–1059** reserved).

| Direction | Id | Name | Body |
|-----------|-----|------|------|
| C → S | 32 | `ClanCreateRequest` | `ClanCreateRequestMsg` |
| C → S | 33 | `ClanInviteRequest` | `ClanInviteRequestMsg` |
| C → S | 34 | `ClanAnswerJoinRequest` | `ClanAnswerJoinRequestMsg` |
| C → S | 35 | `ClanLeaveRequest` | empty |
| C → S | 36 | `ClanKickRequest` | `ClanKickRequestMsg` |
| C → S | 37 | `ClanDissolveRequest` | empty |
| C → S | 38 | `ClanCancelDissolveRequest` | empty |
| S → C | 1050 | `ClanAskJoin` | `ClanAskJoinMsg` |
| S → C | 1051 | `PledgeStatusChanged` | `PledgeStatusChangedMsg` |
| S → C | 1052 | `PledgeMemberList` | `PledgeMemberListMsg` |
| S → C | 1053 | `ClanJoinResult` | `ClanJoinResultMsg` |

Failures are `Error` **1099** (`0` code, `1` message). Success lines also use chat `SystemMessage` **1040** (`0` id, `1` params, `2` text). Clan system ids start at **100** so they do not collide with chat ids 1–3. Show `text`.

MessagePack keys are a fixarray. Extra keys may be appended. Do not renumber these.

### `ClanCreateRequest` (32)

| Key | Field | Type |
|-----|--------|------|
| 0 | name | string |

### `ClanInviteRequest` (33)

| Key | Field | Type |
|-----|--------|------|
| 0 | targetObjectId | int |
| 1 | pledgeType | int |

`pledgeType` must be **0** (main). Subpledges are rejected.

### `ClanAnswerJoinRequest` (34)

| Key | Field | Type |
|-----|--------|------|
| 0 | requestorObjectId | int |
| 1 | response | int |

**0** declines. Any other value accepts.

### `ClanKickRequest` (36)

| Key | Field | Type |
|-----|--------|------|
| 0 | targetObjectId | int |

Object id is the character id, online or not.

### `ClanAskJoin` (1050)

| Key | Field | Type |
|-----|--------|------|
| 0 | requestorObjectId | int |
| 1 | requestorName | string |
| 2 | clanId | int |
| 3 | clanName | string |
| 4 | pledgeType | int |

The invite expires **15** seconds after it is sent. A late answer is `clan_invite`.

### `PledgeStatusChanged` (1051)

| Key | Field | Type |
|-----|--------|------|
| 0 | clanId | int |
| 1 | name | string |
| 2 | level | int |
| 3 | leaderObjectId | int |
| 4 | crestId | int |
| 5 | memberCount | int |
| 6 | dissolvingExpiryUnixMs | long |
| 7 | charPenaltyExpiryUnixMs | long |

`clanId` **0** clears the pledge. `crestId` is **0**. Expiry **0** means that timer is not running. Times are unix milliseconds.

### `PledgeMemberList` (1052)

| Key | Field | Type |
|-----|--------|------|
| 0 | clanId | int |
| 1 | leaderObjectId | int |
| 2 | members | array |

Each member:

| Key | Field | Type |
|-----|--------|------|
| 0 | objectId | int |
| 1 | name | string |
| 2 | classId | int |
| 3 | level | int |
| 4 | online | bool |
| 5 | pledgeType | int |

An empty `members` array with `clanId` 0 clears the list.

### `ClanJoinResult` (1053)

| Key | Field | Type |
|-----|--------|------|
| 0 | response | int |

Sent to the requestor. **0** declined. Otherwise the accepted response value.

### Presence

`UserInfo` (1016) appends `13` clanId and `14` clanCrestId after the expertise keys `10`–`12`.

`CharInfo` (1005) appends `7` clanId, `8` clanCrestId. Key `6` remains paperdoll.

Enter world order is unchanged through knownlist. A member then receives `PledgeStatusChanged` and `PledgeMemberList`, and the welcome `SystemMessage` is still last. Other online members get an updated `PledgeMemberList`.

## Rules

| Rule | Value |
|------|--------|
| Create level | 10 |
| Name | letters or digits, length 2–16, unique ignoring case |
| One clan | a character already in a clan cannot create |
| Level 0 cap | 10 members on the main pledge |
| Invite | leader only, target is a player in interest (AOI), not self, not busy |
| Answer | 15 seconds; conditions are checked again on accept |
| Leave | not the leader; 1 day join penalty (`ALT_CLAN_JOIN_DAYS`) |
| Dismiss | leader only, not self; target gets the join penalty and the clan cannot recruit for 1 day |
| Combat | a landed attack starts a 15 second stance; leave and dismiss are refused during it |
| Dissolve | leader only; clan remains for 7 days (`ALT_CLAN_DISSOLVE_DAYS`); cancel clears it |
| After dissolve | members who stayed have no join penalty; the leader cannot create for 10 days (`ALT_CLAN_CREATE_DAYS`) |

A day is 86_400_000 ms. War, castle, and clan-hall dissolution blocks are not applied.

Leader and member characters cannot be deleted.

## Error codes (1099)

`clan_level`, `clan_member`, `clan_create_penalty`, `clan_name`, `clan_name_length`, `clan_name_taken`, `clan_none`, `clan_leader`, `clan_leader_leave`, `clan_pledge`, `clan_self`, `clan_target`, `clan_target_clan`, `clan_busy`, `clan_waiting`, `clan_recruit_penalty`, `clan_join_penalty`, `clan_full`, `clan_invite`, `clan_combat`, `clan_dismiss_self`, `clan_dissolving`, `clan_not_dissolving`, `clan_store`, plus `world` and `bad_request`.

## System message ids (1040)

| Id | When |
|----|------|
| 100 | Clan created |
| 101 | You joined |
| 102 | Someone joined |
| 103 | You declined |
| 104 | They declined |
| 105 | You left |
| 106 | Someone left |
| 107 | Join wait |
| 108 | Someone was dismissed |
| 109 | Dismiss succeeded |
| 110 | Clan must wait to recruit |
| 111 | You were dismissed |
| 112 | Dissolution registered |
| 113 | Dissolution cancelled |
| 114 | Clan dissolved |
