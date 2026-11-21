# Interlude skill effect coverage (Mobius CT_0)

**Sources:** `data/skill-effects.json`, Mobius `handlers/skill/effects/*.java`  
**Catalog:** `data/skill-effect-coverage.json` (keep in sync with `tools/audit_skill_effects.py`)  
**Regenerate:** `python3 tools/audit_skill_effects.py --write-defaults` then `python3 tools/audit_skill_effects.py`

## Summary

| Status | Effect types | Meaning |
|--------|-------------:|---------|
| full | 108 | Routed through shared combat / buff / toggle / utility pipelines |
| partial | 3 | `BlockBuffSlot` family — abnormal lands; slot mutex not enforced |
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

## Partial effect families (client / follow-up)

| Effect | Gap |
|--------|-----|
| `BlockBuffSlot` / `BlockAbnormalSlot` | Re-buffing blocked slots should refuse or replace per Mobius `BlockBuffSlot.java` |
| `NoblesseBless` / `ProtectionBlessing` | Noblesse death penalty / blessing consume messaging |
| `ServitorShare` / `ApplySkillEffects` | Pet share + nested apply handlers |

## CI gate

`Fineage.Game.Tests.SkillEffectCoverageTests` fails when any effect name in `skill-effects.json` is **missing** or **uncataloged**.