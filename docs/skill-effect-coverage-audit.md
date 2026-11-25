# Interlude skill effect coverage (Mobius CT_0)

**Sources:** `data/skill-effects.json`, Mobius `handlers/skill/effects/*.java`  
**Catalog:** `data/skill-effect-coverage.json` (keep in sync with `tools/audit_skill_effects.py`)  
**Regenerate:** `python3 tools/audit_skill_effects.py --write-defaults` then `python3 tools/audit_skill_effects.py`

## Summary

| Status | Effect types | Meaning |
|--------|-------------:|---------|
| full | 111 | Routed through shared combat / buff / toggle / utility pipelines |
| partial | 0 | — |
| unsupported | 1 | `#effectname1` corrupt datapack placeholder |
| missing | 0 | — |

Cast conditions: see [`skill-cast-condition-audit.md`](skill-cast-condition-audit.md) (37/37 XML kinds **full**).

## Force / sonic charges (PR family: resources)

| Mechanism | Mobius | Fineage |
|-----------|--------|---------|
| `FocusEnergy` cap `#maxCharges` | `Player.increaseCharges` | `ZoneWorldForceCharges` + `ForceChargeRules` |
| `player charges` gate | `ConditionPlayerCharges` | `SkillCastConditionEvaluator` + spend at cast start |
| 10 min decay | `ResetChargesTask` (600000 ms) | `TickForceChargeDecay` |
| Subclass swap | `_charges.set(0)` | `ClearForceCharges` on subclass activate |
| Olympiad enter | `clearCharges` in arena prep | `ClearForceCharges` in `EnterOlympiadArena` |
| Client display | `EtcStatusUpdate.charges` | `FanoutEtcStatusUpdate` |

## Handlers with no Interlude datapack row

| Effect | Gap |
|--------|-----|
| `ServitorShare` / `ApplySkillEffects` | Mobius handlers with no CT_0 skill row. Not a datapack status. |

## CI gate

`Fineage.Game.Tests.SkillEffectCoverageTests` fails when any effect name in `skill-effects.json` is **missing** or **uncataloged**.