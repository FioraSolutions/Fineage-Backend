# Zone V2 party wire (minimal)

Interlude reference and Fineage client mapping live in the Project store: **`docs/party-java-l2.md`**.

## Opcodes

Party C→S ids sit after inventory **15–17** (`UseItem` / `UnequipItem` / `GiveItem`, #128). **20–25** are login-only on the zone port.

| Direction | Id | Name |
|-----------|-----|------|
| C→S | 18 | `PartyInviteRequest` |
| C→S | 19 | `PartyAnswerJoinRequest` |
| C→S | 26 | `PartyLeaveRequest` |
| C→S | 27 | `PartyKickRequest` |
| S→C | 1027 | `PartyAskJoin` |
| S→C | 1028 | `PartyJoinResult` |
| S→C | 1029 | `PartyMemberList` |

MessagePack shapes: `Fineage.Protocol.Messages` (`Party*Msg` types).

## Scope

In-memory party per zone instance; max 9; leader-only invite when already grouped; full roster fan-out on change. No loot rules, dismiss, or leader transfer yet.

## `dev` integration (dual playtest)

Integration branch **`dev`** carries the same party opcodes as **`main`** (#129) together with death/respawn (#162: `ReviveRequest` / `AdminKillPlayer`, `Die` / `Revive` / `TeleportToLocation`). Inventory **15–17** and party **18–19 / 26–27 / 1027–1029** stay unchanged; handlers live in `ZoneWorldParty` and `ZoneServer`.
