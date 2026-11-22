# Event / quest scripted NPC behaviors (#805)

Named Mobius `Quest/Script` bypasses and priority `ai/others` actors are cataloged in [`data/npc-scripts.json`](../data/npc-scripts.json) and dispatched by [`NpcScriptRegistry`](../src/Fineage.Game/Services/NpcScriptRegistry.cs) → [`ZoneWorldNpcScripts.cs`](../src/Fineage.Zone/World/ZoneWorldNpcScripts.cs).

## Priority migrations

| Area | Scripts | Pipeline |
|------|---------|----------|
| Festival guides | `OracleTeleport` | `oracle_teleport` |
| Newbie guides | `NewbieHelper`, `NewbieTravelToken` | `newbie_helper`, `newbie_travel` |
| Adventurer guild | `RaidbossInfo` | `raid_boss_info` (stub) |
| Derby track | `TeleportToRaceTrack` | `monster_race_teleport` |
| Catacombs/necropolis | `DungeonGatekeeper` | `dungeon_gate` via `DungeonAccess` / `TryDungeonAsync`, not Script bypass |
| Village masters | `*Change*`, `FirstClassTransferTalk` | `village_master` |
| Clan / alliance | `ClanMaster`, `AllianceMaster` | `pledge` |

## Logging

- Unknown script id → one `[npc-script] unhandled script id=…` log + unavailable HTML.
- Known script declining an NPC/event → one log per `(script, npcId, event)` + unavailable HTML.
- `NpcTalk` first-talk with quest failure and no lines → still opens default NPC page.
