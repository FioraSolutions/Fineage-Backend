# Mobius CT_0 Interlude ↔ Fineage — parity matrix

> **2026-11-27** — status semantics refreshed for issue #977. The underlying source/data pins remain the
> 2026-11-27 refresh: Fineage `dev` @ `503496ce` and Mobius `L2J_Mobius_CT_0_Interlude` @ `5c019037`
> (gitlab.com/MobiusDevelopment/L2J_Mobius, `master`). This pass did **not** re-run gameplay checks; it
> separates what existing evidence proves from what it does not.
> Method: source and data diff, not issue trackers. Wire: [`wire-coverage.md`](wire-coverage.md).
> Quests: [`QUEST_STATUS.md`](QUEST_STATUS.md). Required data: [`architecture/required-data.md`](architecture/required-data.md) (#985 foundation).
> Prior snapshots: [`mobius-gap-audit-2026-11-22.md`](mobius-gap-audit-2026-11-22.md), [`mobius-gap-audit-2026-11-21.md`](mobius-gap-audit-2026-11-21.md).

**This is the one canonical parity matrix.** Issue [#174](https://github.com/FioraSolutions/Fineage-Backend/issues/174)
links here; other docs are snapshots or implementation audits.

## Status levels and evidence bar

| Level | What it proves | What qualifies as evidence | What does **not** qualify |
|-------|----------------|----------------------------|---------------------------|
| **Implemented** | The mechanism/data/handler exists in the Fineage tree. | A concrete source, data or tool path linked from the cell. Behaviour of that path may still be wrong. | A handler count, quest heuristic, packet-coverage count or generated catalog on its own proves only this level. |
| **Parity verified** | The Fineage runtime reproduces the pinned Mobius behaviour for the stated scope. | A named automated behavioural test that exercises the runtime and asserts values traced to pinned Mobius source/data, or a documented side-by-side comparison with both pins. [`combat-parity.md`](combat-parity.md) is the combat evidence convention. | A structural/code counter, a catalog-presence test, a handler-count, or a test that only proves the file loads. |
| **Production verified** | The behaviour holds in a production-like run under concurrency, persistence and failure. | A linked artefact from Postgres/zone simulation/load/playtest/fault-injection/restore-drill execution. [`testing/economy-fault-injection.md`](testing/economy-fault-injection.md) is the acceptance bar for economy and durability. | A unit/integration test alone; an unrun checklist; a green build; this matrix itself. |

**Cell legend:** `✅ [evidence](...)` = verified at that level with a concrete link · `🟡 [evidence](...)` =
partially implemented (never a verified level) · `❌` = not implemented · `unverified` = no evidence at
that level (not a claim that the row is broken) · `n/a` = the level has no meaning for this row.

**Size:** S = one mechanism · M = a subsystem · L = a long tail of content

`tools/quest_status.py` / [`QUEST_STATUS.md`](QUEST_STATUS.md) and [`wire-coverage.md`](wire-coverage.md)
are **Implementation** counters. They identify wired quest packs and packet handlers; they never prove
behavioural parity or production verification.

## Counts

| Level | ✅ verified | 🟡 partial | unverified / ❌ | n/a | Total |
|-------|------------:|-----------:|----------------:|----:|------:|
| Implemented | 103 | 2 | 0 | 3 | 108 |
| Parity verified | 3 | 5 | 97 | 3 | 108 |
| Production verified | 0 | 0 | 105 | 3 | 108 |

**Current claim:** 103 of 108 systems are implemented; 3 have full linked parity evidence (movement A/B #1051,
weight penalty, and mob death AggroList clear #1105) and 5 have partial linked parity evidence (physical
auto-attack matrix, rows 16/17/72, the magic damage matrix, row 20, #979, and character-create initial equipment, row 2); **no system has linked production evidence**. The 0 production rows are intentionally
unverified, not failures. The old 92/4/9/3 counter was stale; the validator now derives these counts from
the rows.

Since the 2026-11-21 matrix (and the 2026-11-22 audit), the shared handlers for auto-loot, festival, freight, clan-hall siege, mercenary tickets, siege golem, day/night spawns, boats, walking routes, mother tree, command channel, augmentation, skill enchant, cursed weapons, raid points, fishing championship scoring, event-drop windows, and the quest packs landed in `dev`. The rows below are what the code does now.

## Matrix

Status cells are independent. `✅`/`🟡` in a status column must carry an evidence link; `unverified` is not a claim that the row is broken. `n/a` means the level has no meaning for that row. Section rows are bold separators, not rows.

| # | System | Implemented | Parity verified | Production verified | Size | Gap / code evidence (Mobius file → Fineage file) |
|---|--------|-------------|-----------------|---------------------|------|---------------------------------------------------|
| **Auth and character** |
| 1 | Login, roster, Net auth | ✅ [src](../src/Fineage.Login/LandingAuth.cs) | unverified | unverified | — | `network/clientpackets/AuthLogin.java` → `Fineage.Login` `Auth` / `ZoneAuth` |
| 2 | Character create (5 races, templates) | ✅ [src](../src/Fineage.Login/LoginService.Characters.cs) | 🟡 [StarterKitTests](../tests/Fineage.Game.Tests/StarterKitTests.cs) | unverified | — | `CharacterCreate.java` + `initialEquipment.xml` → `CreateCharacter` + `data/starter-kits.json`. Initial equipment, equipped slots, Tutorial Guide and StartingAdena=0 are asserted per base class against the pinned Mobius XML. Other create rules (name/appearance) stay unverified |
| 3 | Enter world / persist / inventory | ✅ [src](../src/Fineage.Zone/World/ZoneWorld.More.Enter.cs) | unverified | unverified | — | `EnterWorld.java` → `ZoneWorld` enter + `CharacterPersistWorker` |
| 4 | Logout and return to lobby | ✅ [src](../src/Fineage.Zone/World/ZoneWorldRestart.cs) | unverified | unverified | — | `RequestRestart.java` → `ZoneWorldRestart`: persist, keep the account, `RestartResponse` then `CharacterRoster`. TCP close still persists via `Leave` |
| **World and geo** |
| 5 | Movement, sit/stand, run/walk, validate | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMovement.cs) | ✅ [A/B tests](../tests/Fineage.Zone.Tests/GeoEngineTests.cs) | unverified | — | `MoveToLocation.java`, `RequestActionUse.java` → `ZoneWorldMovement`, `RequestActionUse`. Parity scope is the pinned [movement A/B](#movement-ab--1051-scoped) only |
| 6 | Geodata LOS / MoveCheck / path | ✅ [src](../src/Fineage.Zone/Geo/GeoEngine.cs) | unverified | unverified | — | `geoengine/GeoEngine.java` → `Geo/GeoEngine.cs`. `scripts/fetch-geodata.sh` loads `data/geodata-regions.json` before the documented boot. A missing region file is logged by name. `FINEAGE_GEODATA_REQUIRED=1` refuses to boot — #918 |
| 7 | Doors | ✅ [src](../src/Fineage.Zone/World/ZoneWorldDoors.cs) | unverified | unverified | — | `entity/actor/instance/Door.java` → `ZoneWorldDoors` |
| 8 | Peace, arena, water, swamp, damage, no-landing, mother tree, jail, siege, fishing, olympiad, HQ | ✅ [src](../src/Fineage.Zone/Geo/MoveZones.cs) | unverified | unverified | — | matching `entity/zone/type/*Zone.java` → `Geo/MoveZones.cs`, `JailZoneCatalog`, `FishingTable`, `OlympiadStadiums`, `ZoneWorldSiege.Structures` |
| 9 | Remaining zone types | ✅ [src](../src/Fineage.Zone/Geo/ZoneTypes/ZoneTypeRegistry.cs) | unverified | unverified | — | `entity/zone/type/*Zone.java` + `zones/*.xml` → `ZoneTypeRegistry` (`data/zone-types.json`). One profile class per type. Unknown type is logged and skipped. Wedding summon reads the no-summon flag. #917 |
| 10 | NPC spawns | ✅ [data](../data/spawns) | unverified | unverified | — | `data/spawns` (186 xml) → 189 spawn JSON files, none left `enabled: false` |
| 11 | Raid / grand boss schedule | ✅ [src](../src/Fineage.Zone/Bosses/BossSchedule.cs) | unverified | unverified | — | `RaidBossSpawnManager.java` → `data/raidbosses.json` 216/223 enabled. Anakazel 25333–25338 stay off (script minions). **25512 Gigantic Chaos Golem** is script-owned (`DrChaos` at 96320,-110912,-3328): catalog keeps `enabled: false` at 0,0,0 and load logs `raid 25512 has no coordinates` — #926 |
| 12 | Day/night spawn swap | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSpawns.DayNight.cs) | unverified | unverified | — | `DayNightSpawnManager.java` → `ZoneWorldSpawns.DayNight` |
| 13 | Walking routes | ✅ [src](../src/Fineage.Zone/World/ZoneWorldWalking.cs) | unverified | unverified | — | `WalkingManager.java` → `ZoneWorldWalking` |
| 14 | Boats | ✅ [src](../src/Fineage.Zone/World/ZoneWorldBoats.cs) | unverified | unverified | — | `BoatManager.java` → `ZoneWorldBoats` + vehicle opcodes in `NetOpcode` |
| 15 | Monster race (derby) | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMonsterRace.cs) | unverified | unverified | — | `games/MonsterRaceManager.java` → `ZoneWorldMonsterRace` |
| **Combat** |
| 16 | Melee hit/miss/crit/shield/shots/position | ✅ [src](../src/Fineage.Zone/Combat/ZoneCombat.cs) | 🟡 [PhysAutoMatrixTests](../tests/Fineage.MobiusDiff.Tests/PhysAutoMatrixTests.cs) | unverified | — | Auto-attack matrix (#979, #978 harness) asserts Mobius-traced hit/crit/shield/soulshot/position/level-gap/PvP/CP/timing for sword, blunt, dagger, pole, fist, dual, 2h sword, 2h blunt, plus player↔mob/player/summon and mob↔player/summon. Not a full Interlude cartesian (no buffs/enchant/traits grid). Evidence: [combat-parity.md](combat-parity.md#physical-auto-attack-differential-matrix-979). `Formulas.java` → `ZoneCombat` |
| 17 | Bow and arrows | ✅ [src](../src/Fineage.Zone/World/ZoneWorldArrows.cs) | 🟡 [PhysAutoMatrixTests](../tests/Fineage.MobiusDiff.Tests/PhysAutoMatrixTests.cs) + [NetBowArrowTests](../tests/Fineage.Zone.Tests/NetBowArrowTests.cs) | unverified | — | Formula cells: bow range 500, default reuse 1500, interval `timeAtk + (1500*333)/Pspd`, 76·proximity/SS/crit vs Gremlin (`phys-auto-p2m-bow`). Arrow consume/grade/last-arrow already Mobius-traced in `NetBowArrowTests`. Distance-scaled bow damage is Mobius-off by default and not asserted. `Creature` bow branch → `ZoneWorldArrows` / `AttackIntervalMs` |
| 18 | Physical/magic skill catalog | ✅ [src](../src/Fineage.Zone/Combat/SkillCombatCatalog.cs) | unverified | unverified | — | skill XML → `data/skill-combat.json`; a missing row refuses the cast |
| 19 | Blow / lethal | ✅ [src](../src/Fineage.Zone/Combat/ZoneCombat.Rolls.cs) | unverified | unverified | — | blow handlers → `ZoneWorldSkills` |
| 20 | Magic crit / fail | ✅ [src](../src/Fineage.Zone/Combat/ZoneCombat.Magic.cs) | 🟡 [MagicMatrixTests](../tests/Fineage.MobiusDiff.Tests/MagicMatrixTests.cs) + [ZoneMagicCritFailTests](../tests/Fineage.Zone.Tests/ZoneMagicCritFailTests.cs) | unverified | — | Seven Java-traced scenarios (HM/EM/DM casters, Willow Staff and unarmed, NPC and player targets, shield-block target): `calcMagicSuccess` rate (incl. negative), `calcMagicDam` base / spiritshot ×2 / blessed ×4 inside √mAtk, crit ×3 (×2.5 player→player) × `MAGIC_CRIT_DMG`, half-fail, resisted, shield mDef-add / perfect block, random-damage spread, `(int)` truncation. NPC M.Atk / M.Def now folded with INT/MEN bonus and levelMod. Not asserted: elemental bonus, PvP/PvE class-balance multipliers (1.0), drain/MP-damage/cubic/signet variants, `MaxMCritRate` only unit-tested. Evidence: [combat-parity.md](combat-parity.md#magic-damage-differential-matrix-979-slice-2). `Formulas.calcMagicDam` → `ZoneWorldSkillDamage.RollSkillDamage` |
| 21 | Buffs, debuffs, stat funcs, hit-time apply | ✅ [src](../src/Fineage.Zone/World/ZoneWorldAbnormals.cs) | unverified | unverified | — | `handlers/skill/effects` → `ZoneWorldSkills` `ScheduleEffect`; 111/112 effect names full in `skill-effect-coverage.json` |
| 22 | BlockBuffSlot family | ✅ [src](../src/Fineage.Zone/Combat/PlayableAttackRules.cs) | unverified | unverified | — | `BlockBuffSlot` / `NoblesseBless` / `ProtectionBlessing` → `ZoneWorldAbnormals` slot block, death consume, `PlayableAttackRules` via the effect name |
| 23 | Cast conditions | ✅ [src](../src/Fineage.Game/Skills/SkillCastConditionEvaluator.cs) | unverified | unverified | — | skill XML `<conditions>` → `SkillCastConditionEvaluator` (37/37 kinds, 189 imported trees) |
| 24 | Signets 454–460 | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSignets.cs) | unverified | unverified | — | `EffectPoint.java` (Mobius still TODO on the signet case) → `ZoneWorldSignets` pulses from the skill template |
| 25 | Cast vs movement, damage/stun break | ✅ [src](../src/Fineage.Zone/World/ZoneWorldCastInterrupt.cs) | unverified | unverified | — | `Formulas.calcAtkBreak` → `ZoneWorldCastInterrupt`, `MagicSkillCanceled` |
| 26 | Spoil / sweep | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSpoil.cs) | unverified | unverified | — | spoil handlers → `ZoneWorldSpoil` |
| 27 | Spoil Festival 302 / Sweeper | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSpoil.Members.cs) | unverified | unverified | — | skill 302 `targetType: AREA` → `ZoneWorldSpoil.Members.TrySkillUseSpoilOrSweep` |
| 28 | Death exp/SP/karma/restart | ✅ [src](../src/Fineage.Zone/World/ZoneWorldDeath.cs) | unverified | unverified | — | `Player` death → `ZoneWorldDeath` including castle and siege HQ restart |
| 29 | PK item drop | ✅ [src](../src/Fineage.Zone/World/ZoneWorldDeathDrop.cs) | unverified | unverified | — | PK drop → `ZoneWorldDeathDrop` |
| 30 | PvP flag / karma | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPvp.cs) | unverified | unverified | — | `Player.updatePvPStatus` → `ZoneWorldPvp` |
| 31 | Weight and grade penalty | ✅ [src](../src/Fineage.Zone/World/ZoneWorldWeight.cs) | ✅ [WeightPenaltyTests](../tests/Fineage.Zone.Tests/WeightPenaltyTests.cs) | unverified | — | weight penalty → `ZoneWorldWeight`; Mobius formula and test scope in [`combat-parity.md`](combat-parity.md) slice 9 |
| **AI and NPC combat** |
| 32 | Mob aggro / hate / clan / LOS | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMobAi.cs) | ✅ [DeathClearsAggroTests](../tests/Fineage.Zone.Tests/DeathClearsAggroTests.cs) | unverified | — | `ai/AttackableAI.java` → `ZoneWorldMobAi`, `ZoneWorldAggro`; death/decay AggroList clear traced to `Creature.doDie`/`decayMe` and `Attackable.onSpawn` (#1105, [`combat-parity.md`](combat-parity.md) slice 8) |
| 33 | Minion follow and raid minion buff | ✅ [src](../src/Fineage.Zone/Npcs/NpcMinionCatalog.cs) | unverified | unverified | — | `AttackableAI` follow → `ZoneWorldNpcMovement` |
| 34 | NPC shots | ✅ [src](../src/Fineage.Zone/World/ZoneWorldNpcSkills.cs) | unverified | unverified | — | `Npc` recharge shots → `ZoneWorldNpcSkills` |
| 35 | NPC heal / buff / debuff | ✅ [src](../src/Fineage.Zone/World/ZoneWorldNpcSkills.cs) | unverified | unverified | — | `NpcAI` → `ZoneWorldNpcSkills` |
| 36 | Guards | ✅ [src](../src/Fineage.Zone/World/ZoneWorldAggro.cs) | unverified | unverified | — | `ai/others` guards → `ScanAggression` |
| 37 | Scripted NPC / boss / area AI | 🟡 [src](../src/Fineage.Zone/World/ZoneWorldNpcScripts.cs) | unverified | unverified | L | Script host `#805` + `npc-scripts.json`. `RaidbossInfo` HTML+radar + `NpcLocationInfo` guide/radar + `ShadowWeapons` coupons + Gatekeeper `necro`/`cata`/`exit` → `DungeonAccess` — #919. Remaining: area/boss combat AI (`ai/areas`/`ai/bosses`) |
| **Drops and economy** |
| 38 | Droplists | ✅ [src](../src/Fineage.Zone/Drops/DropCatalog.cs) | unverified | unverified | — | per-NPC drop XML → 1518 `data/drops` JSON files via `DropCatalog`. A kill with no file logs `drop miss npc={id} (no droplist)`. #803 closed the spawn-set holes |
| 39 | Herbs | ✅ [src](../src/Fineage.Zone/World/ZoneWorldHerbs.cs) | unverified | unverified | — | herb items → `ZoneWorldHerbs` |
| 40 | Ground loot and protection | ✅ [src](../src/Fineage.Zone/World/ZoneWorldGround.cs) | unverified | unverified | — | `ItemsOnGroundManager.java` → `ZoneWorldGround` |
| 41 | Auto-loot | ✅ [src](../src/Fineage.Zone/World/ZoneWorldGround.cs) | unverified | unverified | — | `Attackable` drop + `AUTO_LOOT*` → `ZoneRates` / `ZoneWorldGround` |
| 42 | Party loot and adena share | ✅ [src](../src/Fineage.Game/Services/PartyLootRules.cs) | unverified | unverified | — | `Party.distributeItem` → `PartyLootRules` |
| 43 | Raid loot owner and curse | ✅ [src](../src/Fineage.Zone/World/ZoneWorldRaidLoot.cs) | unverified | unverified | — | raid drop owner → `ZoneWorldRaidLoot` |
| 44 | Inventory / equip / private warehouse | ✅ [src](../src/Fineage.Zone/World/ZoneWorldInventory.cs) | unverified | unverified | — | `ItemContainer` → `ZoneWorldInventory`, `ZoneWorldGear` |
| 45 | Clan warehouse, crest, skills, academy | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPledge.Warehouse.cs) | unverified | unverified | — | `Clan.java` → `ZoneWorldPledge` |
| 46 | Freight warehouse | ✅ [src](../src/Fineage.Zone/World/ZoneWorldFreight.cs) | unverified | unverified | — | freight warehouse → `FreightBypassHandler` |
| 47 | Shops, multisell, buylists | ✅ [src](../src/Fineage.Zone/World/ZoneWorldShops.cs) | unverified | unverified | — | `Buy.java` / `Multisell` → `ZoneWorldShops` |
| 48 | Shop wear / try-on | ✅ [src](../src/Fineage.Zone/Net/ShopPacketHandler.cs) | unverified | unverified | S | `Wear` → `ShopPreviewList` / `RequestPreviewItem` → `ShopPreviewInfo` (#924) |
| 49 | Trade, private store, manufacture | ✅ [src](../src/Fineage.Zone/World/ZoneWorldTrade.cs) | unverified | unverified | — | trade and private-store packets → `ZoneWorldTrade`, `ZoneWorldPrivateStore`, `ZoneWorldManufacture` |
| 50 | Item enchant | ✅ [src](../src/Fineage.Zone/World/ZoneWorldEnchant.cs) | unverified | unverified | — | `RequestEnchantItem.java` → `ZoneWorldEnchant` |
| 51 | Crystallize from inventory | ✅ [src](../src/Fineage.Game/CrystallizeRules.cs) | unverified | unverified | — | `RequestCrystallizeItem.java` → `CrystallizeRules` + `LedgerTxKind.Crystallize`. Enchant-fail crystals stay on `ZoneWorldEnchant` — #921 |
| 52 | Augmentation (life stones) | ✅ [src](../src/Fineage.Zone/Augmentation/AugmentationCatalog.cs) | unverified | unverified | — | `RequestRefine.java` → `AugmentPacketHandler`, `AugmentationCatalog` |
| 53 | Skill enchant | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSkillEnchant.cs) | unverified | unverified | — | `RequestExEnchantSkill.java` → `SkillEnchantService` |
| 54 | Craft and recipes | ✅ [src](../src/Fineage.Zone/World/ZoneWorldCraft.cs) | unverified | unverified | — | `RecipeManager.java` → `ZoneWorldCraft`. Forget-recipe is Net `RecipeBookDestroy` 0x03E6 (`RequestRecipeBookDestroy` 0xad) — #935 |
| 55 | Manor | ✅ [src](../src/Fineage.Zone/World/ZoneWorldManor.cs) | unverified | unverified | — | `CastleManorManager.java` → `Manor/*`, `ZoneWorldManor` |
| 56 | Auditable economy ledgers | ✅ [src](../src/Fineage.Zone/World/ZoneWorldLedger.cs) | unverified | unverified | — | Fineage `*Ledger.cs` on consume, grant, fee, and trade. Not a Mobius type |
| **Social** |
| 57 | Party | ✅ [src](../src/Fineage.Zone/World/ZoneWorldParty.cs) | unverified | unverified | — | `RequestJoinParty.java` → `ZoneWorldParty` |
| 58 | Command channel | ✅ [src](../src/Fineage.Zone/Party/CommandChannelActor.cs) | unverified | unverified | — | `RequestExAskJoinMPCC.java` → `CommandChannelActor` |
| 59 | Friends and block | ✅ [src](../src/Fineage.Zone/World/ZoneWorldFriends.cs) | unverified | unverified | — | friend packets → `ZoneWorldFriends` |
| 60 | Wedding | ✅ [src](../src/Fineage.Zone/World/ZoneWorldWedding.cs) | unverified | unverified | — | `CoupleManager` / voiced Wedding → `ZoneWorldWedding` |
| 61 | Chat channels including hero | ✅ [src](../src/Fineage.Zone/World/ZoneWorldChat.cs) | unverified | unverified | — | `Say2.java`, `ChatHeroVoice.java` → `ZoneWorldChat` |
| 62 | Chat ban, jail, character and account ban | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPunishment.cs) | unverified | unverified | — | `PunishmentManager.java` → `ZoneWorldPunishment` |
| 63 | Petitions | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPetitions.cs) | unverified | unverified | — | `RequestPetition` / `RequestPetitionCancel` / `PetitionManager` → `PetitionQueue` + `ZoneWorldPetitions`; missing petition id logged; petition chat requires consultation — #930 |
| 64 | Duels | ✅ [src](../src/Fineage.Zone/World/ZoneWorldDuels.cs) | unverified | unverified | — | `DuelManager.java` → `ZoneWorldDuels` |
| 65 | Clan wars | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPledge.Wars.cs) | unverified | unverified | — | `ClanWar` → `ZoneWorldPledge.Wars` |
| 66 | Alliance | ✅ [src](../src/Fineage.Zone/World/ZoneWorldAlly.cs) | unverified | unverified | — | `Alliance` → `ZoneWorldAlly` |
| 67 | Party matching rooms | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPartyMatch.cs) | unverified | unverified | — | `PartyMatchRoomList` / waiting list → `ZoneWorldPartyMatch` + `PartyMatchRoomActor`; PartyMatchRoom chat requires a room (logged when missing) — #928 |
| 68 | User commands | ✅ [src](../src/Fineage.Zone/World/ZoneWorldUserCommands.cs) | unverified | unverified | — | `handlers/chat/commands/user/*.java` → `UserCommandRegistry` dispatches ids onto teleport, mount, party, clan, channel, olympiad, and siege. Unstuck shares `BeginTownTeleport`. Unknown id is logged — #920 |
| 69 | Macros | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMacros.cs) | unverified | unverified | — | `RequestMakeMacro.java` / `RequestDeleteMacro.java` / `SendMacroList.java` → `ZoneWorldMacros`, `character_macros`, enter MacroList — #927 |
| 70 | Community board | ✅ [src](../src/Fineage.Zone/World/ZoneWorldCommunityBoard.cs) | unverified | unverified | — | `CommunityBoardHandler` / `RequestShowBoard` / `RequestBBSwrite` / `ShowBoard` → `CommunityBoardService` + `ZoneWorldCommunityBoard`; missing page logs the page name — #929 |
| 71 | PC cafe points and coupons | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPcCafe.cs) | unverified | unverified | — | `PcCafePointsManager` / `RequestPCCafeCouponUse` / `ExPCCafePointInfo` → `PcCafePointsManager` + coupon catalog + grant ledger (#933) |
| **Companions** |
| 72 | Servitors | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSummons.cs) | 🟡 [PhysAutoMatrixTests](../tests/Fineage.MobiusDiff.Tests/PhysAutoMatrixTests.cs) | unverified | — | Auto-attack only: summon→mob (`phys-auto-s2m-kat`), mob→summon, player→summon (Mobius `isPvP`, multipliers 1.0). Kat the Cat 14111 is catalog type Pet (warlock summons in this pack). Siege/pet inventory/other servitor features remain unverified. `Servitor.java` / `Formulas.calcPhysDam` → `ZoneWorldSummons` |
| 73 | Cubics | ✅ [src](../src/Fineage.Zone/World/ZoneWorldCubics.cs) | unverified | unverified | — | `Cubic.java` → `ZoneWorldCubics` (owner M.Atk is in the success roll) |
| 74 | Pets (hunger, exp, evolve, feed) | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPets.cs) | unverified | unverified | — | `Pet.java` → `ZoneWorldPets`, `ZoneWorldPets.Evolve` |
| 75 | Pet rename | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPets.cs) | unverified | unverified | — | `RequestChangePetName.java` → `ZoneWorldPets.TryChangePetName` — #936 |
| 76 | Mounts and wyvern | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMounts.cs) | unverified | unverified | — | `Ride` → `ZoneWorldMounts` |
| **Progression and services** |
| 77 | 1st and 2nd class village masters | ✅ [src](../src/Fineage.Zone/World/ZoneWorldVillageMasters.cs) | unverified | unverified | — | `village_master/*` → `ZoneWorldVillageMasters`, quests 211–233 and 401–418 |
| 78 | 3rd class sagas | ✅ [src](../src/Fineage.Game/Quests/QuestCatalog.Sagas.cs) | unverified | unverified | — | `Q0070`–`Q0100` → `QuestCatalog.Sagas`. Helper `cast()` is visual `MagicSkillUse`, matching Mobius |
| 79 | Subclass | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSubclass.cs) | unverified | unverified | — | `SubClass` → `ZoneWorldSubclass` |
| 80 | Noblesse | ✅ [src](../src/Fineage.Zone/World/ZoneWorldNoblesse.cs) | unverified | unverified | — | village-master noblesse → `ZoneWorldNoblesse` |
| 81 | Henna / symbol maker | ✅ [src](../src/Fineage.Zone/World/ZoneWorldHenna.cs) | unverified | unverified | — | `Henna.java` → `ZoneWorldHenna` / `ZoneWorldSymbolMaker` (lists are HTML, not `HennaEquipList`) |
| 82 | Skill learn trees and transfer skills | ✅ [src](../src/Fineage.Game/Skills/SkillTreeCatalog.cs) | unverified | unverified | — | `SkillTreeData` → `ZoneWorld` skill learn + `ZoneWorldTransferSkills`. Default `FINEAGE_TRAINER_SP_LEARN` is Mobius SP purchase at trainers (#1049 / #1064); load-test bots seed `LearnedSkills` (#1063) |
| 83 | NPC HTML, teleport, warehouse, buff, territory status | ✅ [src](../src/Fineage.Zone/World/ZoneWorldNpcServices.cs) | unverified | unverified | — | `handlers/bypass/npc/{Buy,Sell,Link,Teleport,SupportMagic,TerritoryStatus}.java` → `BypassRegistry`, `ZoneWorldNpcServices` |
| **Quests** |
| 84 | Quest packs | ✅ [src](../tools/quest_status.py) | unverified | unverified | — | 344 Mobius quest scripts → 344 implemented, 0 partial, 0 stub, 0 missing (`tools/quest_status.py`). The heuristic checks talk NPCs, kill targets, and engine calls, not drop rates. Implementation counter only — structural wiring, not behavioral parity. |
| **PvE endgame** |
| 85 | Seven Signs periods, stones, catacombs | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSevenSigns.cs) | unverified | unverified | — | `SevenSigns.java` → `SevenSignsEngine`, `ZoneWorldDungeons` |
| 86 | Festival of Darkness | ✅ [src](../src/Fineage.Zone/World/ZoneWorldFestival.cs) | unverified | unverified | — | `SevenSignsFestival` → `ZoneWorldFestival` |
| 87 | Dimensional rift | ✅ [src](../src/Fineage.Zone/World/ZoneWorldDimensionalRift.cs) | unverified | unverified | — | `DimensionalRiftManager.java` → `ZoneWorldDimensionalRift`. The earthquake warning is scheduled; the `Earthquake` packet is not sent |
| 88 | Mammon | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMammon.cs) | unverified | unverified | — | `SignsPriest` / Mammon → `ZoneWorldMammon` |
| 89 | Cursed weapons | ✅ [src](../src/Fineage.Zone/World/ZoneWorldCursedWeapons.cs) | unverified | unverified | — | `CursedWeaponsManager.java` → `CursedWeapons/CursedWeaponsManager.cs` plus `CursedWeaponList` / `CursedWeaponLocation` |
| 90 | Lottery | ✅ [src](../src/Fineage.Zone/World/ZoneWorldLottery.cs) | unverified | unverified | — | `LotteryManager.java` → `ZoneWorldLottery` |
| 91 | Raid boss points | ✅ [src](../src/Fineage.Zone/World/ZoneWorldRaidBossPoints.cs) | unverified | unverified | — | `RaidBossPointsManager.java` → `RaidBossPointsManager` and ranking HTML for npc 31603 |
| **Castle and siege** |
| 92 | Castle siege, towers, hired guards, HQ | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSiege.cs) | unverified | unverified | — | `Siege.java`, `SiegeGuardManager.java` → `ZoneWorldSiege.Battle` `SpawnHiredSiegeGuards` |
| 93 | Mercenary tickets and siege golem | ✅ [src](../src/Fineage.Zone/World/ZoneWorldMercTickets.cs) | unverified | unverified | — | `MercTicketManager.java`, siege-golem condition → `ZoneWorldMercTickets`, `ZoneWorldSummons` |
| 94 | Clan hall auction and functions | ✅ [src](../src/Fineage.Zone/World/ZoneWorldPledge.Halls.cs) | unverified | unverified | — | `ClanHallAuctionManager.java` → `ClanHallAuctionActor`, `ZoneWorldPledge.HallFunctions`, `AgitDecoInfo` |
| 95 | Clan hall siege | ✅ [src](../src/Fineage.Zone/Clan/ChSiegeActor.cs) | unverified | unverified | — | `CHSiegeManager.java` → `ChSiegeActor` |
| 96 | Fortress siege | n/a | n/a | n/a | — | not an Interlude castle-scope target |
| **Olympiad and hero** |
| 97 | Olympiad, stadium, hero weapons | ✅ [src](../src/Fineage.Zone/World/ZoneWorldOlympiad.cs) | unverified | unverified | — | `OlympiadGameManager` → `Olympiad/*`, `ZoneWorldOlympiad.Npc` hero weapon grant |
| 98 | Hero inscription | ✅ [src](../src/Fineage.Zone/Olympiad/OlympiadBoard.Heroes.cs) | unverified | unverified | — | `RequestWriteHeroWords` → olympiad board `Message` + monument hero list HTML (#932) |
| **Activities and items** |
| 99 | Fishing | ✅ [src](../src/Fineage.Zone/World/ZoneWorldFishing.cs) | unverified | unverified | — | fishing packets → `ZoneWorldFishing` |
| 100 | Fishing championship | ✅ [src](../src/Fineage.Zone/World/ZoneWorldFishingChampionship.cs) | unverified | unverified | — | `FishingChampionshipManager.java` → `ZoneWorldFishingChampionship.cs`. NPC HTML board, weekly reset, prize via `LedgerTxKind.FishingChampionshipPrize`. The ranking is the NPC page, not `RequestExFishRanking` |
| 101 | Soul crystals (Q350) | ✅ [src](../src/Fineage.Zone/World/ZoneWorldSoulCrystals.cs) | unverified | unverified | — | `Q00350` → `ZoneWorldSoulCrystals` |
| 102 | Shots, arrows, consumables, seeds, merc tickets | ✅ [src](../src/Fineage.Zone/World/ZoneWorldShots.cs) | unverified | unverified | — | `handlers/items/{SoulShots,SpiritShot,EnchantScrolls,Seed,MercTicket,SummonItems}.java` → `ZoneWorldShots`, `ZoneWorldConsumables`, `ZoneWorldPets` |
| 103 | Item-handler tail | ✅ [src](../src/Fineage.Zone/Items/ItemHandlerRegistry.cs) | unverified | unverified | — | `ItemHandlerCatalog` + `ItemHandlerRegistry` → Book/Maps/RollingDice/Calculator/SpecialXMas; Net `Dice`/`ShowMiniMap`/`ShowCalculator`/`ShowXMasSeal` 0x03EB–0x03EE — #925 |
| **Meta** |
| 104 | GM admin commands | 🟡 [src](../src/Fineage.Zone/World/ZoneWorldAdmin.cs) | unverified | unverified | L | `handlers/chat/commands/admin` (77 handlers) → heal/kill/res/delete/announce/online/invul/invis/hide/petition/ride + prior live-ops set in `ZoneWorldAdmin` (#931). Remaining HTML/reload/manor/siege/… still open |
| 105 | Seasonal events | ✅ [src](../src/Fineage.Zone/SeasonalEvents/SeasonalEventHost.cs) | unverified | unverified | — | `LongTimeEvent` / `scripts/events/{Christmas,L2Day,HeavyMedal}` → `SeasonalEventHost` + config.xml windows; missing event data logs event id. Local CT0 has no Christmas folder (logged) — #934 |
| 106 | Event drop windows | ✅ [src](../src/Fineage.Zone/Drops/EventDropCatalog.cs) | unverified | unverified | — | `EventDropManager.java` → `Drops/EventDropCatalog.cs` |
| 107 | Player mail | n/a | n/a | n/a | — | `EX_MAIL_ARRIVED` / `CustomMailManager` are not a Fineage Net target |
| 108 | Mobius-only extras | n/a | n/a | n/a | — | captcha, rebirth, premium, sell-buffs, fake players, offline play, banking voiced, GameGuard, HackShield, GM snoop, Kamael slideshow. Not retail Interlude gameplay |

## Movement A/B — #1051 (scoped)

> Pins for this slice only: Fineage `f5759056` (`origin/dev` at run time) and local Mobius `L2J_Mobius_CT_0_Interlude` `84d79a21`. Other rows were not re-verified against these pins.
>
> This pinned comparison is the parity evidence linked from row 5 only. It does not cover rows 6–7 or any other row.

**Method:** identical 2-D scenarios on a flat `20_18`/`21_19` region (16-unit geo cells, fixed tick, no randomness, no wall-clock measurements), comparing Mobius source behaviour to deterministic Fineage tests. Mobius: `Creature.updatePosition`/`moveToLocation` (`Creature.java:3945`, `:4291`), `MovementTaskManager` (100 ms pools), `GeoEngine.getValidLocation`/`canMoveToTarget` (`GeoEngine.java:431`, `:487`), `DoorData.checkIfDoorsBetween` (`DoorData.java:170`).

| Scenario | Mobius evidence | Fineage before | Fineage now | Test |
|---|---|---|---|---|
| Open field | `distPassed = moveSpeed * (gameTicks - moveTimestamp) / 10`; snap to dest at `distFraction > 1.79` | `step = speed * dt / 1000`; snap when remaining ≤ step | unchanged | `GeoEngineTests.Open_move_reaches_dest_on_ground_z`, `Open_move_path_is_single_leg` |
| Wall / closed door | `getValidLocation` checks doors first (`:441`) and cells next; returns origin/previous cell. `moveToLocation` pathfinds when the straight line lost > 30 units (`:4452`) | walls truncated and pathfound; **doors were ignored by `MoveCheck`**, so `ClipDoors` shortened the leg only after the route was chosen and the rest of the move was dropped — the pawn stopped at a closed door instead of routing around | `MoveCheck(..., DoorTable)` refuses the segment at the origin when a closed door crosses it, taking the existing blocked→A* branch; `CanStep`/`MoveCheck` used by the search reject the same door footprint | `GeoEngineTests.Closed_door_blocks_straight_move_and_path_routes_around_it`, `ZoneDoorMovementTests.Closed_door_on_the_line_queues_a_route_instead_of_stopping_at_the_clip` |
| Open door | `checkIfDoorsBetween` returns false; direct move | direct move | direct move | `GeoEngineTests.Open_door_does_not_change_straight_move_or_path`, `ZoneDoorMovementTests.Open_door_on_the_line_keeps_the_direct_move` |
| Blocked corridor / A* | `PathFinding` over `CellNodeBuffer`, bounded by `MAX_ITERATIONS`; per-tick door check remains (`Creature.java:4069`) | resumable A* on NSWE/`MaxClimb`, budgeted per step | same search plus an optional door predicate; no door-aware rewrite of the algorithm. Doors indexed in 128-unit buckets so no-door regions keep the old hot path | `GeoPathSearchTests`, `ZoneNoRouteCacheTests`, `ZonePathPriorityTests`, `ZonePathfindStarvationTests` |
| Follow offset | `offset -= abs(dz); if (offset < 5) offset = 5; distance -= offset - 5` (`Creature.java:4353-4375`) | `ApproachStopDistance` already reproduces it | unchanged | `NetMeleeApproachTests` |
| Slope | server Z advances from the client `ValidatePosition` Z (`Creature.java:3941-3942`) | server Z snaps to geo ground and interpolates between cells | unchanged — position-consistency scope | — |
| Water | swim pair when `isInsideZone(WATER)` (`CreatureStat.java:484-492`); water legs clamp to 700 and skip geodata (`Creature.java:4323-4335`, `:4394`) | swim pair via `InWater`; **no 700-unit clamp / no water geodata bypass** | unchanged — source diff recorded, not part of the reproduced visible defect | — |

**Root cause of the fixed diff:** door collision lived only in `ClipDoors` after the movement leg was selected; the shared `GeoEngine` path search had no door input. The fix moves door validity into the shared geo segment check (single `DoorTable.Blocks`), leaving the existing post-leg `ClipDoors` and per-tick checks as safety nets for doors that change state mid-move.

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

## Issue #174 tracker status

This is the canonical text to post on issue [#174](https://github.com/FioraSolutions/Fineage-Backend/issues/174)
(the parent/linker posts it; this PR only changes the document):

> **Parity matrix status — 2026-11-27 (#977).** The live home remains
> `docs/mobius-parity-matrix.md`. It now tracks three independent levels instead of one `✅ done`:
> **Implemented** (structural/code) 103 ✅, 2 🟡, 0 ❌, 3 n/a; **Parity verified** (behavioural
> against pinned Mobius) 3 verified, 3 partial, 99 unverified, 3 n/a; **Production verified**
> (production-like run/fault artefact) 0 verified, 105 unverified, 3 n/a. Full parity evidence is the
> pinned movement A/B #1051 slice (row 5), `WeightPenaltyTests` against the Mobius weight-penalty
> formula (row 31) and `DeathClearsAggroTests` against
> `Creature.doDie`/`decayMe`/`Attackable.onSpawn` (row 32); rows 16/17/72 are partial (#979 physical
> auto-attack matrix). `docs/QUEST_STATUS.md` and `docs/wire-coverage.md` counters are implementation
> coverage only, not parity or production evidence. All other rows stay explicitly unverified at the
> higher levels; no unrun check is marked verified.

## Ordered next work

PvE and character leveling first. Siege, olympiad, and clan are last because those stacks are already on `dev`.

1. Area / boss combat AI still outside the script host (row 37 residual after RaidbossInfo) — follow #919 / #805.
2. ~~Book / map / dice / calculator / xmas item handlers (row 103) — #925~~ (done).
3. ~~Shop wear preview (row 48) — #924~~ (done).
4. ~~Macros (row 69) — #927~~ (done).
5. ~~Party matching rooms (row 67) — #928~~ (done).
6. ~~Community board (row 70) — #929~~ (done).
7. ~~Petitions (row 63) — #930~~ (done).
8. ~~GM admin verb tail (row 104) — #931~~ (done). ~~Hero words #932~~ / ~~PC cafe #933~~ (done). ~~Seasonal events #934~~ (done). Forget-recipe #935 (row 54).

Siege, olympiad matches, and clan halls are already on `dev` (rows 92–95, 97) and stay last. #299 (early quests) matches the 344/0/0/0 quest heuristic and should be closed. #296 stays open for private-store UI; manufacture windows already exist on the Unity client.

## Related docs

| Doc | Role |
|-----|------|
| This file | Living feature matrix and the only canonical status home |
| `wire-coverage.md` | Implementation-only packet/handler coverage; never parity/production proof |
| `QUEST_STATUS.md` | Implementation-only quest wiring heuristic; never parity/production proof |
| `mobius-gap-audit-2026-11-22.md` | Previous audit snapshot — do not edit |
| `combat-parity.md` | Combat parity evidence convention (behavioural tests and Mobius references) |
| `testing/economy-fault-injection.md` | Production-verification acceptance bar for economy/durability |
| `skill-effect-coverage-audit.md` | Effect-name catalog |
| `skill-cast-condition-audit.md` | Cast-condition kinds |