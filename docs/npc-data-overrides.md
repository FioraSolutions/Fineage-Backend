# NPC data overrides (deviations from Mobius CT 0)

`data/npcs.json` is transcribed from Mobius `L2J_Mobius_CT_0_Interlude` at the commit pinned in
`tools/import_npc_templates.py` (`PINNED_MOBIUS_COMMIT`). A field differs from that XML only when it is listed
here **and** in `NPC_OVERRIDES` in the importer, which refuses to run if the two lists disagree. No other override
exists. `importer --check` applies the same overrides, so CI treats them as intended.

## Tiny Baby family (16019–16023)

CT 0 gives all five the same placeholder block: level 99, HP 253165, MP 9999, P.Atk 4500, M.Atk 3240,
P.Def 3000, M.Def 2820, random 50, accuracy 9, MEN default. Its own `<acquire>` exp/sp
(2145/200, 2275/218, 2019/183) are the level 65–69 rewards, and every Interlude database lists normal
level 65–69 stats. The override restores those values. Exp/sp, speeds, collision, AI, skills and type stay CT 0.

| npcId | Name | Field | CT 0 | Override | Source |
|---|---|---|---|---|---|
| 16019 | Tiny Baby Buffalo | level / HP / MP | 99 / 253165 / 9999 | 67 / 2295.48154 / 1251 | lineage2scarlet.com/npc/list/16019 (L67, HP 2295, MP 1251, P.Atk 624, M.Atk 426, P.Def 281, M.Def 206, MEN 25); l2sanctum.net/wiki/interlude/monsters/tiny-baby-buffalo (L67, folded HP 3626 = 2295·CON) |
| 16019 | | P.Atk / M.Atk / P.Def / M.Def | 4500 / 3240 / 3000 / 2820 | 624.93199 / 426.74754 / 281.55251 / 206.02791 | same |
| 16020 | Tame Tiny Baby Buffalo | level / HP / MP | 99 / 253165 / 9999 | 67 / 2295.48154 / 1251 | lineage2scarlet.com/npc/list/16020; la2b.ru/npcs/tame-tiny-baby-buffalo-16020 (Interlude, L67, HP 2295, MP 1251) |
| 16020 | | P.Atk / M.Atk / P.Def / M.Def | 4500 / 3240 / 3000 / 2820 | 624.93199 / 426.74754 / 281.55251 / 206.02791 | lineage2scarlet.com/npc/list/16020 |
| 16021 | Tiny Baby Cougar | level / HP / MP | 99 / 253165 / 9999 | 69 / 2395.45533 / 1314 | lineage2scarlet.com/npc/list/16021 (L69, HP 2395, MP 1314, P.Atk 667, M.Atk 455, P.Def 291, M.Def 213); l2sanctum.net/wiki/interlude/monsters/tiny-baby-cougar (L69, folded HP 3784) |
| 16021 | | P.Atk / M.Atk / P.Def / M.Def | 4500 / 3240 / 3000 / 2820 | 667.50361 / 455.81844 / 291.15063 / 213.05139 | same |
| 16022 | Tame Tiny Baby Cougar | level / HP / MP | 99 / 253165 / 9999 | 69 / 2395.45533 / 1314 | lineage2scarlet.com/npc/list/16022 (L69, HP 2395, MP 1314, P.Atk 667, M.Atk 455, P.Def 291, M.Def 213, MEN 25) |
| 16022 | | P.Atk / M.Atk / P.Def / M.Def | 4500 / 3240 / 3000 / 2820 | 667.50361 / 455.81844 / 291.15063 / 213.05139 | same |
| 16023 | Tiny Baby Kookaburra | level / HP / MP | 99 / 253165 / 9999 | 65 / 2193.43142 / 1188.8 | lineage2scarlet.com/npc/list/16023 (L65, HP 2193, MP 1189, P.Atk 582, M.Atk 397, P.Def 271, M.Def 198); l2sanctum.net/wiki/interlude/monsters/tiny-baby-kookaburra (L65, folded HP 3465) |
| 16023 | | P.Atk / M.Atk / P.Def / M.Def | 4500 / 3240 / 3000 / 2820 | 582.81759 / 397.98887 / 271.89033 / 198.95754 | same |

All five also get hpRegen 7.5, mpRegen 2.7, random damage 10, XML accuracy 4.75 and MEN 25 instead of
13.43 / 3.09 / 50 / 9 / default. The full-precision numbers are the ones Mobius `CT_2.4_Epilogue` carries for
these ids; they match the Interlude databases above to the displayed integer.
Note: one Russian wiki (linedia.ru) lists a level 99 / 400 000 HP "Interlude" row for the *trained* variant; the
two databases with raw template data agree on the level 65–69 values used here.
