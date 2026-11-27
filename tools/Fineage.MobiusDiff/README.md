# Fineage ↔ Mobius CT0 differential harness

One executable scenario drives both sides:

1. **Fineage side** — `FineageProbe` reuses the production pure combat/stat layers
   (`PlayerEffectiveAttributes`, `PlayerGearBases`, `PlayerFormulaBases`, `CombatStatMods`,
   `SkillStatEffectCatalog`, `ZoneCombat`) in the same order as `ZoneWorldStats.PlayerStats`.
2. **Mobius oracle** — `java/MobiusFormulaDriver.java` reads the Mobius reference XML
   (`chars/baseStats`, `items`, `npcs`, `skills`, `statBonus.xml`) and evaluates the cited pure
   formula layer (`Formulas.calcPhysDam/calcCrit/calcHitMiss`, `FuncPAtkMod`, `FuncPDefMod`,
   `FuncAtkAccuracy`, `FuncAtkEvasion`, `FuncAtkCritical`, `FuncPAtkSpeed`). It is an independent
   evaluator of Mobius **behavior**; no GPL implementation code is copied into this repository.

Both sides emit the same normalized metric names. The runner compares them with
`|actual − expected| <= absolute + relative·|expected|` and writes JSON + Markdown reports with
parameter, expected, actual, tolerance, delta and PASS/FAIL.

## Run

```bash
# all scenarios (writes local-logs/mobius-diff/*.json + *.md)
dotnet run --project tools/Fineage.MobiusDiff -- --all

# one scenario
dotnet run --project tools/Fineage.MobiusDiff -- \
  --scenario tools/Fineage.MobiusDiff/scenarios/physical-attack-human-fighter-40.json

# regenerate committed golden metrics from a readable Mobius ref tree
FINEAGE_MOBIUS_REF=/path/to/mobius-ct0-interlude
dotnet run --project tools/Fineage.MobiusDiff -- --all --refresh-oracle --ref "$FINEAGE_MOBIUS_REF"
```

Exit code is `0` only when every selected scenario passes. Reports are ignored local artifacts
(`local-logs/`). The fast unit tests are ordinary xunit tests and belong in the normal PR gate:

```bash
dotnet test tests/Fineage.MobiusDiff.Tests/Fineage.MobiusDiff.Tests.csproj
```

No PostgreSQL, GameServer boot, load test or zone simulation is involved, so heavy full-server
runs never block PRs.

## Scenarios

`scenarios/*.json` is the only scenario format. A scenario defines:

- `id`, `description`, `seed`, `samples` — `seed` makes the Fineage sampled statistic repeatable.
- `attacker`: `classId`, `level`, fixed `gear` (`slot`, `itemId`, `enchant`) and active `buffs`
  (`skillId`, `level`).
- `target`: `npcId`.
- `contact`: `side` (`front` / `side` / `behind`), `soulshot`, `attackerBow`.
- `oracle`: `kind` (`mobius-formula-java`), `golden` path, Mobius `reference` path.
- `metrics` (optional allowlist) and `tolerances` (`default` plus per-metric `absolute` /
  `relative`).
- `knownDivergences` (optional): report notes that explain a deliberate red row. They never hide a
  failure and never change the PASS/FAIL verdict.

### Metrics

| Metric | Meaning |
|---|---|
| `pAtk`, `pDef`, `pAtkSpd` | final player stat after gear/formula/buff folds |
| `accuracy`, `evasion` | client-visible integer player values |
| `critPermille` | raw critical rate before the 500 ‰ cap |
| `hitChancePermille`, `critChancePermille` | Mobius roll thresholds |
| `damageNoCrit` | one front non-critical plain hit (integer HP) |
| `damageMeanPerSwing` | seeded Fineage sample mean vs Mobius analytic mean (hit × crit × random mean) |

Individual random rolls are never compared. Statistical rows compare a seeded sample mean with
the Mobius analytic expectation; deterministic rows compare formula outputs directly.

### Adding a scenario

1. Add `scenarios/<id>.json` and a golden path under `oracle/<id>.json`.
2. Run `--refresh-oracle --ref <mobius-ref>` with a JDK. If the ref tree is unavailable, keep the
   scenario out of the PR; the harness reports the precise missing dependency instead of inventing
   a Mobius result.
3. Add or reuse a xunit assertion in `tests/Fineage.MobiusDiff.Tests`. Prove both directions:
   a passing dev case and a test-only negative-control mutation that must fail. Do not commit a
   gameplay mutation for the control.

## Pilot and current status

`physical-attack-human-fighter-40` is the pilot: Human Fighter 40, Long Sword, Mithril heavy set,
Might 3 / Focus 3 / Haste 2, front melee against Gremlin `20001`.

It currently reports **FAIL only on damage**, because Fineage uses `70·pAtk/pDef` for a plain
attack and does not apply Mobius proximity, while CT0 `Formulas.calcPhysDam` uses
`76·damage·proximity/defence`. That is a real formula-level divergence owned by #979, not a
random-roll mismatch. The deterministic stat/probability pilot (`physical-stats-human-fighter-40`)
passes and is the harness's green dev case.

`tests/.../Negative_control_mutation_makes_the_comparator_fail` mutates the Fineage-side metric in
memory only; the comparator turns red, proving it can fail.

## Artifact provenance and oracle limits

- Golden files record the Mobius `reference` path, driver, formula names and whether a full server
  was run.
- The formula driver is **not** a full GameServer execution. Full Mobius startup is blocked in this
  tree because `ref/mobius-ct0-interlude/dist/libs` is absent (`build.xml` / `.classpath` require
  `mariadb-java-client-2.7.5.jar`) and the server needs MySQL. The golden therefore says
  `fullServerVerified: false` and names the blocker; formula-level acceptance is verified, full
  server parity is not claimed.
- `ref/` is read-only and gitignored. Golden files are the committed, reviewable output; regenerate
  them only from the reference tree and review the diff.
