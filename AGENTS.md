# Fineage-Backend — agent entry

C#/.NET server (Login TCP/HTTP, Zone). Land via PR into **`dev`**; merge when CI is green.

## READ FIRST: non-negotiable rules (every task, every PR)

1. **Fix the mechanism, not the symptom.** For any reported bug, find the system that produces the behavior, generalize it, and fix it in the shared architecture (extend or refactor the existing pipeline if needed). No per-case patches, no special-case `if`s for one race/class/item/NPC, no stubs, no hacks. If the right fix needs an architectural change, make it (or propose it in the PR) instead of papering over.
2. **No silent fallbacks.** Missing data/asset/table/id must log a concrete error naming what is missing. Never substitute a default, alias or placeholder quietly.
3. **Originals are the source of truth.** Gameplay rules from Mobius CT0 Interlude; client look from the original Interlude client. UI = original window layout + original textures, pixel-perfect, with an original-vs-Fineage screenshot in every UI PR (details below).
4. **Use the extracted reference catalog first** (client repo `l2-unity/Docs/interlude-ref/`, `l2-unity/Docs/ui-original/`; Mobius data in `ref/`). Do not re-extract or guess what is already catalogued; if something is missing, add it to the catalog with the extraction script.
5. **Interlude only.** Nothing from later chronicles (data, animations, effects, skills).
6. Every PR description states the root cause and which shared mechanism was changed. PRs that patch a single case of a general problem will be rejected.
7. **Lossless extraction from originals.** When extracting anything from the original Interlude client or server data (xdat, .usk/.ukx/.utx, .dat, nwindow.dll, Mobius data), dump EVERY property of EVERY object into docs/interlude-ref (or the backend equivalent) as a full lossless dump. Never keep only the fields you currently need. Derived tables are views over the full dump. Every extraction also updates a gap report listing original fields and features Fineage doesn't use or support yet, with affected counts. Don't commit original binaries.

## Parity

Server business logic must match L2J Mobius CT0 Interlude (`ref/mobius-ct0-interlude/`, gitignored). Look the mechanic up there (or on the web) before implementing; do not guess formulas, AI or spawn rules.

## Performance

Zone tick budget is 200 ms; local playtests run the **Debug** build, so performance work must hold in Debug (fix algorithms, not build flags).

## CI (dev PRs)

**Merge gate on PRs into `dev`:** `build-and-test` (solution test + Postgres), `format-check`, and correctness tests only — no load or zone perf jobs by default.

**Perf / load workflows** (`load-test-smoke`, `zone-simulation-perf`, `zone-parallel-regions`) run on PRs into `main`, push to `main`, nightly UTC schedule on `dev`, `workflow_dispatch`, or on a `dev` PR labeled **`perf`**. Issue **#867** perf-chain PRs must add the `perf` label to get load-test gate numbers in CI.

## Model selection

| Work | Model |
|------|--------|
| Light (small UI tweaks, docs, merges, sync) | **Composer** (`composer-2.5`) |
| Medium-light (simple contained fixes, tests, CI fixes) | **DeepSeek V4.1 Flash** (`deepseek-v41-flash-1017`) |
| Medium (contained bugfixes, features inside existing architecture) | **Grok** (`grok-4.7`) |
| Hard (architecture, problems the above cannot solve) | **Kimi K3** (`kimi-k3`) or **Claude** (Opus/Sonnet, sparingly — expensive) |

**If a model is unavailable** (quota/outage), use the fallback: Kimi K3 → Grok; Claude → Grok; DeepSeek V4.1 Flash → Composer.
