# Mobius CT0 Interlude ↔ Fineage — live parity matrix

> Updated **2026-11-06** against Backend `main` / `dev` after [#287](https://github.com/FioraSolutions/Fineage-Backend/pull/287) (parity stack through friends / craft / stores / NPC AI / PvP / death-drop / approach geo).  
> Replaces the stale snapshot in `docs/mobius-parity-audit.md`. Tracker issue [#174](https://github.com/FioraSolutions/Fineage-Backend/issues/174) mirrors this table.  
> SoT for combat detail: [`combat-parity.md`](combat-parity.md). SoT for wire: [`net-protocol.md`](net-protocol.md).

Legend: ✅ done · 🟡 partial · ❌ missing · stub = shell only.

| # | Feature | Area | Status | Notes / next |
|---|---|---|---|---|
| 1 | Login / auth / roster / create (5 races) | Auth | ✅ | Net lobby |
| 2 | Movement, sit/stand, run/walk, target | World | ✅ | |
| 3 | Geodata collision / pathfinding | World | ✅ | LOS + MoveCheck/FindPath; approach ETA/sticky/path-sync (#273) |
| 4 | NPC spawns + respawn (loaded maps) | World | 🟡 | Full world when maps enabled; day/night modes → [#292](https://github.com/FioraSolutions/Fineage-Backend/issues/292) |
| 5 | Melee + hit/miss/crit/shield/ss/position/night | Combat | ✅ | `#256` night |
| 6 | Skills list/use/reuse + Mobius attack catalog | Combat | 🟡 | Catalog formulas; legacy `power+10` → [#290](https://github.com/FioraSolutions/Fineage-Backend/issues/290) |
| 7 | Buffs/debuffs / abnormals / weight 4270 / grade 4267 / DP 5076 | Combat | ✅ | |
| 8 | Magic: mcrit / fail / magicLevel / mCritRate / mCritPower | Combat | ✅ | `#255` |
| 9 | Phys skill crit / CritDmg / BlowRate / pSkillEvas | Combat | ✅ | `#256` |
| 10 | Blow / Lethal | Combat | ✅ | |
| 11 | Spoil / Sweep | Combat | ✅ | `#260` sweep + ground pickup party `distributeItem` |
| 12 | Mob AI aggro/hate/clan/LOS | Combat | ✅ | |
| 13 | NPC soulshots / spiritshots | Combat | 🟡 | Melee SS + magic spiritshots; glow → [#291](https://github.com/FioraSolutions/Fineage-Backend/issues/291) |
| 14 | Weapon trait / NPC defence traits | Combat | ✅ | `#261` weapon + race/type-2 via `AttackTraitBonus` |
| 15 | NPC cast magic/blow at players | Combat | ✅ | `#259` attack + HEAL/BUFF/DEBUFF/IMMOBILIZE/silence + clan heal + SUICIDE |
| 16 | PvP flagging / karma persist | Combat | ✅ | `#271` flag on damage, PK/PvP on kill, karma/pk/pvp persist |
| 17 | Pets / summons | Combat | ❌ | [#288](https://github.com/FioraSolutions/Fineage-Backend/issues/288) |
| 18 | Death: lost-exp + DP + town respawn | Combat | ✅ | |
| 19 | Death: player item drop (PK/karma) | Combat | ✅ | `#258` `onDieDropItem` + `data/death-drop.json`; clan-war/PvP-zone exemptions deferred |
| 20 | Inventory / equip / warehouse private | Economy | ✅ | Clan/freight → [#294](https://github.com/FioraSolutions/Fineage-Backend/issues/294) |
| 21 | Shops buy/sell/multisell | Economy | ✅ | Wear preview stub |
| 22 | Player trade | Economy | ✅ | |
| 23 | Private store / craft | Economy | 🟡 | Server #275–#283 done; client UI → [#296](https://github.com/FioraSolutions/Fineage-Backend/issues/296) |
| 24 | Enchant | Economy | 🟡 | Backend; client UI partial |
| 25 | Party invite/leave + exp share | Social | 🟡 | Invite/leave + exp; loot modes on sweep + ground pickup |
| 26 | Clan basic | Social | 🟡 | Crest/war/ally → [#293](https://github.com/FioraSolutions/Fineage-Backend/issues/293) |
| 27 | Friends / block | Social | 🟡 | List/block #285 done; friend chat → [#289](https://github.com/FioraSolutions/Fineage-Backend/issues/289) |
| 28 | NPC HTML + bypass + teleports + newbie buffs | Content | ✅ | |
| 29 | Quests (engine + elven paths) | Content | 🟡 | Ruins/quest-spawn gaps → [#299](https://github.com/FioraSolutions/Fineage-Backend/issues/299) |
| 30 | Class change 1st (elven paths) | Content | 🟡 | Foundation; playable maps → [#298](https://github.com/FioraSolutions/Fineage-Backend/issues/298) |
| 31 | Raid / Seven Signs / Siege / Olympiad | Content | 🟡 | Shells; **late** → [#295](https://github.com/FioraSolutions/Fineage-Backend/issues/295) |
| 32 | Chat channels beyond say/shout/system | Social | 🟡 | |

## Landed slices (close the cards)

| Issue | Landed in |
|-------|-----------|
| #258 death item drop | `fd53fea` → `#287` |
| #259 NPC AI skills | `c70feae`…`241a32b` → `#287` |
| #271 PvP / karma | `d9c20a7` → `#287` |
| #273 approach FindPath | `a9ac246` → `#287` |
| #275–#279 private stores | `a405b98`…`0600ab7` → `#287` |
| #281 self-craft | `573a81b` → `#287` |
| #283 manufacture | `1aa1186` → `#287` |
| #285 friends + block | `64dde12` → `#287` |

## Next slices — PvE / leveling first

Order is **character progression and hunt combat**, then polish/social, then clan / siege / oly / alliances last.

| Priority | Issue | Why |
|----------|-------|-----|
| 1 | [#298](https://github.com/FioraSolutions/Fineage-Backend/issues/298) 1st class change playable | Unlocks progression past lvl 20 |
| 2 | [#299](https://github.com/FioraSolutions/Fineage-Backend/issues/299) elven quests + ruins spawns | Leveling loops |
| 3 | [#290](https://github.com/FioraSolutions/Fineage-Backend/issues/290) drop `power+10` skill stub | Correct skill damage while hunting |
| 4 | [#288](https://github.com/FioraSolutions/Fineage-Backend/issues/288) pets / summons | Pet/summoner class PvE |
| 5 | [#292](https://github.com/FioraSolutions/Fineage-Backend/issues/292) day/night spawn modes | Hunt roster fidelity |
| 6 | [#296](https://github.com/FioraSolutions/Fineage-Backend/issues/296) client store/craft UI | Gear via craft/stores |
| 7 | [#291](https://github.com/FioraSolutions/Fineage-Backend/issues/291) NPC SS glow | Cosmetic |
| 8 | [#289](https://github.com/FioraSolutions/Fineage-Backend/issues/289) friend chat | Social |
| **Last** | [#294](https://github.com/FioraSolutions/Fineage-Backend/issues/294) clan/freight WH · [#293](https://github.com/FioraSolutions/Fineage-Backend/issues/293) crest/war/ally · [#295](https://github.com/FioraSolutions/Fineage-Backend/issues/295) SS/siege/oly | Endgame social / territory |

Combat polish backlog (not separate matrix rows): see [`combat-parity.md`](combat-parity.md) § Remaining gaps.

## Docs

| Doc | Role |
|-----|------|
| This file | Living feature matrix |
| `combat-parity.md` | Combat slice checklist |
| `elven-village-services.md` | Village content gaps |
| `mobius-parity-audit.md` | **SNAPSHOT** — do not trust without re-run |
