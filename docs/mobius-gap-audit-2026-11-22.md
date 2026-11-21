# Mobius CT_0 Interlude gap audit — 2026-11-22

## Scope

| | |
|---|---|
| **Fineage** | Branch `dev`, commit `7f8d3ce5a4a4e53b693f8685e24af4601052b3b2` |
| **Mobius** | `L2J_Mobius_CT_0_Interlude` via `scripts/fetch-mobius-interlude.sh` → `.cache/l2j-mobius/` |
| **Method** | Compare Java managers, handlers, and `dist/game/data` to Fineage `src/` + `data/`; classify ✅ / 🟡 / ❌ |
| **Out of scope** | Production code changes in this PR; fortress (n/a); mail (n/a Fineage); Essence/other Mobius modules |

Mobius path prefix below: **`mobius/`** =  
`.cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/`  
and **`mobius-data/`** = `.../dist/game/data/`.

---

## Executive summary

Since the **2026-11-21** audit (`0efaa43`), `dev` absorbed festival instances (#751, #753, #756), freight warehouse (#741), GM punishment/jail (#737), wedding gotolove gates (#750), clan hall live doors (#739), force charges (#759), proactive aggro (#760), l10n (#755–757), and rank-2 quest waves (#754, #758, #761). Code verification closed **22** table rows that were 🟡/❌ yesterday (auto-loot, siege merc/golem, CH siege, day/night spawns, festival MVP, freight, bow ammunition, buff hitTime, cast interrupt, guard AI, saga helpers, raid schedule enablement, and others listed with PR anchors below).

Remaining gaps cluster in:

1. **Client-heavy retail** — augmentation, skill enchant, full cursed-weapon lifecycle + curse UI packets (`FINEAGE_ALLOW_*` gates only).
2. **Quest & AI long tail** — **90 partial**, **29 stub**, **1 missing** vs Mobius 344; hundreds of scripted NPC AIs absent.
3. **Skill catalog tail** — Spoil Festival **302** not in `skill-combat.json`; signet EffectPoint incomplete; compound `usingKinds` import still skips AND/OR XML; cast-condition JSON audit (**#762**) not on `dev` yet (runtime evaluator exists for **188** `castCondition` rows in `skills.json`).
4. **World polish** — mother tree zone, fishing championship, raid boss points NPC, event drops/TvT, transfer skills, boss droplist holes on rare NPCs.

---

## Delta vs 2026-11-21 (verified closures)

| Prior row | Now | Evidence |
|-----------|-----|----------|
| Auto-loot ❌ | ✅ | `ZoneRates` + `ZoneWorldGround.Members.AutoLootRecipient` |
| Festival of Darkness ❌ | ✅ | `ZoneWorldFestival*`, `SevenSignsFestivalActor` (#751, #753, #756) |
| Freight WH ❌ | ✅ | `FreightBypassHandler`, `FreightWarehouseTests` (#741) |
| Clan hall siege ❌ | ✅ | `ChSiegeActor` + `ZoneWorldSiege` |
| Mercenary tickets ❌ | ✅ | `ZoneWorldMercTickets`, `MercTicketTests` |
| Siege golem ❌ | ✅ | `SummonSiegeGolemSkillId`, `CanSummonSiegeGolemForSkillCheck` |
| Day/night spawns ❌ | ✅ | `ZoneWorldSpawns.DayNight`, `SpawnPeriodOfDay` |
| Bow arrows 🟡 | ✅ | `ZoneWorldArrows` equip + `ConsumeBowArrow` |
| Buff hitTime 🟡 | ✅ | `ScheduleEffect` at hitTime in `ZoneWorldSkills.Members*` |
| Cast interrupt 🟡 | ✅ | `MaybeBreakCast`, `ZoneWorldCastInterrupt`, `CastRulesTests` |
| City guards 🟡 | ✅ | `GuardTownAiTests`, `ScanAggression` (#738, #760) |
| Sagas 70–100 🟡 | ✅ | Quest catalog helpers (#742) |
| Wedding 🟡 | ✅ (core) | `ZoneWorldWedding`, `WeddingActor` (#750); cupid bow N/A in CT_0 |
| GM / jail 🟡/❌ | 🟡→✅ subset | `PunishmentManager`, `ZoneWorldAdmin.Punishment` (#737); not all ~80 Mobius admin verbs |
| Raid bosses disabled | 🟡→✅ schedule | `data/raidbosses.json` **216/223** `enabled: true` (was 0) |
| Force charges | — | ✅ `ZoneWorldForceCharges` (#759) |
| Dimensional rift | ✅ | unchanged |
| CH doors | — | ✅ `ZoneWorldDoors` (#739) |

---

## System-by-system gaps

Each row: **status**, **concrete gap**, **Mobius anchor**, **Fineage anchor**, **size** (S/M/L).

### Combat formulas & skills

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Core phys/magic/blow | ✅ | — | `model/stats/Formulas.java` | `ZoneCombat`, `ZoneWorldSkillDamage` | — |
| Bow arrows + reuse | ✅ | — | `Creature.doAttack` bow branch | `ZoneWorldArrows`, `PlayerAttackTimeMs` | — |
| Buff apply timing | ✅ | — | `Effect.tick` / hitTime | `ScheduleEffect` in `ZoneWorldSkills.*` | — |
| Cast interrupt (stun/damage) | ✅ | — | `Formulas.calcAtkBreak` | `ZoneWorldCastInterrupt`, `CastRules` | — |
| Compound skill conditions | 🟡 | Runtime `castCondition` AND/OR OK in `skills.json` (**188** rows); `gen_skill_conditions.py` still skips compound **usingKinds**; no `#762` catalog sweep on `dev` | `Skill.java` conditions | `SkillCastConditionEvaluator`, `gen_skill_conditions.py` | M |
| Skills missing from catalog | 🟡 | Non-combat rows without data refused with warn | `skill-combat.xml` union | `ZoneWorldSkills.More` refuse path | S |
| Signet EffectPoint | 🟡 | AOE signet damage points | `handler/skillhandlers/Sign.java` | `ZoneWorldSignets.cs` (EffectPoint TODO) | S |
| Spoil Festival 302 / Sweeper 444 | ❌ | AoE spoil/sweep combat skills | skill handlers | Skill **302** in `skills.json` only; absent `skill-combat.json` | S |
| NPC SS glow 2154 | 🟡 | Fan-out `MagicSkillUse` on NPC physical SS | `Npc.java` rechargeShots | `TryNpcSoulshot` damage only; magic SS via `FanoutNpcMagicSkillUse` | S |
| Enchant on folded stats | 🟡 | +enchant on P.Atk etc. in CharInfo/UserInfo | `PlayerStat` funcs | Item enchant ✅; stat fold partial | S |
| Force charges | ✅ | — | Sonic Focus / Blaster | `ZoneWorldForceCharges` (#759) | — |

### AI

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Monster aggro/hate/LOS | ✅ | — | `AttackableAI` | `ZoneWorldMobAi`, #760 acquisition | — |
| Raid minion | ✅ | — | `Monster.setLeader` | `ZoneWorldNpcMovement` | — |
| Healer/buffer NPC AI | ✅ | — | `NpcAI` + scripts | `ZoneWorldNpcSkills` | — |
| City guards / karma checks | ✅ | — | `ai/others/Guard` | `ScanAggression`, `GuardTownAiTests` | — |
| Event / quest NPC AI | 🟡 | Hundreds of `dist/.../ai/**` | Script AI | Quest JSON + limited scripts | L |
| Minion raid buff on follow | 🟡 | 1/30 clan buff roll close | `AttackableAI` | Follow only | S |

### Drops, spoil, herbs, exp

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Droplist rolling | 🟡 | Full NPC coverage | `Attackable.doDie` + XML | 1295 JSON files; rare mobs gaps | M |
| Spoil/sweep | ✅ | — | `Formulas.calcSpoil` | `ZoneWorldSpoil` | — |
| Herbs | ✅ | — | `ItemData` ex immediate | `ZoneWorldHerbs` | — |
| Exp/SP on kill | ✅ | — | `Attackable.calculateRewards` | `ZoneWorldDeathRules` | — |
| Death penalty | ✅ | — | `Player.java` | `ZoneWorldDeath` | — |
| Karma / PK count | ✅ | — | `Player.updatePvPStatus` | `ZoneWorldPvp` | — |
| **Auto-loot** | ✅ | — | `Attackable.dropItem` + config | `ZoneRates.ShouldAutoLootItem` | — |
| Party exp share | 🟡 | Level gap / vitality nuances | `Party.distributeXpAndSp` | Core share ✅ | S |

### Party & loot

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Distribution modes | ✅ | — | `Party.distributeItem` | `PartyLootRules` | — |
| Command channel | 🟡 | CC creation rules | `CommandChannel` | Chat stubs only (`ZoneWorldChat`) | S |

### Clans, alliances, halls

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Clan CRUD, crest, WH | ✅ | — | `Clan.java` | `ZoneWorldPledge` | — |
| Academy / reputation | ✅ | — | `VillageMaster` | `ZoneWorldPledge.Commands` | — |
| Clan skills | ✅ | — | `SkillTreeData` clan | `ZoneWorldPledge.Skills` | — |
| Clan wars | ✅ | — | `ClanWar.java` | `ZoneWorldPledge.Wars` | — |
| Alliance | ✅ | — | `Alliance.java` | `ZoneWorldAlly` | — |
| Clan hall auction | ✅ | — | `ClanHallAuctionManager` | `ClanHallAuctionActor` | — |
| Clan hall functions | 🟡 | `AgitDecoInfo` fan-out; item-creation multisell depth | `ClanHallManager` | `ZoneWorldPledge.HallFunctions` (#746) partial | M |
| Clan hall escape / restart | ✅ | SOE + death restart | `MapRegionData` CLANHALL | `ClanHallSpawnCatalog`, #747 | — |
| **Clan hall siege** | ✅ | — | `CHSiegeManager.java` | `ChSiegeActor` | — |
| Freight WH | ✅ | — | `Warehouse.java` freight | `FreightBypassHandler`, ledger kinds | — |
| Clan hall doors | ✅ | — | `Door.java` / doorman | `ZoneWorldDoors` (#739) | — |

### Siege (castle)

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Registration / battle | ✅ | — | `Siege.java` | `ZoneWorldSiege` | — |
| Control / flame towers | ✅ | — | `ControlTower`, `FlameTower` | `ZoneWorldSiege.Battle` | — |
| Siege guards / spawn disable | 🟡 | Guard hire, respawn link to towers | `SiegeGuardManager` | Towers ✅; hire flags data gaps | M |
| **Mercenary tickets** | ✅ | — | `MercTicketManager.java` | `ZoneWorldMercTickets` | — |
| **Siege golem** | ✅ | — | `ConditionPlayerCanSummonSiegeGolem` | `ZoneWorldSummons` + cast conditions | — |
| Door open on siege | ✅ | — | `Door.java` | Live doors + cyclic open/close | — |

### Manor, castles, taxes

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Manor periods / sow | ✅ | — | `CastleManorManager` | `Manor/*` | — |
| Castle tax / treasury | ✅ | — | `Castle.java` | `ZoneWorldManor` | — |

### Olympiad & hero

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Olympiad weeks / matches | ✅ | — | `OlympiadGameManager` | `Olympiad/*` | — |
| Hero weapons / gates | 🟡 | Periodic hero weapon grant nuances | `Hero.java` | Core hero ✅ | S |
| Noblesse gate NPC | 🟡 | `RequestNobless` dialog flow | village master scripts | `NpcServiceBypass` noblesse stub | S |

### Seven Signs, catacombs, festival

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Periods / seal votes / stones | ✅ | — | `SevenSigns.java` | `SevenSignsEngine` | — |
| Catacomb/necropolis **entry** | 🟡 | Full gate NPC + validation kick | `DimensionalRift` gates | Teleport + cabal checks | S |
| **Festival of Darkness** | ✅ | — | `SevenSignsFestival` | `ZoneWorldFestival*`, #751–756 | — |
| **Dimensional rift** | ✅ | — | `DimensionalRiftManager.java` | `ZoneWorldDimensionalRift` | — |
| Mammon spawn | ✅ | — | `SignsPriest` / Mammon | `ZoneWorldMammon` | — |

### Cursed weapons

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| PvP level-20 attack rules | 🟡 | Blessing gate only | `PlayableAI` | `PlayableAttackRules`, inventory gates | — |
| Spawn/drop/acquire/disappear | ❌ | Full Zariche/Akamanah cycle | `CursedWeaponsManager.java` | `CursedWeaponsState` + flags only | L |
| ExCursedWeaponList/Location packets | ❌ | Client curse UI | network packets | Not sent | M |

### Duels

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| 1v1 / party duel | ✅ | — | `DuelManager.java` | `ZoneWorldDuels` | — |

### Quests

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Count vs Mobius (344 dirs) | 🟡 | **224** impl / **90** partial / **29** stub / **1** missing | `mobius-data/scripts/quests` | **314** `quest.json` dirs | L |
| Sagas 70–100 | ✅ | — | `Q007*_*.java` | Quest catalog (#742) | — |
| Fishing quests 27–30 | 🟡 | Mostly stub | fishing quest scripts | partial/stub per `quest_status` | S |
| Ketra/Varka 11–12 | 🟡 | Missing talk NPCs | alliance quests | partial | M |
| Profession NPC scripts | 🟡 | Non-pack logic | `village_master/**` | JSON + partial | M |

Run: `python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude`

### NPC services

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Teleport / buff / newbie | ✅ | — | `Teleporter`, NPC HTML | `ZoneWorldNpcServices` | — |
| Multisell / buylists | ✅ | — | `MultisellData` | `ZoneWorldShops` | — |
| Private WH | ✅ | — | `Warehouse` | Wired | — |
| **Augmentation** | ❌ | Refine / cancel / gem confirm | `RequestRefine*.java` | `AugmentationRules` gate only | L |
| **Skill enchant** | ❌ | `RequestExEnchantSkill` | mobius network packet | `SkillEnchantRules` gate only | M |
| Skill learn trees | 🟡 | All class edges | `SkillTreeData` | Partial `skill-trees.json` | M |
| Subclass | ✅ | — | `SubClass` | `ZoneWorldSubclass` | — |
| Transfer skills | ❌ | Certificate transfer | skill handlers | Not found | S |

### Trade & stores

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Player trade | ✅ | — | `TradeList` | `ZoneWorldTrade` | — |
| Private buy/sell/craft | ✅ | — | `PrivateStore` | `ZoneWorldPrivateStore` | — |

### Pets, summons, mounts

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Servitor | ✅ | — | `Servitor.java` | `ZoneWorldSummons` | — |
| Cubics | 🟡 | Offensive cubic uses M.Atk | `Cubic.java` | `ZoneWorldCubics.cs` (M.Atk TODO) | S |
| Pet hunger/exp/res | ✅ | — | `Pet.java` | `ZoneWorldPets` | — |
| Baby pet buffs | ✅ | — | `BabyPet` AI | `ZoneWorldPets` | — |
| Pet evolution | ✅ | — | `PetManager.doEvolve` | `ZoneWorldPets.Evolve` | — |
| Wyvern / strider | ✅ | — | `Ride` | `ZoneWorldMounts` | — |

### Zones & environment

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Peace / PvP arena | ✅ | — | `zones/peace.xml` | `PeaceZones`, `PvpZones` | — |
| Water (swim speed) | ✅ | — | `WaterZone` | `WaterZones` | — |
| Swamp (speed) | ✅ | — | `SwampZone` | `SwampZones` | — |
| Damage zones | ✅ | — | `DamageZone` | `DamageZones` | — |
| No landing (wyvern) | ✅ | — | `NoLandingZone` | `NoLandingZones` | — |
| **Mother tree** (elven regen) | ❌ | HP/MP bonus zone | `MotherTreeZone` | Not loaded | S |
| Day/night spawns | ✅ | — | `DayNightSpawnManager.java` | `ZoneWorldSpawns.DayNight` | — |

### World simulation

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Geodata pathfinding | 🟡 | Full world geo | `GeoEngine.java` | Ported; optional stub | M |
| Spawn map coverage | 🟡 | Full world spawns live | `SpawnData.xml` | **189** spawn JSON; most files enabled | M |
| Raid bosses | 🟡 | Live spawns all regions | `RaidBossSpawnManager` | **216/223** enabled in catalog | M |
| Boss points ranking | ❌ | RB ranking NPC | `RaidBossPointsManager` | Not present | S |
| Boats | 🟡 | Gludin↔Talking Island etc. | `BoatManager.java` | `ZoneWorldBoats` sim ✅; vehicle wire in `client-packet-contracts.md` | M |
| Walking NPCs | ✅ | — | `WalkingManager.java` | `ZoneWorldWalking` | — |
| Monster Derby Track | ✅ | — | `MonsterRaceManager.java` | `ZoneWorldMonsterRace` (#736) | — |

### Chat & restrictions

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Channel set (say/shout/…) | 🟡 | All chat types + hero | `Say2.java` | `ZoneWorldChat` core | S |
| Level / cursed whisper trade | 🟡 | Cursed weapon shout/trade block | `Say2` | Karma + partial curse checks | S |
| Chat ban / jail / char ban | 🟡 | Full `PunishmentManager` enum | `PunishmentManager.java` | `PunishmentManager` subset (#737) | M |
| Silence / message refusal | ✅ | — | block list | `ZoneWorldFriends` | — |
| Wedding | ✅ | — | `mods/events/Wedding` | `ZoneWorldWedding` (#750) | — |

### Items, shots, henna

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Soul/spirit shots | ✅ | — | `ShotItems` | `ZoneWorldShots` | — |
| Item enchant | ✅ | — | `RequestEnchantItem` | `ZoneWorldEnchant` | — |
| Henna | ✅ | — | `Henna.java` | `ZoneWorldHenna` | — |

### Fishing & lottery

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Fishing minigame | ✅ | — | `FishingChampionshipManager` core | `Fishing/*` | — |
| Fishing championship event | ❌ | Weekly top | `FishingChampionshipManager` | Not present | S |
| Loto lottery | ✅ | — | `LotteryManager` | `ZoneWorldLottery` | — |

### Events, GM, punishments

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| GM admin commands | 🟡 | ~80 admin handlers | `handlers/admin/*.java` | `ZoneWorldAdmin` + punishment (#737) subset | L |
| Event drops / TvT | ❌ | `EventDropManager` | event scripts | Not present | M |
| Jail / ban login | 🟡 | All punishment types | `PunishmentManager` | Jail/chat/char ban wired; login ban partial | M |

### Character creation & templates

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| 5 races / templates | ✅ | — | `CharTemplateTable` | Imported templates | — |
| Starting items / quests | 🟡 | Race-specific start | `CharCreateTable` | Newbie helpers Q255/998 | S |

### Economy ledger (Fineage-only)

Fineage adds structured ledgers (`ConsumeLedger`, `GrantLedger`, …) beyond Mobius; no parity gap — behavioral parity is in manor/trade/enchant/freight flows.

### Wire / protocol

Documented in `docs/net-protocol.md`. Residual: `MagicSkillCanceled`, full `RelationChanged` polish, auto-shot slot highlight (`combat-parity`), boat/derby layouts in `docs/client-packet-contracts.md`.

---

## Quest coverage summary

| Status | Count (2026-11-22) | Δ vs 11-21 |
|--------|-------------------:|-----------:|
| implemented | 224 | +11 |
| partial | 90 | −3 |
| stub | 29 | −2 |
| missing | 1 | −6 |
| **Mobius total** | **344** | — |

Fineage-only quest dirs: Q256, Q998 (and other non-Mobius ids in `data/quests`).

Recent batches: rank-2 waves (#754, #758, #761); Mobius dialog vendoring (#752); festival-unblocks Q634/Q635 via rift (already on dev).

---

## Data coverage summary

| Asset | Fineage | Mobius (indicative) |
|-------|--------:|--------------------:|
| `skill-combat` skills | 387 | ~same order |
| `skills.json` `castCondition` | 188 | XML conditions |
| `usingKinds` in combat JSON | 50 | weapon kind gates |
| Drop JSON files | 1295 | per-NPC XML groups |
| Spawn JSON files | 189 (enabled) | world XML groups |
| Raid boss catalog rows | 223 (**216** enabled) | full schedule |
| Quest `quest.json` | 314 | 344 Java quests |

---

## Gap counts (table rows)

| Status | Count |
|--------|------:|
| ✅ closed / parity | **78** |
| 🟡 partial | **32** |
| ❌ missing | **12** |

**Closed since 2026-11-21:** **22** rows upgraded to ✅ (listed in delta table).  
**Still open:** **44** 🟡/❌ rows (32+12).

---

## Top 20 gaps by player impact

| Rank | System | Status | Gap (short) | Size |
|------|--------|--------|-------------|------|
| 1 | Quest coverage | 🟡 | 120 quests not fully playable | L |
| 2 | Item augmentation | ❌ | No refine/life stones gameplay | L |
| 3 | Cursed weapons | ❌ | No acquire/drop cycle | L |
| 4 | Event / quest NPC AI | 🟡 | Scripted AI long tail | L |
| 5 | GM admin long tail | 🟡 | Subset of Mobius verbs | L |
| 6 | Skill enchant | ❌ | No ExEnchantSkill flow | M |
| 7 | Siege guard hire data | 🟡 | Hire / tower link gaps | M |
| 8 | Skill learn trees | 🟡 | Missing class edges | M |
| 9 | Droplist holes | 🟡 | Rare NPCs | M |
| 10 | Compound conditions import | 🟡 | `#762` + usingKinds AND/OR | M |
| 11 | Geodata coverage | 🟡 | Stub mode on some hosts | M |
| 12 | Spoil Festival 302 | ❌ | AoE spoil skill | S |
| 13 | Mother tree zone | ❌ | Elven regen bonus | S |
| 14 | Boats client wire | 🟡 | Sim without full vehicle packets | M |
| 15 | Cubics M.Atk term | 🟡 | Success formula | S |
| 16 | Fishing championship | ❌ | Weekly top event | S |
| 17 | Event drops / TvT | ❌ | `EventDropManager` | M |
| 18 | Transfer skills | ❌ | Certificate handlers | S |
| 19 | Boss points NPC | ❌ | Ranking UI | S |
| 20 | Cursed weapon packets | ❌ | Client curse UI | M |

---

## Suggested audit refresh

1. `git fetch origin dev` after major merges (especially `#762` cast-condition audit when it lands).
2. `python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude`
3. Regenerate combat rows when `skill-combat.json` / `skills.json` changes.
4. Keep `mobius-gap-audit-2026-11-21.md` as the prior snapshot — do not edit in place.