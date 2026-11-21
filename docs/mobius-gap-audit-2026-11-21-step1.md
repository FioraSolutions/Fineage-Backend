# Mobius CT_0 Interlude parity audit — Step 1 (2026-11-21)

| | |
|---|---|
| **Fineage** | Branch `dev`, commit `daa9a276` |
| **Baseline** | Prior snapshot [`mobius-gap-audit-2026-11-22.md`](mobius-gap-audit-2026-11-22.md); quests [`QUEST_STATUS.md`](QUEST_STATUS.md) |
| **Wire** | Fineage Net opcodes in `src/Fineage.Net.Contract/Opcodes/NetOpcode.cs`; handlers `src/Fineage.Zone/Net/NetZoneServer.Handle.cs` |
| **Out of scope** | Skill effect/condition runtime (separate agent); fortress; mail; boats/airships full wire (n/a partial) |

## Executive delta since 2026-11-22 audit

| Area | Was | Now | Evidence |
|------|-----|-----|----------|
| Item augmentation | ❌ gate only | ✅ refine/cancel + ledgers | `ZoneWorldAugment.cs`, `AugmentPacketHandler.cs`, `AugmentationCatalog`, tests |
| Cursed weapons lifecycle | ❌ flags only | 🟡 manager + fanout tests | `CursedWeaponsManager.cs`, `ZoneWorldCursedWeapons.cs`, `CursedWeaponsTests.cs` |
| Quest counts | 224/90/29/1 | **240/94/10/0** | `QUEST_STATUS.md` (regenerated on `dev`) |
| Duelist force charges | — | ✅ | `ZoneWorldForceCharges.cs` (#774) |

## Packet handlers (C2S → zone)

**Login** (`Fineage.Login/Net/`): Auth, roster, create/delete/restore character, handoff — **done** (`ProtocolCompatTests`).

**Zone** (`NetZoneServer.Handle.cs` switch): all mapped `NetOpcode` client requests through dedicated `*PacketHandler` types — **done** for Interlude gameplay slice. Unknown opcode → `Reject` with concrete code (no silent drop).

Residual wire polish (not missing handlers, missing S2C bodies): `docs/client-packet-contracts.md`, `docs/net-protocol.md` — `MagicSkillCanceled`, full `RelationChanged`, boat vehicle family, auto-shot UI highlight.

## Subsystem status (done / partial / missing)

Legend: **done** ✅ · **partial** 🟡 · **missing** ❌

| Subsystem | Status | Evidence / gap |
|-----------|--------|----------------|
| Combat formulas (phys/magic/blow) | ✅ | `ZoneCombat.cs`, `ZoneWorldSkillDamage` |
| Bow arrows + cast interrupt | ✅ | `ZoneWorldArrows`, `ZoneWorldCastInterrupt` |
| Soulshots / spiritshots | ✅ | `ZoneWorldShots` |
| Signets (EffectPoint AOE) | 🟡 | `ZoneWorldSignets.cs` EffectPoint incomplete |
| Spoil Festival 302 combat row | ❌ | skill 302 not in `skill-combat.json` |
| NPC soulshot glow (2154) | 🟡 | #291 open; damage SS ✅ |
| Death / exp loss / 5076 / PK drop | ✅ | `ZoneWorldDeath`, `ZoneDeathRulesTests` |
| Karma / PvP count | ✅ | `ZoneWorldPvp` |
| Items / inventory / weight 4270 | ✅ | weight tests, ledger on mutations |
| Warehouse / freight | ✅ | `WarehousePacketHandler`, freight #741 |
| Trade / private store / manufacture | ✅ | `ZoneWorldTrade`, `ZoneWorldPrivateStore`, `RecipePacketHandler` |
| Crafting / recipes | ✅ | `RecipePacketHandler`, data recipes |
| Enchant item | ✅ | `ZoneWorldEnchant` |
| Augmentation refine | ✅ | `ZoneWorldAugment` (was ❌ in 11-22 audit) |
| Skill enchant (ExEnchantSkill) | ❌ | `SkillEnchantRules` gate only |
| Henna | ✅ | `ZoneWorldHenna` |
| Cursed weapons cycle | 🟡 | Manager + tests; drop-from-raid integration verify |
| Cursed weapon UI packets | 🟡 | `FanoutCursedWeaponList` in tests; client parity verify |
| Quests 344 Mobius | 🟡 | **240** impl, **94** partial, **10** stub |
| NPC AI (mobs/guards/raid) | ✅ core | `ZoneWorldMobAi`, guards #738/#760 |
| Event/quest scripted AI | 🟡 | JSON quests only; Mobius `ai/**` long tail |
| Seven Signs / catacombs / festival | ✅ entry 🟡 | festival #751–756; gate NPC kick partial |
| Dimensional rift | ✅ | `ZoneWorldDimensionalRift` |
| Siege / CH siege / merc / golem | ✅ hire data 🟡 | `ZoneWorldSiege`, `ChSiegeActor`, merc tickets |
| Clan / ally / wars / academy | ✅ | `ZoneWorldPledge.*`, `ZoneWorldAlly` |
| Clan hall auction / doors / escape | ✅ | auction actor, doors #739 |
| Clan hall functions depth | 🟡 | `ZoneWorldPledge.HallFunctions` partial deco/multisell |
| Manor / castle tax | ✅ | `ZoneWorldManor` |
| Olympiad / hero | ✅ nuances 🟡 | `Olympiad/*`; hero weapon grant |
| Noblesse NPC flow | 🟡 | bypass stub |
| Subclass | ✅ | `ZoneWorldSubclass` |
| Party / loot modes | ✅ | `PartyLootRules` |
| Command channel | 🟡 | chat stubs only `ZoneWorldChat.cs` |
| Duels | ✅ | `ZoneWorldDuels` |
| Pets / summons / cubics | ✅ cubic 🟡 | `ZoneWorldPets`, `ZoneWorldSummons`; cubic M.Atk TODO |
| Fishing minigame | ✅ | `Fishing/*` |
| Fishing championship | ❌ | not present |
| Teleports / gatekeepers | ✅ | `ZoneWorldNpcServices`, teleport data |
| Admin / GM commands | 🟡 | `ZoneWorldAdmin` subset ~80 Mobius handlers |
| Chat channels | 🟡 | core say/party/clan; hero/friend #289 |
| Friends / block | ✅ | `ZoneWorldFriends` |
| Macros / shortcuts persist | ✅ | `ZoneWorldShortcuts` |
| Geodata / path | 🟡 | optional stub host mode |
| Spawns / day-night | ✅ | `ZoneWorldSpawns.DayNight` |
| Raid bosses schedule | 🟡 | 216/223 enabled; regional gaps |
| Mother tree zone | ❌ | not loaded |
| Boats sim | 🟡 | `ZoneWorldBoats`; vehicle packets partial |
| Monster derby | ✅ | #736 |
| Event drops / TvT | ❌ | not present |
| Transfer skills | ❌ | not found |
| Skill learn trees | 🟡 | partial `skill-trees.json` |
| Droplist coverage | 🟡 | 1295 JSON; rare mob holes |
| Economy ledger | ✅ (Fineage) | all adena/item through ledger kinds |

## Quest summary

| Status | Count |
|--------|------:|
| implemented | 240 |
| partial | 94 |
| stub | 10 |
| missing | 0 |

## Open parity gaps (coherent features — issue targets)

Count **🟡/❌ subsystem rows still actionable:** **28** (augmentation + core freight/guards/festival removed from 11-22 list). Quest backfill tracked as **2** meta issues (partial wave + stub wave).

Tracker epic: GitHub issue created/updated in Step 2.