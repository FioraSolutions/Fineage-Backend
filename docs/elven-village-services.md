# Elven Village services on Net (foundation brick)

Scope: NPCs in `data/spawns/elven_village.json` only. Same pipeline scales per area:
`tools/import_npc_html.py <spawn file>` → `data/html`, quests in `data/quests.json`, items via `scripts/gen_drop_items.py`.

## Works (data + server, Net `NpcTalk 0x0322` / `BypassRequest 0x0323` → `NpcHtml 0x0324`)
| Service | NPCs | Notes |
|---|---|---|
| NPC dialog pages | all 50 non-attackable village NPCs | Mobius HTML imported (88 files); Chat/Link pages followed. |
| Gatekeeper | Mirabel 30146 | Page `Chat 1` lists 7 NORMAL destinations; legacy `goto <id>` rewritten to `teleport NORMAL <index>` of `teleports.json`. Noblesse pages are data only. |
| Shops | Unoren 30147, Ariel 30148, Creamees 30149, Herbiel 30150, Lynn 31414, Perelin 31563 | `Buy <list>` → `BuyList 0x0325`, `Sell` → `SellList 0x0326`. Buylists already in `buylists.json`. |
| Warehouse | Chad 30151, Julia 30152, Markius 30153 | Private deposit/withdraw (`DepositP`/`WithdrawP`). |
| Quests | Q160 Nerupa's Request (Nerupa→Unoren→Creamees→Julia→Nerupa), Q161 Fruit of the Mother Tree (Andellia→Thalia→Andellia) | Mobius conds, items 1026-1029/1036-1037, rewards (5 Lesser Healing Potion + 1000 xp / 1000 adena + 1000 xp). Elf + level 3 gate via new dialog `races`/`minLevel`/`maxLevel`. Flow: talk → `Quest` link → `quest_talk:<id>` → `quest_event:<id>:<event>`. |
| Newbie Helper 30400 | | Existing gift / guide buff path. |

## Quest engine additions
- Dialog gates `races` (0 Human, 1 Elf, 2 Dark Elf, 3 Orc, 4 Dwarf), `minLevel`, `maxLevel`. List passing branch first, refusal pages after (Mobius `race != ELF ? 01 : level < MIN ? 02 : 03`).
- `QuestActor(Race, Level)` passed by ZoneWorld on talk / event / quest list.
- Quest HTML event links must carry the id: `quest_event:<questId>:<event>` (Roxxy 999 fixed; the bypass parser rejected the id-less form on Net).

## Missing (next bricks, in order)
1. **Kill-count quests** for the elven newbie zone (e.g. Q260-range hunts from Elven Sentinels): engine has `kill` hook; needs a "count var + drop chance" op (`inc_var`, `chance`) to express Mobius `giveItemRandomly`.
2. **Quest items tab / quest item flag** in items (client shows them in the main bag now).
3. Trainers 30155-30158 (`SkillList` learn on Net), Hierarch 30154 class change (`villagemaster`).
4. Merchant extras: `Wear`, `TerritoryStatus`, `exc_multisell 003` (multisell data check).
5. Newbie Guide 30599 multi-page / Adventure Guildsman 31780 quest list, Lottery 30991, Olympiad 31688 — leave as pages.
6. Other areas: run `tools/import_npc_html.py data/spawns/<area>.json` + review WARN lines.
