# Mobius CT0 Interlude ↔ Fineage — live parity matrix

> Updated **2026-11-06** against Backend `dev` + open PRs `#255` (magicLevel/mcrit), `#256` (phys crit/night), `#257` (NPC soulshots).  
> Replaces the stale snapshot in `docs/mobius-parity-audit.md` and the outdated table on Backend issue [#174](https://github.com/FioraSolutions/Fineage-Backend/issues/174).  
> SoT for combat detail: [`combat-parity.md`](combat-parity.md). SoT for wire: [`net-protocol.md`](net-protocol.md).

Legend: ✅ done · 🟡 partial · ❌ missing · stub = shell only.

| # | Feature | Area | Status | Notes / next |
|---|---|---|---|---|
| 1 | Login / auth / roster / create (5 races) | Auth | ✅ | Net lobby |
| 2 | Movement, sit/stand, run/walk, target | World | ✅ | |
| 3 | Geodata collision / pathfinding | World | 🟡 | LOS ported; moveToPawn pathfinding incomplete |
| 4 | NPC spawns + respawn (loaded maps) | World | 🟡 | Full world when maps enabled; day/night spawn modes not switched |
| 5 | Melee + hit/miss/crit/shield/ss/position/night | Combat | ✅ | `#256` night; `#255` unrelated |
| 6 | Skills list/use/reuse + Mobius attack catalog | Combat | 🟡 | Catalog formulas; legacy `power+10` outside catalog |
| 7 | Buffs/debuffs / abnormals / weight 4270 / grade 4267 / DP 5076 | Combat | ✅ | |
| 8 | Magic: mcrit / fail / magicLevel / mCritRate / mCritPower | Combat | ✅ | `#255` |
| 9 | Phys skill crit / CritDmg / BlowRate / pSkillEvas | Combat | ✅ | `#256` |
| 10 | Blow / Lethal | Combat | ✅ | |
| 11 | Spoil / Sweep | Combat | 🟡 | Party distributeItem stub |
| 12 | Mob AI aggro/hate/clan/LOS | Combat | ✅ | |
| 13 | NPC soulshots | Combat | 🟡 | `#257` stock+chance on melee; no SS glow packet yet; spiritshots unused (no NPC magic cast) |
| 14 | Weapon trait / NPC defence traits | Combat | ❌ | Needs NPC passive → trait data |
| 15 | NPC cast magic/blow at players | Combat | ❌ | No mob skill AI |
| 16 | PvP flagging / karma persist | Combat | ❌ | karma field 0 |
| 17 | Pets / summons | Combat | ❌ | |
| 18 | Death: lost-exp + DP + town respawn | Combat | ✅ | |
| 19 | Death: player item drop (PK/karma) | Combat | ❌ | |
| 20 | Inventory / equip / warehouse private | Economy | ✅ | Clan/freight ❌ |
| 21 | Shops buy/sell/multisell | Economy | ✅ | Wear preview stub |
| 22 | Player trade | Economy | ✅ | |
| 23 | Private store / craft | Economy | ❌ | |
| 24 | Enchant | Economy | 🟡 | Backend; client UI partial |
| 25 | Party invite/leave + exp share | Social | 🟡 | Loot modes incomplete |
| 26 | Clan basic | Social | 🟡 | No crest/war/ally |
| 27 | Friends / block | Social | ❌ | |
| 28 | NPC HTML + bypass + teleports + newbie buffs | Content | ✅ | |
| 29 | Quests (engine + elven paths) | Content | 🟡 | Many scripts; ruins/quest-spawn gaps |
| 30 | Class change 1st (elven paths) | Content | 🟡 | Foundation; Gludio masters outside default maps |
| 31 | Raid / Seven Signs / Siege / Olympiad | Content | 🟡 | Schedule/reg shells; no full content |
| 32 | Chat channels beyond say/shout/system | Social | 🟡 | |

## Next backend slices (priority)

1. **Weapon trait / NPC defence traits** — [#261](https://github.com/FioraSolutions/Fineage-Backend/issues/261)
2. **NPC magic/blow skills vs players** — [#259](https://github.com/FioraSolutions/Fineage-Backend/issues/259)
3. **Party loot modes + sweep distributeItem** — [#260](https://github.com/FioraSolutions/Fineage-Backend/issues/260)
4. **Player death item drop** — [#258](https://github.com/FioraSolutions/Fineage-Backend/issues/258)
5. **Private store** (needs Net + client — coordinate).
6. **Geodata pathfinding** for approach (medium).

## Docs

| Doc | Role |
|-----|------|
| This file | Living feature matrix |
| `combat-parity.md` | Combat slice checklist |
| `elven-village-services.md` | Village content gaps |
| `mobius-parity-audit.md` | **SNAPSHOT** — do not trust without re-run |
