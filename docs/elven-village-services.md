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
| Kill quests | Q267 Wrath of Verdure (Treant Bremec 31853, Goblin Raider 20325, elf 4+), Q313 Collect Spores (Herbiel 30150, Spore Fungus 20509, lvl 8+) | Repeatable. Q267: 50% Goblin Club per kill, hand in all → 1 Silvery Leaf each, +600 adena at 10+, `quit` exits. Q313: 40% Spore Sac, cap 10 → cond 2, 10 sacs → 3500 adena. |
| Newbie Helper 30400 | | Existing gift / guide buff path. |

## Quest engine additions
- Dialog gates `races` (0 Human, 1 Elf, 2 Dark Elf, 3 Orc, 4 Dwarf), `minLevel`, `maxLevel`. List passing branch first, refusal pages after (Mobius `race != ELF ? 01 : level < MIN ? 02 : 03`).
- `QuestActor(Race, Level)` passed by ZoneWorld on talk / event / quest list.
- Quest HTML event links must carry the id: `quest_event:<questId>:<event>` (Roxxy 999 fixed; the bypass parser rejected the id-less form on Net).

- Kill hook is live: `GrantKillReward` → `ApplyQuestKill` → `QuestMachine.OnKill` for the killer (melee + skill/mob-AI kill paths), published like talk (bag `InventoryUpdate`, sounds, journal `QuestList`). Party share / `ALT_PARTY_RANGE` not yet.
- Op `drop_item` {itemId, count, chance 0..1, limit (0 = none), condAtLimit} = Mobius `giveItemRandomly` (ItemGet sound, Middle + cond at cap).
- Op `exchange_all` {itemId, rewardItemId, per, bonusMin, bonusAdena} = hand-in-all rewards.
- Dialog gate `hasItem` + `minCount` (Mobius `hasQuestItems` / `getQuestItemsCount >=`).
- Quest rewards that merge into a held stack are sent as `Modify` (client ignored `Add` for a held objectId).

## Missing (next bricks, in order)
1. Party kill share (Mobius `getRandomPartyMember` / ALT_PARTY_RANGE) and more elven hunt quests (Q102 Sea of Spores Fever, Q408 Path of the Elven Wizard need multi-mob/multi-item and class-change rewards).
2. **Quest items tab / quest item flag** in items (client shows them in the main bag now).
3. Trainers 30155-30158 (`SkillList` learn on Net), Hierarch 30154 class change (`villagemaster`).
4. Merchant extras: `Wear`, `TerritoryStatus`, `exc_multisell 003` (multisell data check).
5. Newbie Guide 30599 multi-page / Adventure Guildsman 31780 quest list, Lottery 30991, Olympiad 31688 — leave as pages.
6. Other areas: run `tools/import_npc_html.py data/spawns/<area>.json` + review WARN lines.
