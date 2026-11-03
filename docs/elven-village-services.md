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
| Party kill quest | Q316 Destroy Plague Carriers (Ellenia 30155, elf 18+) | Data + mechanism ready; Sukar Wererat 20040/20047 and Varool 27020 are **not spawned** in the loaded elven maps yet (they live outside the village area). 2 fangs per wererat, Varool fang cap 1; 30 adena/fang, 10000/Varool fang, +5000 at 10+. |
| Newbie Helper 30400 | | Existing gift / guide buff path. |

## Quest engine additions
- Dialog gates `races` (0 Human, 1 Elf, 2 Dark Elf, 3 Orc, 4 Dwarf), `minLevel`, `maxLevel`. List passing branch first, refusal pages after (Mobius `race != ELF ? 01 : level < MIN ? 02 : 03`).
- `QuestActor(Race, Level)` passed by ZoneWorld on talk / event / quest list.
- Quest HTML event links must carry the id: `quest_event:<questId>:<event>` (Roxxy 999 fixed; the bypass parser rejected the id-less form on Net).

- Kill hook is live: `GrantKillReward` → `ApplyQuestKill` → `QuestMachine.OnKill` for the killer (melee + skill/mob-AI kill paths), published like talk (bag `InventoryUpdate`, sounds, journal `QuestList`). Kill steps marked `party: true` go to one random eligible member (killer included) within `ALT_PARTY_RANGE` 1600, alive, with matching state/cond/gates and no full capped drop (Mobius `getRandomPartyMemberState` + `checkPartyMember`). Unmarked steps stay killer-only, as Mobius Q267/Q313 do.
- Op `sell_items` {prices:[{itemId, adena}], bonusMin, bonusAdena} = hand in all registered items for adena.
- Op `drop_item` {itemId, count, chance 0..1, limit (0 = none), condAtLimit} = Mobius `giveItemRandomly` (ItemGet sound, Middle + cond at cap).
- Op `exchange_all` {itemId, rewardItemId, per, bonusMin, bonusAdena} = hand-in-all rewards.
- Dialog gate `hasItem` + `minCount` (Mobius `hasQuestItems` / `getQuestItemsCount >=`).
- Quest rewards that merge into a held stack are sent as `Modify` (client ignored `Add` for a held objectId).

## Trainers (foundation, free mode is default)
- `data/skill-trees.json` rows carry Mobius `levelUpSp` (`scripts/gen_skill_sp.py`, 13528 rows).
- Learned skills persist per character: `CharacterRecord.LearnedSkills`, JSON `learnedSkills`, Postgres `character_skills` (migration 004). Loaded at enter and merged into known skills (max level wins) on top of the class tree.
- Trainer page `SkillList` bypass (Ellenia 30155, Cobendell 30156, Greenis 30157, Esrandell 30158 pages link it) → `TrainerBypassHandler`:
  - **Default (`FINEAGE_TRAINER_SP_LEARN` unset)**: NPC-tree skills stay free at their level, exactly as before (Wind Strike etc.). SkillList shows an HTML note with the next level that brings a new skill. `learn_skill` does nothing.
  - **`FINEAGE_TRAINER_SP_LEARN=1`**: Mobius mode. Only autoGet rows are free; SkillList is an interim HTML list (next level per skill, getLevel reached, SP cost) with `learn_skill <id>` links. Learning spends SP, saves `character_skills`, re-sends `SkillList` (V2 + Net `FanoutSkillList`), SysMsg 277 / 278.
- **Open question for Nikita: keep free trainer skills or switch to SP-buy?** To flip: set `FINEAGE_TRAINER_SP_LEARN=1` for Zone (e.g. in `run-local-net.sh`). Characters then lose NPC skills they have not bought; consider a one-time grant of currently-known NPC skills into `character_skills` before flipping. Real Mobius UI (AcquireSkillList / RequestAcquireSkillInfo / RequestAcquireSkill) needs Net opcodes + a client window; the HTML list is the stopgap.
- Q316 mobs: Mobius spawns Sukar Wererats / Varool at ~(12400..12900, 75500..80200, z -4176) — the underground Elven Ruins, not the village/forest maps we load. Not spawned; needs that dungeon map first.

## Missing (next bricks, in order)
1. More elven hunt quests (Q102 Sea of Spores Fever); spawn Q316 / Q408 wererats when the Elven Ruins are loaded; quest-spawn op for Q409.
2. **Quest items tab / quest item flag** in items (client shows them in the main bag now).
3. Trainers 30155-30158 (`SkillList` learn on Net). Hierarch 30154 + first class change: foundation landed (see below).
4. Merchant extras: `Wear`, `TerritoryStatus`, `exc_multisell 003` (multisell data check).
5. Newbie Guide 30599 multi-page / Adventure Guildsman 31780 quest list, Lottery 30991, Olympiad 31688 — leave as pages.
6. Other areas: run `tools/import_npc_html.py data/spawns/<area>.json` + review WARN lines.

## Merchant extras + quest-item flag

- `TerritoryStatus` bypass is live (TerritoryBypassHandler → `territorystatus.htm` / `territorynoclan.htm` from Mobius): castle from MerchantTowns (Elven Village → Oren), owner clan + leader + tax from siege state.
- `Wear` stays a stub: Net has no ShopPreviewList and the client has no preview UI yet.
- Equipment exchange link `exc_multisell 003` already works (multisell 3 allows NPCs 30147–30149).
- Items now carry `quest` (scripts/gen_quest_item_flags.py marks it from Mobius `is_questitem`; 11 items so far, including 7586 Sweet Fluid). Quest items can't be sold, dropped, traded, or put in the warehouse. The flag isn't sent over the Net wire yet, so the client has no quest tab.

## Hierarch / first class change (foundation)

How Mobius does it: **Asterios 30154 does not change classes.** `village_master/FirstClassTransferTalk` only talks about
the transfer (race / class-level / mage pages). Elves change class at the human-town Grand Masters:
`ElfHumanWizardChange1` (Levian 30037 Gludin, Sylvain 30070 Dion, Raimund 30289 Gludio, 32095, 32098) and
`ElfHumanFighterChange1` (Pabris 30066, Rains 30288, Ramos 30373, 32094). Gates: 2nd/3rd/4th class pages first,
then from-class, level 20, proof item from the path quest; success takes the proof, `setClassId` + `setBaseClass`,
15 Shadow Item Exchange Coupon (D) 8869.

On Net:
- `Quest <ScriptName> [event]` with a non-numeric name is now a script bypass (`BypassReply.Script`); the world serves
  scripts listed in `data/village-masters.json` (`VillageMasterCatalog`, pure decisions) with Mobius HTML in
  `data/html/village_master/<Script>/` (copied from Mobius). Unknown scripts keep the "not implemented" page.
- Asterios: `Quest FirstClassTransferTalk` → `30154_mystic/fighter/no/transfer_1/transfer_2.html`; info sub-pages
  limited to that NPC's own `30154_*.html`.
- Grand Masters: class list by race / group (`-01`/`-08`/`-11`, mismatch page), info pages below the mismatch number,
  class-id events run `ClassChangeRequested`. Elven options: Knight 19 (1204 Elven Knight Brooch), Scout 22
  (1217 Reisa's Recommendation), Wizard 26 (1230 Eternity Diamond), Oracle 29 (1235 Leaf of Oracle); human ones too.
- Apply (`ZoneWorldVillageMasters.ApplyClassChange`): take all proofs, coupons, `ZoneEntity.ClassId` + `ZoneSession.ChangeClass`,
  speeds and max HP/MP from the new template, `RefreshKnownSkills` (new class tree: free NPC skills unless SP-learn is on),
  SkillList (V2 + Net), SysMsg 1308, UserInfo (V2 + Net), CharInfo to the knownlist, save class
  (`IAccountStore.TrySaveClass`: JSON + Postgres `characters.class_id`), bag, progress.
- **Q408 Path of the Elven Wizard** (Rossela 30414 → Greenis 30157 / Thalia 30371 / Northwind 30423), generated by
  `scripts/gen_q408.py` from the Mobius script, branch for branch. Quest engine gained dialog gates `classes` (QuestActor
  now carries ClassId), `hasItems` (all), `lacksItems` (none) and `when: any`. Drops: Pincer Spider 20466 → Red Down (70%, 5),
  Dryad Elder 20019 → Gold Leaves (40%, 5), Sukar Wererat Leader 20047 → Amethyst (40%, 2). Reward 163800 adena,
  Eternity Diamond, exp/sp by level (18: 591724/35928, 19: 456128/29230, 20+: 320534/22532).

Play reachability with the default local maps (elven_village, elven_starting, ...):
- Asterios dialogs: yes. Q408 start, ruby (Pincer Spiders) and aquamarine (Dryad Elders) branches: yes.
- Amethyst branch: **blocked** — Sukar Wererat Leaders are in the Elven Ruins (not loaded), as for Q316.
- The actual transfer NPCs live in Gludin/Gludio/Dion, outside the default `FINEAGE_ZONE_MAPS`. Server path is covered by
  tests; to try it in Play load a town map with a Grand Master (e.g. add `gludio` and widen `FINEAGE_ZONE_BOUNDS`).

Not done / next:
- Elven Ruins map for wererats (Q408 amethyst).
- Mobius `setClassId` extras: MagicSkillUse 5103 class-change effect broadcast, party/clan member-list refresh,
  `checkPlayerSkills` (drop skills above the new tree), sub-class / `setBaseClass` (we only have one class id).
- Net client has no dedicated class-change UI: everything is NpcHtml + UserInfo/SkillList it already handles.
- `FINEAGE_TRAINER_SP_LEARN` and `AOI_CELL` stay off by default (unchanged).

## Other elven first-job paths (Q406 / Q407 / Q409)

Generated by `scripts/gen_elven_paths.py` (same pattern as `gen_q408.py`, one `Gen` helper per quest) from the Mobius
scripts, branch for branch. New dialog gate `atLeast: {itemId: count}` (Mobius `getQuestItemsCount(a) >= n` on several
items at once); Mobius quest vars map to `whenVar` (`variable` for Q407, `memo` for Q409 memoState).

| Quest | Start | Other NPCs | Kill steps | Proof → class |
|---|---|---|---|---|
| Q406 Path of the Elven Knight | Sorius 30327 (Gludio) | Kluto 30317 (Gludin) | 20 Topaz Pieces 70% from Tracker Skeleton(s) / Skeleton Scout / Bowman / Ruin Spartoi / Salamander Noble (20035/20042/20045/20051/20054/20060, others_18_21); 20 Emerald Pieces 50% from Ol Mahum Novice 20782 (others_18_22) | Elven Knight Brooch 1204 → Elven Knight 19 |
| Q407 Path of the Elven Scout | Reoria 30328 (Gludio) | Moretti 30337, Babenco 30334 (Gludio), Prias 30426 (others_19_20) | 4 torn letters from Ol Mahum Patrol 20053 (others_18_22); Rusted Key 60% from Ol Mahum Sentry 27031 (others_19_20) | Reisa's Recommendation 1217 → Elven Scout 22 |
| Q409 Path of the Elven Oracle | Manuel 30293 (Gludio) | Allana 30424, Perrin 30428 (Gludin) | Lizardman Warrior/Scout/Soldier 27032-27034 and Tamil 27035, **spawned by Allana `replay_1` / Perrin `replay_2`** | Leaf of Oracle 1235 → Elven Oracle 29 |

Rewards like Mobius: 163800 adena + proof + exp/sp by level (Q406 18/19/20+: 591724/33328, 456128/29850, 320534/23152;
Q407: 591724/33328, 456128/26630, 320534/19932; Q409: 591724/33788, 456128/27090, 320534/20392).

**Play reachability:** as in Mobius, none of these three paths has an NPC or a mob in the Elven Village maps — all start in
Gludio/Gludin and hunt in the Gludio-area `others_*` maps. With the default `FINEAGE_ZONE_MAPS` they are data + tests only.
To play them load `gludio,gludin` plus the hunt maps above (and widen `FINEAGE_ZONE_BOUNDS`); this slice does not change the maps.

Stubs: Q409 `replay_1` / `replay_2` set the memo state but cannot spawn the quest monsters (no quest-spawn op yet) and
show a short note page instead; Mobius `onAttack` script values (Q407 "only the attacker who hit it", Q409 "single attacker")
are approximated by killer credit.

## Newbie helpers / village buffs (Mobius CT0 parity foundation)

- **Support Magic** (`data/newbie-buffs.json`, Mobius `handlers/bypasshandlers/SupportMagic.java`): levels 8..39, refused at class level 3, **no isNewbie check** (`requireNewbie: false`). Windows: Wind Walk 4322 8–39, Shield 4323 11–39; fighter 4324 12–38 / 4325 13–38 / 4326 14–38 / 4327 15–37; mage 4328 12–38 / 4329 13–38 / 4330 14–38 / 4331 15–37; Life Cubic 4338 16–36. Too low/high serves Mobius `default/SupportMagicLowLevel.htm` / `SupportMagicHighLevel.htm`. No cooldown/once rule in Mobius (re-castable).
- **Blessing of Protection** 5182: unchanged (≤39, refused at class level 2).
- **Newbie Travel Token** (`travelToken` block; Mobius `ai/others/NewbieTravelToken`): Newbie Guide 30599 `Script NewbieTravelToken` shows `newbie_travel/30599.htm` (level ≥20 → `cant-travel.htm`). A destination event takes one token 8542 and teleports (DE 30600, DW 30601, EV 30599, OV 30602, TI 30598). No token → SysMsg 351 (INCORRECT_ITEM_COUNT). Destinations outside loaded maps are refused **before** the token is taken (Fineage deviation; Mobius has the whole world).
- Stubs: `Script NpcLocationInfo` (Mobius `custom/`, not ported) still shows the unavailable page. Newbie first-talk gift (NewbieHelperGift) unchanged.
- Play: Elven Village → talk to Newbie Guide (30599) → Support Magic at lv 8–39; Travel Token link with token 8542 in bag at lv <20.
