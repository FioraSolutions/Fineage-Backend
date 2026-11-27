# Required game data (#985)

AGENTS.md forbids silent fallbacks for missing game data. This document is the
versioned registry and policy for that rule.

## Registry

Source of truth: [`data/required-data-registry.json`](../../data/required-data-registry.json).

Loaded by `Fineage.Game.RequiredData.RequiredDataRegistry`. Every
`RequiredDataKind` enum member must appear in the JSON (parse fails otherwise).

| Kind | File / pack | Policy |
|------|-------------|--------|
| skill | `skills.json` | refuse-action |
| item | `items.json` | refuse-action |
| npc | `npcs.json` | refuse-action |
| teleport | `teleports.json` | refuse-action |
| droplist | `drops/` | refuse-action |
| skill-passive | `skill-passives.json` | refuse-action |
| npc-ai | `npc-ai-support.json` | refuse-action |
| quest | `quests/` | refuse-action |
| spawn | `spawns/` | fail-fast-boot |
| buylist | `buylists.json` | refuse-action |
| multisell | `multisell.json` | refuse-action |
| npc-script | `npc-scripts.json` | refuse-action |

## Policies

- **fail-fast-boot** — Zone `Program` calls `Required.Registry.AssertBootPacksPresent`
  before creating the world. A missing pack exits with a named error.
- **refuse-action** — Lookups that need a row call `Required.RequireValue` /
  `SkillCatalog.Require` / `TeleportCatalog.RequireLists`. Missing keys throw
  `RequiredDataException` naming kind, key, and file.

## Boot cross-check

`Fineage.Zone.RequiredData.RequiredDataCrossCheck` runs in Zone `Program` right after
`AssertBootPacksPresent`. It resolves every id one catalog uses to point at another and
fails boot with **all** dangling references listed (`kind key <- source file: detail`):

| Source | Resolves |
|--------|----------|
| `spawns/*.json`, `raidbosses.json`, `npc-minions.json`, `summons.json` | npc (+ summon cost/upkeep items) |
| `drops/npc-*.json` | owner npc, every drop item |
| `skill-trees.json`, `fishing.json` skillTree, `pledge-skills.json` | skill, learn items |
| `item-equip-skills.json`, `herbs.json`, `cursed-weapons.json`, `item-skills.json` | item, skill |
| `buylists.json`, `multisell.json`, `recipes.json` | item (multisell pseudo-ids ≤ 0 are points, not items), buylist npcs |
| `npc-ai-support.json` | npc, every cast skill |
| `html/**/*.htm` (not `html/admin/`) | `Buy`/`Wear` → buylist (and that the list allows the page's npc), `multisell`/`exc_multisell` → list, `teleport LIST` → the page npc's list in `teleports.json`, `Script <name>` → `npc-scripts.json`, `Script <n>` → `quests/` |

The CI gate is `RequiredDataCrossCheckTests.Shipped_data_has_no_dangling_references`
(fast unit tests, every dev PR). Holey fixtures for every source above assert the error
names the key and the referencing file.

### knownAbsent

A registry kind may list `knownAbsent: [{key, reason}]` for references the **originals
themselves** leave dangling (Mobius points at an id it never defines). A row without a
reason is rejected at parse; a listed key that is no longer referenced or is now defined is
itself a violation, so the list cannot rot. Current rows: skill 3498, buylist
`3198900@32007`, multisell 517, and npc-scripts `0`, `225_TestOfSearcher`, `IceFairySirra`
(Mobius-dangling) plus `ToIVortex`, `EchoCrystals`, `ValakasTeleporters`, `Baium`,
`Frintezza`, `PaganTeleporters` (Mobius scripts not ported yet, #919). The action that
needs one still refuses with a named error at runtime.

## Audit (2026-11-27)

Data holes the cross-check found on first run, and how they were closed:

| Hole | Count | Fix |
|------|------:|-----|
| items referenced by buylists / multisell / recipes / clan skills / herbs, absent from `items.json` | 771 | imported from Mobius `stats/items` (`scripts/gen_gmshop_missing_items.py --all-referenced`) |
| skills referenced by equip skills (82), clan skills (22), herbs (8), cursed weapons (2), absent from `skills.json` | 113 | imported from Mobius `stats/skills` (`tools/import_skills.py --add-missing`, referenced set widened) |
| html bypass targets (scripts 9, buylist allowance 1, multisell 1) | 11 | `knownAbsent` with reason (above) |

Code sites that substituted a default for a data-driven id, migrated to `Require`:

| Area | Sites | Old behaviour |
|------|------:|---------------|
| skills | 17 | `"Skill {id}"` names, `""` abnormal slot, skipped clan/noble/hero grants, unknown-is-buff dispel, cursed weapon max level 10, wedding level 0 |
| items | 30 | `"item {id}"` / id-as-name, skipped olympiad/hero/academy/lord/village-master/newbie rewards, skipped drops (`drop skip unknown item`), enchant crystal dropped to 0, weight 0, not-stackable |
| npc templates | 14 | `summon/pet/quest spawn skip unknown npcId`, race runner skipped, siege/festival/signet spawn silently absent, `KeyNotFoundException` without file |
| quests | 1 | noblesse gate quest missing → `bad_request` |

Left as-is on purpose (not game-data defaults): GM/user-input validation (`//create`,
admin buy, pet restore of persisted rows), persisted player state that names a removed id
(learned skills, stored effects — #984 owns state coherence), transport `catch {}` in
Net/Login (socket shutdown), and genuinely optional catalogs (`npc-ai-support` row absent =
no support casts, no droplist = no drop, abnormal visual effect = cosmetic).

## Metric

`fineage.data.silent_fallback` (OpenTelemetry counter, tag `kind`) must stay at
**0**. Call `Required.NoteSilentFallback` only while migrating a leftover silent
site; new code must refuse instead.

## Extending

1. Add a `RequiredDataKind` value and a matching JSON row (same `id`).
2. Point call sites at `Required.RequireValue` / catalog `Require*`.
3. Add a holey-data test in `RequiredDataRegistryTests` for that kind.
4. Do not add `?? default` for that kind without a comment that it is **not**
   game data (transport defaults, test fakes, etc.).

## Status

Registry, fail-fast boot packs, boot cross-check + CI gate, and the data-driven call
sites above are on the refuse path. The last `NoteSilentFallback` caller (cursed weapon
skill 3603) is gone now that the template is imported.
