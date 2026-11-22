# Interlude (CT_0) skill data purge

Authoritative skill ids: **L2J Mobius `L2J_Mobius_CT_0_Interlude`**, `dist/game/data/stats/skills/**/*.xml` (including `custom/`). The committed whitelist is `data/interlude-skill-ids.tsv` (regenerate with `python3 tools/gen_interlude_skill_whitelist.py` after `./scripts/fetch-mobius-interlude.sh`).

`GameData.Load` and `InterludeSkillWhitelistTests` reject any `data/**/*.json` row whose `skillId` (or nested `skill.id` / `cubic.id`) is not on that whitelist.

## Removed skill ids

| Id | Name (datapack label) | Where it was | Why not Interlude |
| --- | --- | --- | --- |
| 1531 | Blessed Blood | `data/skill-transfer-trees.json` (Bishop / Eva Saint transfer rows) | Present on Mobius **High Five** `transferSkillTree.xml` only; no skill `1531` in CT_0 Interlude `stats/skills` |
| 1539 | Stigma of Shilien | `data/skill-transfer-trees.json` (same) | Same — CT2.5+ transfer tree import, absent from CT_0 Interlude skills XML |

Suspect High Five / Classic combat skills from the audit brief (`10291`–`10294`, `10308`, `10322`, `10302`, `10318`, `10310`, `10305`, `10064`, `10081`, `10090`) were **not** present in Fineage data at purge time.

## Kept (ambiguous) ids

None: every row in `data/skills.json` matches Mobius CT_0 Interlude id, name, and level count for ids we ship. `skills.json` intentionally contains **784** of **2700** Interlude skills (combat/support subset); ids are still validated against the full whitelist when referenced elsewhere.

## Counts (2026-11-22 purge)

| Metric | Count |
| --- | ---: |
| Interlude whitelist (`interlude-skill-ids.tsv`) | 2700 |
| `skills.json` definitions | 784 |
| `skill-trees.json` distinct skill ids | 622 |
| Removed transfer-tree rows | 4 (2 unique skill ids × 2 classes) |
| Non-Interlude `skillId` references after purge | 0 |