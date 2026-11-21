# Mobius CT_0 Interlude ↔ Fineage — parity matrix

> **2026-11-21** — audited against Fineage `dev` @ `0efaa43` and  
> `.cache/l2j-mobius/L2J_Mobius_CT_0_Interlude` (MobiusDevelopment GitLab, `master`).  
> Method: source/data diff, not issue trackers. Detail: [`mobius-gap-audit-2026-11-21.md`](mobius-gap-audit-2026-11-21.md).  
> Combat slice checklist: [`combat-parity.md`](combat-parity.md). Quest counts: [`QUEST_STATUS.md`](QUEST_STATUS.md).

**Legend:** ✅ done · 🟡 partial · ❌ missing · n/a (not in Interlude target or explicitly deferred)

**Size:** S = days-scale slice · M = multi-week subsystem · L = large program

| # | System | Status | Size | Notes (gap headline) |
|---|--------|--------|------|----------------------|
| **Auth & character** |
| 1 | Login, roster, Net auth | ✅ | — | `Fineage.Login` |
| 2 | Character create (5 races, templates) | ✅ | — | `LoginAcceptance` + Mobius class templates import |
| 3 | Enter world / persist / inventory | ✅ | — | Accounts + `ZoneWorld.Members.Persist` |
| **World & geo** |
| 4 | Movement, sit/stand, run/walk, validate | ✅ | — | `ZoneWorldMovement` |
| 5 | Geodata LOS / MoveCheck / FindPath | 🟡 | M | `GeoEngine` port; no-geo fallback; fence rules skipped (`combat-parity` §12) |
| 6 | Doors (collision) | 🟡 | S | `DoorTable`; no doorman/siege door timers |
| 7 | Zones: peace / PvP arena / water | 🟡 | M | `PeaceZones`, `PvpZones`, `WaterZones` — no swamp, damage, no-landing, mother tree (`zones/*.xml`) |
| 8 | NPC spawns + respawn | 🟡 | L | 189 spawn files; TI-focused; 223/223 RB rows `enabled: false` in `raidbosses.json` |
| 9 | Day/night spawn swap | ❌ | M | Mobius `DayNightSpawnManager.java` + spawn `night` flags |
| 10 | Walking NPC routes | ❌ | S | Mobius `WalkingManager.java` |
| 11 | Boats | ❌ | M | Mobius `BoatManager.java` |
| **Combat core** |
| 12 | Melee hit/miss/crit/shield/ss/position/night | ✅ | — | `ZoneCombat`, `combat-parity` slices 5–16 |
| 13 | Bow + arrows | 🟡 | S | Consumption/reuse timing open (`combat-parity` §3) |
| 14 | Physical/magic skill damage catalog | ✅ | — | 387 rows `skill-combat.json`; missing row → refuse cast (no `power+10`) |
| 15 | Blow / lethal | ✅ | — | `ZoneWorldSkills` + gen scripts |
| 16 | Magic crit / fail / mcrit stats | ✅ | — | Slice 10 |
| 17 | Buffs/debuffs / abnormals / stat funcs | 🟡 | M | 372 stat-effect skills; passive HP/zone conditions skipped; buffs apply at cast-start not hitTime |
| 18 | Skill conditions (`using` kinds) | 🟡 | S | 41 skills; compound XML conditions skipped (`gen_skill_conditions.py`) |
| 19 | Signets (454–460) | 🟡 | S | `ZoneWorldSignets`; EffectPoint TODO |
| 20 | Cast ↔ movement / queue | 🟡 | M | No damage/stun interrupt; no `MagicSkillCanceled` on target death |
| 21 | Spoil / sweep single-target | ✅ | — | `ZoneWorldSpoil` |
| 22 | Spoil/Sweeper Festival (302/444) | ❌ | S | Mobius AoE spoil handlers |
| 23 | Death exp/SP/karma/DP/town respawn | ✅ | — | `ZoneWorldDeath*` |
| 24 | PK item drop | ✅ | — | `ZoneWorldDeathDrop` |
| 25 | PvP flag / karma persist | ✅ | — | `ZoneWorldPvp` |
| 26 | Weight penalty 4270 / grade 4267 | ✅ | — | `ZoneWorldWeight` |
| **AI & NPC combat** |
| 27 | Mob AI aggro/hate/clan/LOS | ✅ | — | `ZoneWorldMobAi`, `ZoneWorldAggro` |
| 28 | Minion follow + raid minion | ✅ | — | `ZoneWorldNpcMovement` |
| 29 | NPC soul/spirit shots | 🟡 | S | Stock+chance; SS glow 2154 / boss stock copy open |
| 30 | NPC cast heal/buff/debuff/AI skills | ✅ | — | `ZoneWorldNpcSkills` |
| 31 | Guard / village master AI scripts | 🟡 | M | Generic AI only; Mobius `ai/others/*Guard*` scripts not ported |
| **Drops & economy** |
| 32 | Droplists (1295 NPC JSON) | 🟡 | M | `DropCatalog`; not every Mobius NPC has a row |
| 33 | Herbs on ground | ✅ | — | `ZoneWorldHerbs` |
| 34 | Ground loot + drop protection | ✅ | — | `ZoneWorldGround`, `GroundRules` |
| 35 | Auto-loot (config + herbs/raid flags) | ❌ | M | Mobius `Attackable.doDie` + `PlayerConfig.AUTO_LOOT*` |
| 36 | Party loot modes + adena share | ✅ | — | `PartyLootRules`, spoil/ground |
| 37 | Raid loot owner + curse | ✅ | — | `ZoneWorldRaidLoot`, `ZoneWorldRaidCurse` |
| 38 | Inventory / equip / private WH | ✅ | — | `ZoneWorldInventory`, `ZoneWorldGear` |
| 39 | Clan WH / crest / skills / academy | ✅ | — | `ZoneWorldPledge.*` |
| 40 | Freight warehouse | ❌ | S | Mobius freight; bypass stub `NpcServiceBypass.cs` |
| 41 | Shops / multisell / buylists | ✅ | — | `ZoneWorldShops` |
| 42 | Trade / private store / manufacture | ✅ | — | `ZoneWorldTrade`, `ZoneWorldPrivateStore`, `ZoneWorldManufacture` |
| 43 | Item enchant scrolls | ✅ | — | `ZoneWorldEnchant` |
| 44 | Item augmentation (life stones) | ❌ | L | Mobius `AugmentationData.java`, `RequestRefine*.java` |
| 45 | Skill enchant (+) | ❌ | M | Mobius `RequestExEnchantSkill.java` — persistence note only in `AccountModels` |
| 46 | Craft / recipes / dwarven manufacture | 🟡 | M | `ZoneWorldCraft`; recipe coverage vs Mobius `RecipeController` not 1:1 |
| 47 | Manor (sow/harvest/tax) | ✅ | — | `ZoneWorldManor*`, `Manor/*` |
| 48 | Economy ledgers (consume/grant/fee/…) | ✅ | — | `*Ledger.cs` |
| **Social** |
| 49 | Party invite/leave/CC | ✅ | — | `ZoneWorldParty` |
| 50 | Friends / block | ✅ | — | `ZoneWorldFriends` |
| 51 | Chat say/shout/trade/party/clan/hero | 🟡 | S | `ZoneWorldChat`; GM/alliance broadcast polish |
| 52 | Chat ban (punishment) | 🟡 | S | `ChatBannedUntilUnixMs`; no jail/ban login |
| 53 | Duels 1v1 / party | ✅ | — | `ZoneWorldDuels` |
| 54 | Clan wars | ✅ | — | `ZoneWorldPledge.Wars` |
| 55 | Alliance | ✅ | — | `ZoneWorldAlly` |
| **Companions** |
| 56 | Servitors | ✅ | — | `ZoneWorldSummons` |
| 57 | Cubics | 🟡 | S | `ZoneWorldCubics`; M.Atk/M.Def on success TODO |
| 58 | Pets (hunger, exp, resurrect) | 🟡 | M | `ZoneWorldPets`; baby buffs / evolution missing |
| 59 | Mounts / wyvern | ✅ | — | `ZoneWorldMounts*` |
| **Progression & services** |
| 60 | 1st/2nd class (village masters) | 🟡 | M | `ZoneWorldVillageMasters`; Ketra/Varka NPC gaps on quests |
| 61 | 3rd class sagas (70–100) | 🟡 | M | Saga engine; no helper skill casts / timers |
| 62 | Subclass | ✅ | — | `ZoneWorldSubclass` |
| 63 | Noblesse | 🟡 | S | `ZoneWorldNoblesse`; NPC selection bypass stub |
| 64 | Henna / symbol maker | ✅ | — | `ZoneWorldHenna`, `ZoneWorldSymbolMaker` |
| 65 | Skill learn / SP | 🟡 | M | Trainer HTML; not every Mobius skill tree edge |
| 66 | NPC teleports / HTML / bypass | ✅ | — | `ZoneWorldNpcHtml`, `BypassHandlers` |
| 67 | Warehouse / package / clan WH | 🟡 | S | Clan done; freight missing |
| **Quests & content** |
| 68 | Quest engine + packs | 🟡 | L | 213 impl / 93 partial / 31 stub / 7 missing / 344 Mobius (`QUEST_STATUS`) |
| 69 | NPC quests (non-pack) village master scripts | 🟡 | M | Alliance/clan masters wired; many `ai/others` absent |
| **PvE endgame** |
| 70 | Raid/grand boss schedule | 🟡 | L | `ZoneWorldBosses`; world gating disables most spawns |
| 71 | Seven Signs (engine, stones, catacombs gate) | 🟡 | M | `SevenSigns/*`; **Festival of Darkness** instance ❌ |
| 72 | Mammon merchants | ✅ | — | `ZoneWorldMammon` |
| 73 | Dimensional rift | ❌ | L | Mobius `DimensionalRiftManager.java` |
| 74 | Cursed weapons (Zariche/Akamanah) | ❌ | L | Rules only (`PlayableAttackRules`); Mobius `CursedWeaponsManager.java` |
| 75 | Lottery (Loto) | ✅ | — | `ZoneWorldLottery` |
| **Castle & siege** |
| 76 | Castle siege (towers, guards, HQ flag) | 🟡 | M | `ZoneWorldSiege.*`; flame/control towers present |
| 77 | Siege mercenary tickets | ❌ | M | Mobius `MercTicketManager.java`, `handlers/items/MercTicket.java` |
| 78 | Noble siege skills / siege golem summon | ❌ | M | Mobius `ConditionPlayerCanSummonSiegeGolem.java` |
| 79 | Clan hall **auction** | ✅ | — | `ZoneWorldPledge.Halls`, `ClanHallAuctionActor` |
| 80 | Clan hall **siege** | ❌ | M | Mobius `CHSiegeManager.java` |
| 81 | Fortress siege | n/a | — | Interlude Fineage scope: castles only |
| **Olympiad & hero** |
| 82 | Olympiad matchmaking / stadium / hero | ✅ | — | `Olympiad/*`, `ZoneWorldOlympiad*` |
| 83 | Raid boss points (ranking) | ❌ | S | Mobius `RaidBossPointsManager.java` |
| **Activities** |
| 84 | Fishing | ✅ | — | `ZoneWorldFishing`, `Fishing/*` |
| 85 | Fishing championship / quests 27–30 | 🟡 | M | Engine ✅; quest packs stub/partial |
| 86 | Soul crystals / symbol maker raids | 🟡 | S | `ZoneWorldSoulCrystals` — coverage incomplete |
| **Items & shots** |
| 87 | Soul/spirit shots (player + auto) | ✅ | — | `ZoneWorldShots`, slice 9 |
| 88 | Arrows | 🟡 | S | Partial bow loop |
| 89 | Consumables (potions, scrolls) | ✅ | — | `ZoneWorldConsumables` |
| **Meta** |
| 90 | GM commands / admin panel | 🟡 | L | `ZoneWorldAdmin` (~500 LOC) vs 80+ Mobius `handlers/admin/*` |
| 91 | Scripted events (drops, TvT, …) | ❌ | M | Mobius `EventDropManager`, `dist/.../events` |
| 92 | Mail system | n/a | — | Not targeted on Fineage Net |
| 93 | Community board | ❌ | L | Mobius `communitybbs/*` |
| 94 | Autoplay voiced command | n/a | — | Mobius optional; client-side on retail |

## Landed since 2026-11-06 matrix (PRs ~#587–#697)

Core combat/PvE (earlier #256–#287) plus: full pledge/siege/manor/olympiad stacks, pets/summons/mounts, fishing/lottery/Mammon, Seven Signs periods, raid curse/loot, clan wars, Ketra/Varka and profession quest waves, economy ledgers, expanded `skill-combat` / drops / NPC AI skills.

## Prioritized next slices (player impact)

See **Top 20** in [`mobius-gap-audit-2026-11-21.md`](mobius-gap-audit-2026-11-21.md). PvE-first ordering: world spawns → quest partials → augmentation → cursed weapons → auto-loot → siege mercs/golem → festival/rift → skill enchant.

## Related docs

| Doc | Role |
|-----|------|
| This file | Living feature matrix |
| `mobius-gap-audit-2026-11-21.md` | 2026-11-21 full audit + Mobius paths |
| `mobius-parity-audit.md` | **SNAPSHOT 2026-11-03** — historical only |
| `combat-parity.md` | Combat slice checklist |
| `QUEST_STATUS.md` | Per-quest Mobius diff |