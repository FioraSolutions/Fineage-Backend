# Mobius CT_0 Interlude gap audit — 2026-11-21

## Scope

| | |
|---|---|
| **Fineage** | Branch `dev`, commit `0efaa43eceded815e2e1307d6094a099150eb40f` |
| **Mobius** | `L2J_Mobius_CT_0_Interlude` via `scripts/fetch-mobius-interlude.sh` → `.cache/l2j-mobius/` |
| **Method** | Compare Java managers, handlers, and `dist/game/data` to Fineage `src/` + `data/`; classify ✅ / 🟡 / ❌ |
| **Out of scope** | Production code changes; fortress (n/a); mail (n/a Fineage); Essence/other Mobius modules |

Mobius path prefix below: **`mobius/`** =  
`.cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/`  
and **`mobius-data/`** = `.../dist/game/data/`.

---

## Executive summary

Between the stale **2026-11-06** matrix and this audit, Fineage landed most **Interlude-sized gameplay loops** on the Net zone architecture: combat formulas, spoil/sweep, pledge (clan skills, wars, halls, WH), castle siege **core**, manor, olympiad, Seven Signs **calendar**, fishing, lottery, Mammon, pets/servitors/cubics/mounts, PvP/karma, raid loot/curse, and **213/344** Mobius quest scripts at **implemented** status.

Remaining gaps are concentrated in five themes:

1. **World breadth** — day/night spawns, boats, walking NPCs, most raid/spawn maps disabled, swamp/damage/no-landing zones.
2. **Retail item systems** — augmentation, skill enchant, auto-loot, cursed-weapon lifecycle.
3. **Siege completeness** — mercenary tickets, siege golem, clan-hall siege; door/siege timers.
4. **Seven Signs instances** — Festival of Darkness, dimensional rift (score hooks exist; instances do not).
5. **Quest & AI long tail** — 131 quests not fully implemented; guard/special NPC scripts; GM/event tooling.

---

## System-by-system gaps

Each row: **status**, **concrete gap** (what Mobius does that Fineage does not, with Mobius anchor), **Fineage anchor**, **size**.

### Combat formulas & skills

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Core phys/magic/blow | ✅ | — | `model/stats/Formulas.java` | `ZoneCombat`, `ZoneWorldSkillDamage` | — |
| Bow arrows + reuse | 🟡 | No arrow delete / bow reuse `1500*345/Pspd` | `Creature.doAttack` bow branch | `ZoneWorldArrows` (partial) | S |
| Buff apply timing | 🟡 | Effects at hitTime for buffs/debuffs | `Effect.tick` / `CreatureAI.thinkCast` | Instant at cast start (`ZoneWorldSkills.Members`) | M |
| Cast interrupt (stun/damage) | 🟡 | Damage can break cast channel | `Formulas.calcAtkBreak` | Not wired | M |
| Compound skill conditions | 🟡 | AND/OR weapon/hp/zone conditions | `Skill.java` conditions | `gen_skill_conditions.py` skips compounds | M |
| Skills missing from catalog | 🟡 | Non-combat / support skills without row refused | `skill-combat.xml` union | Warn + refuse (`ZoneWorldSkills.Members`) | S |
| Signet EffectPoint | 🟡 | AOE signet damage points | `handler/skillhandlers/Sign.java` | `ZoneWorldSignets.cs` TODO | S |
| Spoil Festival 302 / Sweeper 444 | ❌ | AoE spoil/sweep | skill handlers | Not present | S |
| NPC SS glow 2154 | 🟡 | Fan-out MagicSkillUse on NPC SS | `Npc.java` | Melee SS only | S |
| Enchant on folded stats | 🟡 | +enchant on P.Atk etc. in UserInfo | `PlayerStat` funcs | Item enchant ✅; stat fold partial | S |

### AI

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Monster aggro/hate/LOS | ✅ | — | `AttackableAI` | `ZoneWorldMobAi` | — |
| Raid minion | ✅ | — | `Monster.setLeader` | `ZoneWorldNpcMovement` | — |
| Healer/buffer NPC AI | ✅ | — | `NpcAI` + scripts | `ZoneWorldNpcSkills` | — |
| City guards / karma checks | 🟡 | Guards attack PK; scan aggressive mobs when enabled; ally hate on hit | `ai/others/Guard`, `SiegeGuard` | Karma scan in `ScanAggression`; `CallGuardHelp`; `GuardTownAiTests` | M |
| Event / quest NPC AI | 🟡 | Hundreds of `dist/.../ai/**` | Script AI | Quest pack + limited scripts | L |
| Minion raid buff on follow | 🟡 | 1/30 clan buff roll close | `AttackableAI` | Follow only | S |

### Drops, spoil, herbs, exp

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Droplist rolling | 🟡 | Full NPC coverage | `Attackable.doDie` + XML | 1295 JSON files; gaps on rare mobs | M |
| Spoil/sweep | ✅ | — | `Formulas.calcSpoil` | `ZoneWorldSpoil` | — |
| Herbs | ✅ | — | `ItemData` ex immediate | `ZoneWorldHerbs` | — |
| Exp/SP on kill | ✅ | — | `Attackable.calculateRewards` | `ZoneWorldDeathRules` | — |
| Death penalty | ✅ | — | `Player.java` | `ZoneWorldDeath` | — |
| Karma / PK count | ✅ | — | `Player.updatePvPStatus` | `ZoneWorldPvp` | — |
| **Auto-loot** | ❌ | AUTO_LOOT, AUTO_LOOT_RAIDS, herbs | `Attackable.dropItem` | Always ground drop | M |
| Party exp share | 🟡 | Level gap / vitality nuances | `Party.distributeXpAndSp` | Core share ✅ | S |

### Party & loot

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Distribution modes | ✅ | — | `Party.distributeItem` | `PartyLootRules` | — |
| Command channel | 🟡 | CC creation rules | `CommandChannel` | Opcode stubs | S |

### Clans, alliances, halls

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Clan CRUD, crest, WH | ✅ | — | `Clan.java` | `ZoneWorldPledge` | — |
| Academy / reputation | ✅ | — | `VillageMaster` | `ZoneWorldPledge.Commands` | — |
| Clan skills | ✅ | — | `SkillTreeData` clan | `ZoneWorldPledge.Skills` | — |
| Clan wars | ✅ | — | `ClanWar.java` | `ZoneWorldPledge.Wars` | — |
| Alliance | ✅ | — | `Alliance.java` | `ZoneWorldAlly` | — |
| Clan hall auction | ✅ | — | `ClanHallAuctionManager` | `ClanHallAuctionActor` | — |
| Clan hall functions | 🟡 | `AgitDecoInfo` fan-out TBD | `ClanHallManager` | `ZoneWorldPledge.HallFunctions`, `HallFunctionActor` | — |
| **Clan hall siege** | ❌ | CH siege cycles | `CHSiegeManager.java` | Auction only | M |
| Freight WH | ❌ | Account freight deposit | `Warehouse.java` freight type | Stub bypass | S |

### Siege (castle)

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Registration / battle | ✅ | — | `Siege.java` | `ZoneWorldSiege` | — |
| Control / flame towers | ✅ | — | `ControlTower`, `FlameTower` | `ZoneWorldSiege.Battle` | — |
| Siege guards / spawn disable | 🟡 | Guard hire, respawn link to towers | `SiegeGuardManager` | Towers ✅; hire flags `isHired=0` data | M |
| **Mercenary tickets** | ❌ | Buy/deploy castle mercs | `MercTicketManager.java` | No matches in `src/` | M |
| **Siege golem** | ❌ | Noble skill summon golem in siege | `ConditionPlayerCanSummonSiegeGolem` | `ZoneWorldSummons` generic only | M |
| Door open on siege | 🟢 | — | `Door.java` `OPEN_BY_TIME` | Live Net doors + siege/CH respawn + doorman + cyclic `openTime`/`closeTime` tick | — |

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
| **Festival of Darkness** | ❌ | Instance rooms, scores, rewards | `SevenSignsFestival` | `ISevenSignsFestivalHook` only | L |
| **Dimensional rift** | ✅ | Rift rooms, jumps, rewards | `DimensionalRiftManager.java` | `ZoneWorldDimensionalRift` | L |
| Mammon spawn | ✅ | — | `SignsPriest` / Mammon | `ZoneWorldMammon` | — |

### Cursed weapons

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| PvP level-20 attack rules | 🟡 | Blessing gate only | `PlayableAI` | `PlayableAttackRules` | — |
| Spawn/drop/acquire/disappear | ❌ | Full Zariche/Akamanah cycle | `CursedWeaponsManager.java` | Item IDs in passives only | L |
| ExCursedWeaponList/Location packets | ❌ | Client curse UI | network packets | Not sent | M |

### Duels

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| 1v1 / party duel | ✅ | — | `DuelManager.java` | `ZoneWorldDuels` | — |

### Quests

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Count vs Mobius (344 dirs) | 🟡 | 213 impl / 93 partial / 31 stub / 7 missing | `mobius-data/scripts/quests` | `data/quests` (308 `quest.json`) | L |
| Sagas 70–100 | 🟡 | Helper skill casts, NPC timers | `Q007*_*.java` | `QuestCatalog.Sagas` simplified | M |
| Fishing quests 27–30 | 🟡 | Mostly stub | fishing quest scripts | stub in `QUEST_STATUS` | S |
| Ketra/Varka 11–12 | 🟡 | Missing talk NPCs | alliance quests | partial | M |
| Profession NPC scripts | 🟡 | Non-pack logic | `village_master/**` | JSON + partial | M |

Run: `python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude`

### NPC services

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Teleport / buff / newbie | ✅ | — | `Teleporter`, NPC HTML | `ZoneWorldNpcServices` | — |
| Multisell / buylists | ✅ | — | `MultisellData` | `ZoneWorldShops` | — |
| Private WH | ✅ | — | `Warehouse` | Wired | — |
| **Augmentation** | ❌ | Refine / cancel / gem confirm | `RequestRefine*.java`, `AugmentationData.java` | No `src/` implementation | L |
| **Skill enchant** | ❌ | `RequestExEnchantSkill` | mobius network packet | Comment in `AccountModels` only | M |
| Skill learn trees | 🟡 | All class edges | `SkillTreeData` | Partial catalogs | M |
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
| Cubics | 🟡 | Offensive cubic uses M.Atk | `Cubic.java` | TODO in `ZoneWorldCubics` | S |
| Pet hunger/exp/res | ✅ | — | `Pet.java` | `ZoneWorldPets` | — |
| **Baby pet buffs** | ✅ | Auto-cast buffs when fed | `BabyPet` AI | `ZoneWorldPets` + skill-effects | — |
| **Pet evolution** | ✅ | Wolf → strider, multisell hatchling | `PetManager.doEvolve` | `ZoneWorldPets.Evolve` + bypass | — |
| Wyvern / strider | ✅ | — | `Ride` | `ZoneWorldMounts` | — |

### Zones & environment

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Peace / PvP arena | ✅ | — | `zones/peace.xml`, arena | `PeaceZones`, `PvpZones` | — |
| Water (swim speed) | ✅ | — | `WaterZone` | `WaterZones` | — |
| **Swamp** (speed) | ✅ | `SwampZone` move_bonus | `mobius-data/zones/swamp.xml` | `SwampZones` + speed fold | — |
| **Damage** zones | ✅ | Periodic HP/MP | `DamageZone` | `DamageZones` + sim tick | — |
| **No landing** (wyvern) | ✅ | Wyvern dismount timer | `NoLandingZone` | `NoLandingZones` + mount hook | — |
| **Mother tree** (elven regen) | ❌ | HP/MP bonus zone | `MotherTreeZone` | Not loaded | S |
| Day/night spawns | ❌ | Swap spawn groups at game hour | `DayNightSpawnManager.java` | Not present | M |

### World simulation

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Geodata pathfinding | 🟡 | Full world geo | `GeoEngine.java` | Ported; optional stub | M |
| **Spawn map coverage** | 🟡 | Full world spawns | `SpawnData.xml` | 189 JSON; TI focus | L |
| Raid bosses | 🟡 | Live spawns all regions | `RaidBossSpawnManager` | 223 rows, all `enabled: false` OOT | L |
| Boss points ranking | ❌ | RB ranking NPC | `RaidBossPointsManager` | Not present | S |
| Boats | ❌ | Gludin↔Talking Island etc. | `BoatManager.java` | Not present | M |
| Walking NPCs | ❌ | Patrolling merchants | `WalkingManager.java` | Not present | S |
| **Monster Derby Track** | ✅ | — | `MonsterRaceManager.java`, Race Manager NPC | `MonsterRace/*`, `ZoneWorldMonsterRace`, `data/monster-race.json` | — |

### Chat & restrictions

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Channel set (say/shout/…) | 🟡 | All chat types + hero | `Say2.java` | `ZoneWorldChat` core | S |
| Level / cursed whisper trade | 🟡 | Cursed weapon shout/trade block | `Say2` | Karma + partial curse checks | S |
| Chat ban | 🟡 | Full `PunishmentManager` | `PunishmentManager.java` | Chat ban field only | M |
| Silence / message refusal | ✅ | — | block list | `ZoneWorldFriends` | — |
| Wedding (custom) | 🟡 | Cupid bow N/A in CT_0 script; jail/festival/rift gotolove gates | `mods/events/Wedding`, `CoupleManager` | `ZoneWorldWedding`, `docs/wedding-v2-protocol.md` | S |

### Items, shots, henna

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Soul/spirit shots | ✅ | — | `ShotItems` | `ZoneWorldShots` | — |
| Item enchant | ✅ | — | `RequestEnchantItem` | `ZoneWorldEnchant` | — |
| Henna | ✅ | — | `Henna.java` | `ZoneWorldHenna` | — |
| Arrows | 🟡 | See combat | — | — | S |

### Fishing & lottery

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| Fishing minigame | ✅ | — | `FishingChampionshipManager` core | `Fishing/*` | — |
| Fishing championship event | ❌ | Weekly top | `FishingChampionshipManager` | Not present | S |
| Loto lottery | ✅ | — | `LotteryManager` | `ZoneWorldLottery` | — |

### Events, GM, punishments

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| GM admin commands | 🟡 | ~80 admin handlers | `handlers/admin/*.java` | `ZoneWorldAdmin` subset | L |
| Event drops / TvT | ❌ | `EventDropManager` | event scripts | Not present | M |
| Jail / ban login / char ban | ❌ | `PunishmentManager` types | full enum | Chat ban only | M |

### Character creation & templates

| System | Status | Gap | Mobius | Fineage | Size |
|--------|--------|-----|--------|---------|------|
| 5 races / templates | ✅ | — | `CharTemplateTable` | Imported templates | — |
| Starting items / quests | 🟡 | Race-specific start | `CharCreateTable` | Newbie helpers Q255/998 | S |

### Economy ledger (Fineage-only)

Fineage adds structured ledgers (`ConsumeLedger`, `GrantLedger`, …) beyond Mobius; no parity gap — behavioral parity is in manor/trade/enchant flows.

### Wire / protocol

Documented in `docs/net-protocol.md`. **Blocked-feature wire layouts** (boats, derby): `docs/client-packet-contracts.md`. Residual gaps: `MagicSkillCanceled`, full `RelationChanged` polish, auto-shot slot highlight (`combat-parity`).

---

## Quest coverage summary

| Status | Count |
|--------|------:|
| implemented | 213 |
| partial | 93 |
| stub | 31 |
| missing | 7 |
| **Mobius total** | **344** |

Fineage-only: Q256, Q998.

Rank-2 quest batch (PR 1): Q385, Q426, Q505, Q646 — generator `scripts/gen_quests_rank2_missing.py`. Batch 2: Q50–Q53 bait + Q27–Q30 chest chain — `scripts/gen_quests_rank2_fishing.py` (bait prerequisite chain not JSON-gated). Q634/Q635 ship with dimensional rift (#7); Q255 skipped (Fineage Q998).

---

## Data coverage summary

| Asset | Fineage | Mobius (indicative) |
|-------|--------:|--------------------:|
| `skill-combat` skills | 387 | ~same order (CT0 attack skills) |
| `skill-stat-effects` | 372 skills | XML stat funcs |
| `skill-passives` | 166 | operateType P |
| Drop JSON files | 1295 | per-NPC XML groups |
| Spawn JSON files | 189 | world XML groups |
| Raid boss catalog rows | 223 (0 enabled OOT) | full schedule |
| Quest `quest.json` | 308 | 344 Java quests |

---

## Top 20 gaps by player impact

Ordered for a **live Interlude shard** (leveling → gear → economy → endgame). Impact = how many players hit the gap during normal play.

| Rank | System | Status | Gap (short) | Mobius anchor | Size |
|------|--------|--------|-------------|---------------|------|
| 1 | World spawns / hunting grounds | 🟡 | Most regions/RB disabled | `SpawnData.xml`, `RaidBossSpawnManager` | L |
| 2 | Quest coverage | 🟡 | 124 quests not fully playable | `mobius-data/scripts/quests/**` | L |
| 3 | Item augmentation | ❌ | No refine/life stones | `AugmentationData.java` | L |
| 4 | Cursed weapons | ❌ | No acquire/drop cycle | `CursedWeaponsManager.java` | L |
| 5 | Auto-loot | ❌ | All drops to ground | `Attackable.java` + `PlayerConfig` | M |
| 6 | Seven Signs Festival | ❌ | No instance / rewards | `SevenSignsFestival` | L |
| 7 | Dimensional rift | ✅ | Instances + Q634/Q635 | `DimensionalRiftManager.java` | L |
| 8 | Siege mercenaries | ❌ | No ticket deploy | `MercTicketManager.java` | M |
| 9 | Siege golem (noble) | ❌ | No summon siege golem | `ConditionPlayerCanSummonSiegeGolem` | M |
| 10 | Skill enchant (+skills) | ❌ | No ExEnchantSkill | `RequestExEnchantSkill.java` | M |
| 11 | Day/night spawns | ❌ | No night mobs | `DayNightSpawnManager.java` | M |
| 12 | Clan hall siege | ❌ | Auction without siege | `CHSiegeManager.java` | M |
| 13 | Baby pet / evolution | ❌ | Pet meta incomplete | `Pet.java`, `BabyPet` | M |
| 14 | Guard / town AI | 🟡 | No PK guard behavior | `ai/others/Guard` | M |
| 15 | Zone types (swamp/damage/NL) | ❌ | Missing movement/regen rules | `zones/*.xml` | M |
| 16 | Boats + walking NPCs | ❌ | No immersion routes | `BoatManager`, `WalkingManager` | M |
| 17 | Saga quest fidelity | 🟡 | No helper casts/timers | `Q0070–Q0100` | M |
| 18 | GM / punishment tooling | 🟡 | Thin admin + no jail | `handlers/admin`, `PunishmentManager` | M |
| 19 | Compound skill conditions | 🟡 | Wrong refuse on edge skills | `Skill` conditions | M |
| 20 | Freight warehouse | ❌ | Cannot ship items | `Warehouse` freight | S |

### Top 20 implementation status

| Rank | Item | PR(s) | Status | Client work needed |
|------|------|-------|--------|-------------------|
| 1 | World spawns / hunting grounds | #701 | done | No |
| 2 | Quest coverage | #709, #717, #718, #720 | partial — +16 hand-ported; Q634/635 blocked (rift); Q255→Q998; ~86 partial remain | No |
| 3 | Item augmentation | #745 | server gates + packet doc (`FINEAGE_ALLOW_AUGMENTATION`); refine UI/data TBD | Yes |
| 4 | Cursed weapons | #745 | server gates + packet doc (`CursedWeaponsState`); full cycle TBD | Yes |
| 5 | Auto-loot | #719 | done | No (`FINEAGE_AUTO_LOOT*`) |
| 6 | Seven Signs Festival | — | blocked | No (instances) |
| 7 | Dimensional rift | — | done | No |
| 8 | Siege mercenaries | #722 | done | No |
| 9 | Siege golem (noble) | #722 | done | No |
| 10 | Skill enchant (+skills) | #745 | server gates + packet doc (`FINEAGE_ALLOW_SKILL_ENCHANT`); trainer flow TBD | Yes |
| 11 | Day/night spawns | #721 | done — periodOfDay + IG transition | No |
| 12 | Clan hall siege | #724 | done — CH siege schedule + `SiegeInfo` by hall id | No |
| 13 | Baby pet / evolution | #726, #728 | done — spice heal + evolve/multisell | No |
| 15 | Zone types (swamp/damage/NL) | #731 | done — imported JSON + movement/sim | No |
| 16 | Boats + walking NPCs | #734 | done — walking routes + 5 boats; vehicle wire pending | Yes — `docs/client-packet-contracts.md` |
| 14 | Guard / town AI | #738 | done — karma scan, ally hate, `FINEAGE_GUARD_ATTACK_AGGRO_MOB` | No |
| 17 | Saga quest fidelity | #742 | done — helper timers, 4339 cast, archon skill-see | No |
| 2 | Quest batches | #709, #717, #718, #720 | partial — calc hunts Q169/316/341/347 on dev; **blocked** on next `gen_quests_*.py` (missing `npcId-00.htm` html) | No |
| 16a | Monster Derby Track | #736 | done — `MonsterRaceActor`, Race Manager HTML, `MonRaceInfo` `0x03CA` | Yes — `MonRaceInfo` `0xDD` wire in `docs/client-packet-contracts.md` |
| 18 | GM / punishment tooling | — | **other agent** | No |
| 19 | Compound skill conditions | — | **other agent** | No |
| 20 | Freight warehouse | — | **other agent** | No (no account freight persistence) |
| 6 | Seven Signs Festival | — | **other agent** | No (instances) |
| 7 | Dimensional rift | — | **other agent** | No (unblocks Q634/635) |

### Parallel ownership (2026-11-21)

Avoid duplicate PRs on these rows:

| Owner | Ranks / work |
|-------|----------------|
| **This agent (f138 main queue)** | 14–17, 3/4/10 (#738, #742, #745) merged; quest gens blocked on HTML import |
| **Other agents** | 19 compound conditions, 18 GM/punishment, 20 freight, 6 Festival, 7 rift |

Refresh `origin/dev` before branching; others may land parallel merges.

---

## Suggested audit refresh

1. `git fetch origin dev` and re-run this doc after major merges.
2. `python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude`
3. Regenerate combat rows if `skill-combat.json` changes (`scripts/gen_skill_*.py`).
4. Keep `mobius-parity-audit.md` as historical snapshot only — **do not** update in place.