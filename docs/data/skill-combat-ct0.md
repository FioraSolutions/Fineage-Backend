# `data/skill-combat.json` and `data/npc-ai-support.json` — CT 0 pin

Both files are now reproducible from the pinned Mobius CT 0 Interlude datapack and checked in CI
(`npc-data-check`):

```
python3 tools/import_skill_combat.py   <CT_0 checkout> [--write]   # --check by default
python3 scripts/gen_npc_ai_support.py  <CT_0 checkout> [--check]
```

Both go through `tools/mobius_pin.py`: the path must be inside `L2J_Mobius_CT_0_Interlude` and at the pinned commit
(or an identical `stats/skills` / `stats/npcs` tree). Other chronicles are refused.

## What was wrong

The Nov 4 import (#195) mixed Epilogue/HighFive NPC-skill numbers into `skill-combat.json` (50 rows) and generated
`npc-ai-support.json` from the HighFive XML layout. HighFive folds the initial MP cost into `mpConsume`
(CT 0 keeps `mpInitialConsume` separate, and the zone adds both), changes Valakas / Frintezza / Sailren power and
magicLevel, gives Cubic Drain (4049) 16 ranks instead of 8, and makes the four elemental-stone item skills
(3165–3171) magic with a 4000 ms hit time (CT 0: static `isMagic` 2, 1000 ms).

## Curse of Lake Ghost (4382)

`mpConsume` was `13 20 28 38 50 61 71 76 80 83 84 85`, the shared table of CT 0's other NPC skills (4032 NPC Strike,
4067, 4072, 4101, 4232, 4244, 4377, 4472, 4492, 4573 …). 4382 itself is `14 22 31 43 55 68 79 84 89 92 94 95`
in CT 0, Epilogue, HighFive and C4. The value entered in the initial commit `98c3e6f9` (Net foundation roll-out,
Nov 4), not from any datapack row for 4382: it is the shared NPC mp table copied onto the wrong skill. CT 0 value restored,
and CT 0's `effectRange` 200 (missing in the file) added.

## Guarded fields (`skill-combat.json`)

`name, levels, power[], mpConsume[], mpInitialConsume[], hitTimeMs, coolTimeMs, castRange, effectRange, magic,
overHit, magicLevel[]`. `magicLevel` -1 is Fineage's "no level check" sentinel (== CT 0 0). `usingKinds`,
`reuseDelayMs` and the hand-added fields (`kind`, `blow*`, lethal, `castCondition`) are not checked.
