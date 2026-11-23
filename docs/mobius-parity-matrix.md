# Mobius CT_0 Interlude ↔ Fineage — parity matrix

> **2026-11-23** — re-verified against Fineage `dev` @ `8daf4209` and  
> Mobius `L2J_Mobius_CT_0_Interlude` @ `5c019037` (gitlab.com/MobiusDevelopment/L2J_Mobius, `master`).  
> Unity client `FioraSolutions/Fineage` `dev` @ `c1e79fe`.  
> Method: source and data diff, not issue trackers. Wire: [`wire-coverage.md`](wire-coverage.md).  
> Quests: [`QUEST_STATUS.md`](QUEST_STATUS.md) (344 / 0 / 0 / 0).  
> Prior snapshots: [`mobius-gap-audit-2026-11-22.md`](mobius-gap-audit-2026-11-22.md), [`mobius-gap-audit-2026-11-21.md`](mobius-gap-audit-2026-11-21.md).

**Legend:** ✅ done · 🟡 partial · ❌ missing · n/a (not an Interlude Net target, or a Mobius-only extra)

**Size:** S = one mechanism · M = a subsystem · L = a long tail of content

## Counts

| Status | Rows |
|--------|-----:|
| ✅ done | 86 |
| 🟡 partial | 8 |
| ❌ missing | 11 |
| n/a | 3 |
| **total** | **108** |

Since the 2026-11-21 matrix (and the 2026-11-22 audit), the shared handlers for auto-loot, festival, freight, clan-hall siege, mercenary tickets, siege golem, day/night spawns, boats, walking routes, mother tree, command channel, augmentation, skill enchant, cursed weapons, raid points, fishing championship scoring, event-drop windows, and the quest packs landed in `dev`. The rows below are what the code does now.

## Matrix

| # | System | Status | Size | Gap (Mobius file → Fineage file) |
|---|--------|--------|------|----------------------------------|
| **Auth and character** |
| 1 | Login, roster, Net auth | ✅ | — | `network/clientpackets/AuthLogin.java` → `Fineage.Login` `Auth` / `ZoneAuth` |
| 2 | Character create (5 races, templates) | ✅ | — | `CharacterCreate.java` → `CreateCharacter` + class templates |
| 3 | Enter world / persist / inventory | ✅ | — | `EnterWorld.java` → `ZoneWorld` enter + `CharacterPersistWorker` |
| 4 | Logout and return to lobby | 🟡 | S | `Logout.java`, `RequestRestart.java` → disconnect persists in `ZoneWorld.Members.Leave`; no lobby-restart opcode (`RESTART_RESPONSE` absent) — #923 |
| **World and geo** |
| 5 | Movement, sit/stand, run/walk, validate | ✅ | — | `MoveToLocation.java`, `RequestActionUse.java` → `ZoneWorldMovement`, `RequestActionUse` |
| 6 | Geodata LOS / MoveCheck / path | 🟡 | M | `geoengine/GeoEngine.java` → `Geo/GeoEngine.cs`. Engine is ported; the repo has no `*.l2j` regions. A missing directory is logged and geo is disabled unless `FINEAGE_GEODATA_REQUIRED=1` — #918 |
| 7 | Doors | ✅ | — | `entity/actor/instance/Door.java` → `ZoneWorldDoors` |
| 8 | Peace, arena, water, swamp, damage, no-landing, mother tree, jail, siege, fishing, olympiad, HQ | ✅ | — | matching `entity/zone/type/*Zone.java` → `Geo/MoveZones.cs`, `JailZoneCatalog`, `FishingTable`, `OlympiadStadiums`, `ZoneWorldSiege.Structures` |
| 9 | Remaining zone types | ❌ | M | `NoStoreZone.java`, `NoSummonFriendZone.java`, `NoRestartZone.java`, `EffectZone.java`, `ScriptZone.java`, `TownZone.java`, `RespawnZone.java`, `ConditionZone.java`, `DerbyTrackZone.java`, `ResidenceTeleportZone.java`, `NoBookmark` / tax xml → not loaded. `ZoneWorldWedding.cs` passes `noSummonZone: false` — #917 |
| 10 | NPC spawns | ✅ | — | `data/spawns` (186 xml) → 189 spawn JSON files, none left `enabled: false` |
| 11 | Raid / grand boss schedule | 🟡 | S | `RaidBossSpawnManager.java` → `data/raidbosses.json` 216/223 enabled. Anakazel 25333–25338 stay off (script minions). **25512 Gigantic Chaos Golem** is `enabled: false` at 0,0,0 — the import has no coordinates — #926 |
| 12 | Day/night spawn swap | ✅ | — | `DayNightSpawnManager.java` → `ZoneWorldSpawns.DayNight` |
| 13 | Walking routes | ✅ | — | `WalkingManager.java` → `ZoneWorldWalking` |
| 14 | Boats | ✅ | — | `BoatManager.java` → `ZoneWorldBoats` + vehicle opcodes in `NetOpcode` |
| 15 | Monster race (derby) | ✅ | — | `games/MonsterRaceManager.java` → `ZoneWorldMonsterRace` |
| **Combat** |
| 16 | Melee hit/miss/crit/shield/shots/position | ✅ | — | `model/stats/Formulas.java` → `ZoneCombat` |
| 17 | Bow and arrows | ✅ | — | `Creature` bow branch → `ZoneWorldArrows` |
| 18 | Physical/magic skill catalog | ✅ | — | skill XML → `data/skill-combat.json`; a missing row refuses the cast |
| 19 | Blow / lethal | ✅ | — | blow handlers → `ZoneWorldSkills` |
| 20 | Magic crit / fail | ✅ | — | `Formulas` magic crit → `ZoneWorldSkillDamage` |
| 21 | Buffs, debuffs, stat funcs, hit-time apply | ✅ | — | `handlers/skill/effects` → `ZoneWorldSkills` `ScheduleEffect`; 108/112 effect names full in `skill-effect-coverage.json` |
| 22 | BlockBuffSlot family | 🟡 | S | `BlockBuffSlot.java` → coverage catalog marks `BlockBuffSlot` / `BlockAbnormalSlot` partial: the abnormal lands, the slot mutex does not — #922 |
| 23 | Cast conditions | ✅ | — | skill XML `<conditions>` → `SkillCastConditionEvaluator` (37/37 kinds, 189 imported trees) |
| 24 | Signets 454–460 | ✅ | — | `EffectPoint.java` (Mobius still TODO on the signet case) → `ZoneWorldSignets` pulses from the skill template |
| 25 | Cast vs movement, damage/stun break | ✅ | — | `Formulas.calcAtkBreak` → `ZoneWorldCastInterrupt`, `MagicSkillCanceled` |
| 26 | Spoil / sweep | ✅ | — | spoil handlers → `ZoneWorldSpoil` |
| 27 | Spoil Festival 302 / Sweeper | ✅ | — | skill 302 `targetType: AREA` → `ZoneWorldSpoil.Members.TrySkillUseSpoilOrSweep` |
| 28 | Death exp/SP/karma/restart | ✅ | — | `Player` death → `ZoneWorldDeath` including castle and siege HQ restart |
| 29 | PK item drop | ✅ | — | PK drop → `ZoneWorldDeathDrop` |
| 30 | PvP flag / karma | ✅ | — | `Player.updatePvPStatus` → `ZoneWorldPvp` |
| 31 | Weight and grade penalty | ✅ | — | weight penalty → `ZoneWorldWeight` |
| **AI and NPC combat** |
| 32 | Mob aggro / hate / clan / LOS | ✅ | — | `ai/AttackableAI.java` → `ZoneWorldMobAi`, `ZoneWorldAggro` |
| 33 | Minion follow and raid minion buff | ✅ | — | `AttackableAI` follow → `ZoneWorldNpcMovement` |
| 34 | NPC shots | ✅ | — | `Npc` recharge shots → `ZoneWorldNpcSkills` |
| 35 | NPC heal / buff / debuff | ✅ | — | `NpcAI` → `ZoneWorldNpcSkills` |
| 36 | Guards | ✅ | — | `ai/others` guards → `ScanAggression` |
| 37 | Scripted NPC / boss / area AI | 🟡 | L | `dist/game/data/scripts/ai` (90 java) → `data/npc-scripts.json` (32 rows) and `ZoneWorldNpcScripts`. An unknown script id logs `[npc-script] unhandled script id`. `RaidbossInfo` on 31775/31776 still calls `SendScriptUnavailable`; ranking HTML is only for npc 31603 — #919 |
| **Drops and economy** |
| 38 | Droplists | ✅ | — | per-NPC drop XML → 1518 `data/drops` JSON files via `DropCatalog`. A kill with no file logs `drop miss npc={id} (no droplist)`. #803 closed the spawn-set holes |
| 39 | Herbs | ✅ | — | herb items → `ZoneWorldHerbs` |
| 40 | Ground loot and protection | ✅ | — | `ItemsOnGroundManager.java` → `ZoneWorldGround` |
| 41 | Auto-loot | ✅ | — | `Attackable` drop + `AUTO_LOOT*` → `ZoneRates` / `ZoneWorldGround` |
| 42 | Party loot and adena share | ✅ | — | `Party.distributeItem` → `PartyLootRules` |
| 43 | Raid loot owner and curse | ✅ | — | raid drop owner → `ZoneWorldRaidLoot` |
| 44 | Inventory / equip / private warehouse | ✅ | — | `ItemContainer` → `ZoneWorldInventory`, `ZoneWorldGear` |
| 45 | Clan warehouse, crest, skills, academy | ✅ | — | `Clan.java` → `ZoneWorldPledge` |
| 46 | Freight warehouse | ✅ | — | freight warehouse → `FreightBypassHandler` |
| 47 | Shops, multisell, buylists | ✅ | — | `Buy.java` / `Multisell` → `ZoneWorldShops` |
| 48 | Shop wear / try-on | 🟡 | S | `handlers/bypass/npc/Wear.java` → `StubBypassHandler("Wear")` in `BypassRegistry.cs`. `SHOP_PREVIEW_LIST` is not sent — #924 |
| 49 | Trade, private store, manufacture | ✅ | — | trade and private-store packets → `ZoneWorldTrade`, `ZoneWorldPrivateStore`, `ZoneWorldManufacture` |
| 50 | Item enchant | ✅ | — | `RequestEnchantItem.java` → `ZoneWorldEnchant` |
| 51 | Crystallize from inventory | ❌ | S | `RequestCrystallizeItem.java` → no handler. Enchant-fail crystals in `ZoneWorldEnchant` are a different path — #921 |
| 52 | Augmentation (life stones) | ✅ | — | `RequestRefine.java` → `AugmentPacketHandler`, `AugmentationCatalog` |
| 53 | Skill enchant | ✅ | — | `RequestExEnchantSkill.java` → `SkillEnchantService` |
| 54 | Craft and recipes | 🟡 | S | `RecipeManager.java` → `ZoneWorldCraft`. Forget-recipe (`RequestRecipeBookDestroy`) has no opcode — #935 |
| 55 | Manor | ✅ | — | `CastleManorManager.java` → `Manor/*`, `ZoneWorldManor` |
| 56 | Auditable economy ledgers | ✅ | — | Fineage `*Ledger.cs` on consume, grant, fee, and trade. Not a Mobius type |
| **Social** |
| 57 | Party | ✅ | — | `RequestJoinParty.java` → `ZoneWorldParty` |
| 58 | Command channel | ✅ | — | `RequestExAskJoinMPCC.java` → `CommandChannelActor` |
| 59 | Friends and block | ✅ | — | friend packets → `ZoneWorldFriends` |
| 60 | Wedding | ✅ | — | `CoupleManager` / voiced Wedding → `ZoneWorldWedding` |
| 61 | Chat channels including hero | ✅ | — | `Say2.java`, `ChatHeroVoice.java` → `ZoneWorldChat` |
| 62 | Chat ban, jail, character and account ban | ✅ | — | `PunishmentManager.java` → `ZoneWorldPunishment` |
| 63 | Petitions | ❌ | S | `RequestPetition.java` → `Say` petition types return `NotInPetitionChat` (`ZoneWorldChat`) — #930 |
| 64 | Duels | ✅ | — | `DuelManager.java` → `ZoneWorldDuels` |
| 65 | Clan wars | ✅ | — | `ClanWar` → `ZoneWorldPledge.Wars` |
| 66 | Alliance | ✅ | — | `Alliance` → `ZoneWorldAlly` |
| 67 | Party matching rooms | ❌ | M | `RequestAskJoinPartyRoom.java` and `EX_PARTY_ROOM_MEMBER` → no room manager. Chat types exist and then have nowhere to go — #928 |
| 68 | User commands | ✅ | — | `handlers/chat/commands/user/*.java` → `UserCommandRegistry` dispatches ids onto teleport, mount, party, clan, channel, olympiad, and siege. Unstuck shares `BeginTownTeleport`. Unknown id is logged — #920 |
| 69 | Macros | ❌ | S | `RequestMakeMacro.java`, `SendMacroList.java` → no macro store — #927 |
| 70 | Community board | ❌ | L | `communitybbs/` and `RequestShowBoard.java` → no board — #929 |
| 71 | PC cafe points and coupons | ❌ | S | `PcCafePointsManager.java`, `RequestPCCafeCouponUse.java` → no points — #933 |
| **Companions** |
| 72 | Servitors | ✅ | — | `Servitor.java` → `ZoneWorldSummons` |
| 73 | Cubics | ✅ | — | `Cubic.java` → `ZoneWorldCubics` (owner M.Atk is in the success roll) |
| 74 | Pets (hunger, exp, evolve, feed) | ✅ | — | `Pet.java` → `ZoneWorldPets`, `ZoneWorldPets.Evolve` |
| 75 | Pet rename | ❌ | S | `RequestChangePetName.java` → `ZoneEntity.Summon` has a name field and no rename request — #936 |
| 76 | Mounts and wyvern | ✅ | — | `Ride` → `ZoneWorldMounts` |
| **Progression and services** |
| 77 | 1st and 2nd class village masters | ✅ | — | `village_master/*` → `ZoneWorldVillageMasters`, quests 211–233 and 401–418 |
| 78 | 3rd class sagas | ✅ | — | `Q0070`–`Q0100` → `QuestCatalog.Sagas`. Helper `cast()` is visual `MagicSkillUse`, matching Mobius |
| 79 | Subclass | ✅ | — | `SubClass` → `ZoneWorldSubclass` |
| 80 | Noblesse | ✅ | — | village-master noblesse → `ZoneWorldNoblesse` |
| 81 | Henna / symbol maker | ✅ | — | `Henna.java` → `ZoneWorldHenna` / `ZoneWorldSymbolMaker` (lists are HTML, not `HennaEquipList`) |
| 82 | Skill learn trees and transfer skills | ✅ | — | `SkillTreeData` → `ZoneWorld` skill learn + `ZoneWorldTransferSkills` |
| 83 | NPC HTML, teleport, warehouse, buff, territory status | ✅ | — | `handlers/bypass/npc/{Buy,Sell,Link,Teleport,SupportMagic,TerritoryStatus}.java` → `BypassRegistry`, `ZoneWorldNpcServices` |
| **Quests** |
| 84 | Quest packs | ✅ | — | 344 Mobius quest scripts → 344 implemented, 0 partial, 0 stub, 0 missing (`tools/quest_status.py`). The heuristic checks talk NPCs, kill targets, and engine calls, not drop rates |
| **PvE endgame** |
| 85 | Seven Signs periods, stones, catacombs | ✅ | — | `SevenSigns.java` → `SevenSignsEngine`, `ZoneWorldDungeons` |
| 86 | Festival of Darkness | ✅ | — | `SevenSignsFestival` → `ZoneWorldFestival` |
| 87 | Dimensional rift | ✅ | — | `DimensionalRiftManager.java` → `ZoneWorldDimensionalRift`. The earthquake warning is scheduled; the `Earthquake` packet is not sent |
| 88 | Mammon | ✅ | — | `SignsPriest` / Mammon → `ZoneWorldMammon` |
| 89 | Cursed weapons | ✅ | — | `CursedWeaponsManager.java` → `CursedWeapons/CursedWeaponsManager.cs` plus `CursedWeaponList` / `CursedWeaponLocation` |
| 90 | Lottery | ✅ | — | `LotteryManager.java` → `ZoneWorldLottery` |
| 91 | Raid boss points | ✅ | — | `RaidBossPointsManager.java` → `RaidBossPointsManager` and ranking HTML for npc 31603 |
| **Castle and siege** |
| 92 | Castle siege, towers, hired guards, HQ | ✅ | — | `Siege.java`, `SiegeGuardManager.java` → `ZoneWorldSiege.Battle` `SpawnHiredSiegeGuards` |
| 93 | Mercenary tickets and siege golem | ✅ | — | `MercTicketManager.java`, siege-golem condition → `ZoneWorldMercTickets`, `ZoneWorldSummons` |
| 94 | Clan hall auction and functions | ✅ | — | `ClanHallAuctionManager.java` → `ClanHallAuctionActor`, `ZoneWorldPledge.HallFunctions`, `AgitDecoInfo` |
| 95 | Clan hall siege | ✅ | — | `CHSiegeManager.java` → `ChSiegeActor` |
| 96 | Fortress siege | n/a | — | not an Interlude castle-scope target |
| **Olympiad and hero** |
| 97 | Olympiad, stadium, hero weapons | ✅ | — | `OlympiadGameManager` → `Olympiad/*`, `ZoneWorldOlympiad.Npc` hero weapon grant |
| 98 | Hero inscription | ❌ | S | `RequestWriteHeroWords.java` → no hero-words store. `EX_HERO_LIST` is only the olympiad status sheet — #932 |
| **Activities and items** |
| 99 | Fishing | ✅ | — | fishing packets → `ZoneWorldFishing` |
| 100 | Fishing championship | ✅ | — | `FishingChampionshipManager.java` → `ZoneWorldFishingChampionship.cs`. NPC HTML board, weekly reset, prize via `LedgerTxKind.FishingChampionshipPrize`. The ranking is the NPC page, not `RequestExFishRanking` |
| 101 | Soul crystals (Q350) | ✅ | — | `Q00350` → `ZoneWorldSoulCrystals` |
| 102 | Shots, arrows, consumables, seeds, merc tickets | ✅ | — | `handlers/items/{SoulShots,SpiritShot,EnchantScrolls,Seed,MercTicket,SummonItems}.java` → `ZoneWorldShots`, `ZoneWorldConsumables`, `ZoneWorldPets` |
| 103 | Item-handler tail | ❌ | S | `Book.java`, `Calculator.java`, `Maps.java`, `RollingDice.java`, `SpecialXMas.java` → no handlers (`DICE`, `SHOW_CALCULATOR`, `SHOW_XMAS_SEAL` absent) — #925 |
| **Meta** |
| 104 | GM admin commands | 🟡 | L | `handlers/chat/commands/admin` (77 handlers) → 34 `admin_*` verbs in `ZoneWorldAdmin`. Missing verbs are not dispatched — #931 |
| 105 | Seasonal events | ❌ | S | `scripts/events/{Christmas,L2Day,HeavyMedal}.java` → not present. Time-boxed `EventDropCatalog` is separate and done — #934 |
| 106 | Event drop windows | ✅ | — | `EventDropManager.java` → `Drops/EventDropCatalog.cs` |
| 107 | Player mail | n/a | — | `EX_MAIL_ARRIVED` / `CustomMailManager` are not a Fineage Net target |
| 108 | Mobius-only extras | n/a | — | captcha, rebirth, premium, sell-buffs, fake players, offline play, banking voiced, GameGuard, HackShield, GM snoop, Kamael slideshow. Not retail Interlude gameplay |

## Walked and folded into the rows above

These Mobius trees were walked. They do not get their own row when the mechanism already has one.

| Mobius tree | Where it landed |
|-------------|-----------------|
| `gameserver/ai` (14 engine types) | rows 32–36 |
| `scripts/handlers/skill/effects` (146) and `skill/targets` (34) | rows 21–23. Interlude skills in `skills.json` use 22 target types; the extra target classes are not referenced by those skills |
| `scripts/handlers/bypass/npc` (28) | row 83, except Wear (48) and Mobius-only Rebirth / NpcViewMod / VoiceCommand (108) |
| `scripts/handlers/actions` click and shift-click | row 5 (target and social) |
| `entity/actor/instance` (59) | merchant, trainer, warehouse, doorman, raid, guard, boat, pet, cubic, festival, rift — rows 8, 14, 47, 72–74, 85–88 |
| `managers/*` | each manager is named on its row. AntiFeed, DailyReset, Id, PrecautionaryRestart, ServerRestart, ZoneBuild are process concerns, not gameplay gaps |
| `data/stats`, `buylists`, `multisell`, `teleporters`, `html`, `mapregion` | catalogs behind rows 18, 47, 55, 83 |
| `data/instances` (`Coliseum.xml` only) | no extra instance system beyond olympiad and the rift |
| `network/clientpackets` (158 + 46 extended) and `serverpackets` (270) | [`wire-coverage.md`](wire-coverage.md) |

## Ordered next work

PvE and character leveling first. Siege, olympiad, and clan are last because those stacks are already on `dev`.

1. Zone types that change movement, stores, summon, and death restart (row 9) — #917.
2. Geodata region files so LOS and paths leave stub mode (row 6) — #918. Loader is #793.
3. Scripted boss / area / other AI still outside `npc-scripts.json` (row 37) — #919. Host is #805.
4. Inventory crystallize (row 51) — #921.
5. BlockBuffSlot mutex, plus NoblesseBless and ProtectionBlessing (row 22) — #922.
6. Logout back to the character lobby (row 4) — #923.
7. Shop wear preview (row 48) — #924.
8. Book / map / dice / calculator / xmas item handlers (row 103) — #925.
9. One raid row with no coordinates: Gigantic Chaos Golem 25512 (row 11) — #926.
10. Macros (row 69) — #927.
11. Party matching rooms (row 67) — #928.
12. Community board (row 70) — #929.
13. Petitions (row 63) — #930.
14. GM admin verb tail (row 104) — #931. Then hero words #932, PC cafe #933, seasonal events #934, forget-recipe #935, pet rename #936 (rows 98, 71, 105, 54, 75).

Siege, olympiad matches, and clan halls are already on `dev` (rows 92–95, 97) and stay last. #299 (early quests) matches the 344/0/0/0 quest heuristic and should be closed. #296 stays open for private-store UI; manufacture windows already exist on the Unity client.

## Related docs

| Doc | Role |
|-----|------|
| This file | Living feature matrix |
| `wire-coverage.md` | Every Interlude client and server packet |
| `QUEST_STATUS.md` | Per-quest heuristic |
| `mobius-gap-audit-2026-11-22.md` | Previous audit snapshot — do not edit |
| `combat-parity.md` | Combat slice checklist |
| `skill-effect-coverage-audit.md` | Effect-name catalog |
| `skill-cast-condition-audit.md` | Cast-condition kinds |
