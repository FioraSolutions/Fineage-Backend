# Zone V2 boss schedule wire

Respawn schedule for Interlude raid bosses and the eight grand bosses. This does not implement boss AI, combat, or drops. Combat calls `ZoneWorld.OnBossKilled(npcId)` after the boss dies.

Reference rules (not copied code): L2J Mobius CT 0 Interlude `RaidBossSpawnManager`, `Spawn.setRespawnDelay`, `Rnd.get`, `NPC.ini` `RaidMinRespawnMultiplier` / `RaidMaxRespawnMultiplier`, `GrandBossManager`, `GrandBoss.ini`, `raidboss_spawnlist`, and `grandboss_data`. Grand-boss hour windows follow `QueenAnt`, `Core`, `Orfen`, `Zaken`, `Baium`, `Antharas`, `Valakas`, and `Frintezza` (`interval + getRandom(-random, +random)` hours).

Opcodes already taken elsewhere include Seven Signs **90–91** / **1090–1091**. Dungeon access uses **92** / **1092**. Boss schedule uses the next free ids.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C→S | 93 | `BossStatusRequest` | `BossStatusRequestMsg` |
| C→S | 94 | `AdminBossSchedule` | `AdminBossScheduleMsg` |
| S→C | 1093 | `BossStatus` | `BossStatusMsg` |

`AdminBossSchedule` is rejected with `forbidden` unless `FINEAGE_ZONE_ADMIN` is `1`, `true`, or `yes`.

## Status

`Kind` is 0 raid or 1 grand.

`Status` is shared: 0 alive, 1 waiting, 2 fighting, 3 dead. Raid bosses use alive and dead. Grand bosses also use waiting and fighting. Mobius stores raid dead as its own enum and some grand-boss scripts store a private dead value of 1. Both are normalized to dead **3** so waiting stays 1.

`BossStatusRequest.NpcId` 0 returns every boss, sorted by npc id. Any other id returns that row or `unknown_boss`.

`Enabled` is false for rows outside Talking Island. They stay in the catalog and on this wire, and the simulation does not place them in the world. The same rule keeps town spawn maps disabled.

`Spawned` is true while that object id is in the world.

## Respawn

Raid rows store `respawnDelaySeconds` and `respawnRandomSeconds` from `raidboss_spawnlist`. The window is `(delay ± random)` seconds, with each side raised to at least 1 second, then converted to milliseconds. A delay of 0 is not a window (dimensional-rift and script bosses). `RaidMinRespawnMultiplier` and `RaidMaxRespawnMultiplier` scale that window (`FINEAGE_RAID_MIN_RESPAWN_MULTIPLIER` and `FINEAGE_RAID_MAX_RESPAWN_MULTIPLIER`, default 1.0). The roll is inclusive on both ends.

Grand rows store `respawnIntervalHours` and `respawnRandomHours` from `GrandBoss.ini`:

| Boss | Npc | Interval hours | Random hours |
|------|-----|----------------|--------------|
| Queen Ant | 29001 | 36 | 8 |
| Core | 29006 | 60 | 8 |
| Orfen | 29014 | 48 | 8 |
| Zaken | 29022 | 60 | 8 |
| Baium | 29020 | 168 | 8 |
| Antharas | 29019 | 264 | 8 |
| Valakas | 29028 | 264 | 8 |
| Frintezza | 29045 | 48 | 8 |

`OnBossKilled` sets status dead, stores max HP and max MP for the next life, and sets `respawnUnixMs` to now plus the roll. The periodic tick spawns a due boss through the same NPC materialize path as the spawn catalog and sets status alive. A zero-delay raid boss stays dead until the next process start, which spawns it once the saved time is in the past.

On start, alive and fighting bosses with `enabled` true are placed in the world at their saved HP and MP. Non-positive saved vitals mean full. Waiting bosses are not placed. Dead bosses with a future `respawnUnixMs` stay unspawned. Spawn coordinates and headings are the Mobius `raidboss_spawnlist` / `grandboss_data` values. Frintezza's stored location is 0, 0, 0.

Object ids are 80001 upward, inside the town NPC band and below item instance ids.

## Admin

`Action` 1 marks the boss dead (`OnBossKilled`) and despawns it if it is in the world. `Action` 2 force-spawns it now, clears the respawn time, and sets status alive. Force spawn ignores `enabled` so a disabled row can be placed for a test.

## Errors

Failures use `Error` **1099**: `world`, `bad_request`, `unknown_boss`, `forbidden`.

## Persistence

`boss-schedule.json` sits beside the account store unless `FINEAGE_BOSS_STORE` is set. Version is 1. Each row stores `npcId`, `status`, `respawnUnixMs`, `currentHp`, and `currentMp`. The file is written when a boss dies, when one is force-spawned, when vitals are noted, when a due boss spawns, when a player leaves, and when the zone process stops.
