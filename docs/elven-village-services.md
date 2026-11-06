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
| Kill quests | Q267 Wrath of Verdure (Treant Bremec 31853, Goblin Raider 20325, elf 4+), Q313 Collect Spores (Herbiel 30150, Spore Fungus 20509, lvl 8+), **Q102 Sea of Spores Fever** (Alberius 30284, elf 12+) | Q267/Q313 repeatable. Q102: Dryad tears → Cobendell medicines → sentinels; Sword/Staff of Sentinel + shots/echoes. |
| Party kill quest | Q316 Destroy Plague Carriers (Ellenia 30155, elf 18+) | Live with `elven_fortress` wererats/Varool. 2 fangs per wererat, Varool fang cap 1; 30 adena/fang, 10000/Varool fang, +5000 at 10+. |
| Newbie Helper 30400 | | Existing gift / guide buff path. |
| Nerupa 30370 | | Q160 giver, elven newbie guide south of the newbie spawn. Page `data/html/default/30370.htm`; quest links in `data/quests.json`. |

## Elven newbie spawn: Nerupa 30370 + Newbie Helper 30400

Mobius CT 0 Interlude spawns both elven newbie guides from the **global**
`dist/game/data/spawns/InterludeSpawns.xml`, not from a per-territory NPC file (`ElvenTerritory/ElvenVillageNPCs.xml`
does not exist in this datapack). They are the only two NPCs within 1k of the elf creation point.

| NPC | Mobius row (`InterludeSpawns.xml`) | Distance to elf start |
|---|---|---|
| Nerupa 30370 (Folk, Q160) | `x="46048" y="41200" z="-3504" heading="17000" respawnDelay="60"` | 82 (x 46045, y 41251, z -3440) |
| Newbie Helper 30400 (Folk) | `x="46112" y="41200" z="-3504" heading="17000" respawnDelay="60"` | 106 |

They stand 64 apart on the same z/heading, so both fit in one client view. Object ids: 60474 (Nerupa) and
60475 (Helper) in the town band `[60000, 100000)`.

Measured against all six `classes.json` creation points for `races[1]` (Elven Fighter 18 / Elven Mystic 25):
Nerupa is 51..134 flat (82..149 in 3D), the Helper 47..134 flat. Both are inside one client view from every
elf spawn, so "she is not standing at the newbie spawn" is never a spawn-coordinate problem in this map.

If the Helper draws and Nerupa does not, the backend side is identical for both: same map, same
`EntitySpawn` path, same `Folk`/`attackable=false`/level-70 template, both at the Mobius row. The only
difference on the wire is `TemplateId` 30370 vs 30400, which the client maps to its own appearance data — so
a client-side missing/unbound appearance for 30370 looks exactly like "NPC not spawned". Check that first
before touching spawn data.

Source of truth: `scripts/gen_elven_newbie_guides.py` reads the vendored
`ref/mobius-ct0-interlude/data/spawns/InterludeSpawns.xml` (fallback `$MOBIUS_SRC` / `~/Downloads`), and
inserts-or-corrects those two rows in `data/spawns/elven_village.json` by coordinates, keeping the objectId.
It refuses to run if the two guides are not within 900 units of each other, so a Mobius change cannot silently
split the newbie spawn. `--check` exits 1 when the JSON has drifted (CI-safe).

Note on scope: the whole `elven_village.json` map is 82 rows, 81 of which match `InterludeSpawns.xml`
row-for-row (verified against the snapshot). Only Lynn 31414 is a village-file row. A regeneration of
`elven_village.json` through `tools/import_npc_templates.py` would drop these two rows plus the rest —
re-run `scripts/gen_elven_newbie_guides.py` afterwards.

Test: `tests/Fineage.Zone.Tests/NerupaSpawnTests.cs` asserts the coordinates/heading/respawn, that
`scripts/run-local-net.sh` enables `elven_village` and its bounds keep her, the 64-unit helper distance, that
she is within view of every elf creation point in `classes.json`, that she
is materialised as a live `Npc` entity in the entrant's knownlist at the Mobius spot, that she is selectable and
talkable (Q160 gate), and that the Net `EntitySpawn` frame decodes back as a visible, non-attackable
`Nerupa`/`30370`. The test builds its own single-map world instead of setting `FINEAGE_ZONE_MAPS`, because that
filter is process-global and xUnit runs collections in parallel.

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
1. More elven hunt quests beyond Q102/Q267/Q313/Q316. Q102 Sea of Spores Fever is live (#299).
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

Play reachability (hosted / `run-local-net.sh` default = **every enabled spawn map**):
- Asterios dialogs: yes. Q408 all branches: yes (`elven_fortress` carries Sukar Wererat Leaders).
- Grand Masters in Gludio/Gludin/Dion: yes when maps are enabled (default).
- Path quests Q406/Q407/Q409: Gludio/Gludin + `others_*` hunt maps are in the default full-world load.

Not done / next:
- Mobius `setClassId` extras: MagicSkillUse 5103 class-change effect broadcast, party/clan member-list refresh,
  `checkPlayerSkills` (drop skills above the new tree), sub-class / `setBaseClass` (we only have one class id).
- Net client has no dedicated class-change UI: everything is NpcHtml + UserInfo/SkillList it already handles.
- `FINEAGE_TRAINER_SP_LEARN` stays off by default. Hosted Zone defaults `FINEAGE_ZONE_AOI_CELL` to 3000.

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

**Play reachability:** path NPCs/mobs live in Gludio/Gludin/`others_*` — loaded by the default full-world Zone.
Q409 `replay_1` / `replay_2` use quest op `spawn_npc` (Mobius `addSpawn` + `addAttackDesire`) near Allana/Perrin.
Mobius `onAttack` script values (Q407 "only the attacker who hit it", Q409 "single attacker") are approximated by killer credit.

## Newbie helpers / village buffs (Mobius CT0 parity foundation)

- **Support Magic** (`data/newbie-buffs.json`, Mobius `handlers/bypasshandlers/SupportMagic.java`): levels 8..39, refused at class level 3, **no isNewbie check** (`requireNewbie: false`). Windows: Wind Walk 4322 8–39, Shield 4323 11–39; fighter 4324 12–38 / 4325 13–38 / 4326 14–38 / 4327 15–37; mage 4328 12–38 / 4329 13–38 / 4330 14–38 / 4331 15–37; Life Cubic 4338 16–36. Too low/high serves Mobius `default/SupportMagicLowLevel.htm` / `SupportMagicHighLevel.htm`. No cooldown/once rule in Mobius (re-castable).
- **Blessing of Protection** 5182: unchanged (≤39, refused at class level 2).
- **Newbie Travel Token** (`travelToken` block; Mobius `ai/others/NewbieTravelToken`): Newbie Guide 30599 `Script NewbieTravelToken` shows `newbie_travel/30599.htm` (level ≥20 → `cant-travel.htm`). A destination event takes one token 8542 and teleports (DE 30600, DW 30601, EV 30599, OV 30602, TI 30598). No token → SysMsg 351 (INCORRECT_ITEM_COUNT). Destinations outside loaded maps are refused **before** the token is taken (Fineage deviation; Mobius has the whole world).
- Stubs: `Script NpcLocationInfo` (Mobius `custom/`, not ported) still shows the unavailable page. Newbie first-talk gift (NewbieHelperGift) unchanged.
- Play: Elven Village → talk to Newbie Guide (30599) → Support Magic at lv 8–39; Travel Token link with token 8542 in bag at lv <20.

## Elven Underground Fortress (Q316 / Q408 mobs)

- Map `elven_fortress` (`data/spawns/elven_fortress.json`, 153 rows): the underground rows (z < -3700, x 5000..35000, y 60000..90000) of Mobius `spawns/Others/20_20.xml`, **moved** out of `others_20_20` by `scripts/gen_elven_fortress.py` (object ids kept, no duplicates; `others_20_20` keeps its 9 surface rows).
- Mobs: Sukar Wererat 20040 ×15, Sukar Wererat Leader 20047 ×11, Varool Foulclaw 27020 ×1 (300 s respawn), plus Shade Horror, Dark/Mist Terror, Dre Vanul, Baraq Orcs, Dungeon Skeletons, Dread Soldier, Kirunak 27021.
- `scripts/run-local-net.sh` default now loads it: `FINEAGE_ZONE_MAPS=…,elven_fortress,…` and a second bounds box `FINEAGE_ZONE_BOUNDS=20000,20000,80000,80000;5000,60000,35000,90000`. To leave it out, override `FINEAGE_ZONE_MAPS` without `elven_fortress` (the second box alone spawns nothing).
- Play: Elven Village gatekeeper → "Elven Fortress" (820 a, 29294, 74968, -3776 — already a loaded destination), then go down into the fortress.
- Unblocked: Q316 Destroy Plague Carriers (Ellenia 30155) — wererat fang drops from 20040/20047 and Varool's fang from 27020 (party kill credit already in place). Q408 Path of the Elven Wizard amethyst step — Sukar Wererat Leaders 20047.
- Gaps: no server geodata for 20_20 (Z from spawn rows, same as the other elven maps); the client has 20_20 terrain but the fortress interior geometry/walkability is unverified — if the client can't render the underground, players will only see mobs at their spawn Z. No admin teleport inside yet (gatekeeper drops you at the entrance).
