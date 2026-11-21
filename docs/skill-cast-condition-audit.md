# Interlude skill cast-condition audit (Mobius CT_0)

**Source:** `L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills/*.xml`  
**Evaluator:** `SkillCastConditionEvaluator` + `ZoneWorld.SkillCastConditions` (`SkillCastCtx`)  
**Regenerate:** `python3 tools/audit_skill_cast_conditions.py` → `docs/skill-cast-condition-audit.json`

## Summary (2026-11-21)

| Area | Status |
|------|--------|
| Cast `<conditions>` tree (and/or/not, using, player, target, game) | All **37** attribute kinds used in Interlude XML are implemented in the shared evaluator |
| **`data/skills.json` `castCondition` import** (`gen_skill_cast_conditions.py`) | **189** skills imported (**29** compound AND/OR/NOT); **0** unsupported kinds in Interlude XML |
| **`data/skill-combat.json`** (`gen_skill_conditions.py`, full trees) | **69** combat rows with `castCondition` (**16** compound); flat `usingKinds` import removed |
| Legacy `usingKinds` on passive mastery (no Mobius `<conditions>`) | **39** skills; still gated via `Wearing()` when no `castCondition` tree is present |
| Force / sonic `player charges` + `FocusEnergy` + `EtcStatusUpdate.charges` | Shared spend at **cast start** (`ArmCastResources`); refusal leaves charges intact |
| `itemConsumeId` / `itemConsumeCount` | Pre-check SysMsg **351** on every `TrySkillUse`; consume at **hit time** via `LaunchConsumeItems` on `PendingSkillHit` |
| HP cast gate (`player hp`) | Evaluator + tests (Guts 139) |
| HP-scaled power (`DeathLink`) | `ZoneWorldSkillDamage.DeathLinkPowerMul` (existing) |
| Passive / toggle stat funcs (`hpBelow`, `resting`, positional) | `SkillStatEffectCatalog.Collect` / `CollectPassives` (existing) |
| Abnormal stack replacement | `ZoneAbnormals.Apply` by `abnormalType` (existing tests) |

## Condition type table (Mobius XML → pipeline)

See machine-readable [`skill-cast-condition-audit.json`](skill-cast-condition-audit.json). Top counts:

| Condition type | Skills | Implemented |
|----------------|-------:|:-------------:|
| `op:player` | 359 | Y |
| `op:and` | 148 | Y |
| `op:using` / `using.kind` | 103 | Y |
| `player.invsize` + `player.weight` | 102 each | Y |
| `player.insidezoneid` | 28 | Y (instance id + test hook) |
| `player.charges` | 13 | Y |
| `player.hp` | 6 | Y |
| `target.race` / `target.npcids` | 15 / 12 | Y |

## Force / sonic skills (`player charges`)

| Skill | Id | Notes |
|-------|---:|-------|
| Double Sonic Slash | 5 | charges in cast tree |
| Sonic Blaster | 6 | 2 charges |
| Sonic Storm | 7 | |
| Sonic Buster | 9 | |
| Force Burst | 17 | |
| Force Storm | 35 | |
| Triple Sonic Slash | 261 | |
| Hurricane Assault | 284 | |
| Force Meditation | 441 | |
| Sonic Barrier | 442 | |
| Force Barrier | 443 | |

Cap from Sonic Focus (8) / Focus Force (50) known levels; client shows charges on `EtcStatusUpdate`.

## Not in Interlude XML (fail-closed if imported later)

Mobius defines additional `player.*` attrs (olympiad, `ispvpflagged`, `castle`, …) that **do not appear** in CT_0 Interlude skill conditions. Unknown attrs log `skill={id} lv={lv} skill-cast-condition unsupported …` and refuse the cast.

## Client follow-up

- Grey unusable skills locally from the same condition data (charges, HP %, weight/inv, missing items).
- Show charge stacks consistently with server `EtcStatusUpdate.charges` (already sent when force charges change).