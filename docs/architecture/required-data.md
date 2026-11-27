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

## Policies

- **fail-fast-boot** — Zone `Program` calls `Required.Registry.AssertBootPacksPresent`
  before creating the world. A missing pack exits with a named error.
- **refuse-action** — Lookups that need a row call `Required.RequireValue` /
  `SkillCatalog.Require` / `TeleportCatalog.RequireLists`. Missing keys throw
  `RequiredDataException` naming kind, key, and file.

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

Foundation landed with skill / teleport require helpers and spawn fail-fast boot.
Remaining catalog call sites migrate onto the same refuse path; the silent-fallback
counter tracks leftovers until they are gone.
