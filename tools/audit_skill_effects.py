#!/usr/bin/env python3
"""Audit Mobius CT_0 skill <effect name="…"> vs Fineage handler coverage.

Reads data/skill-effects.json and data/skill-effect-coverage.json (status per effect type).
Regenerate coverage entries with --write-defaults when Mobius adds new effect names.

Usage:
  python3 tools/audit_skill_effects.py [--write-defaults] [--mobius-handlers DIR]

Prints summary to stdout and writes docs/skill-effect-coverage-audit.json.
"""
from __future__ import annotations

import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EFFECTS_PATH = ROOT / "data" / "skill-effects.json"
COVERAGE_PATH = ROOT / "data" / "skill-effect-coverage.json"
OUT_PATH = ROOT / "docs" / "skill-effect-coverage-audit.json"
MOBIUS_DEFAULT = (
    ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/scripts/handlers/skill/effects"
)

# Keep in sync with SkillEffectCoverageTests / SkillEffectCoverageCatalog.cs
FULL_ROUTES = {
    # Combat pipeline (skill-combat.json + ZoneWorldSkillDamage)
    "PhysicalAttack",
    "MagicalAttack",
    "EnergyAttack",
    "FatalBlow",
    "Backstab",
    "PhysicalAttackHpLink",
    "DeathLink",
    "StaticDamage",
    "MagicalDamageMp",
    "MagicalAttackMp",
    "PhysicalDamage",
    "PhysicalDamageHpLink",
    "PhysicalDamageMute",
    "EnergyDamage",
    # Abnormal apply (buff / debuff / CC)
    "Buff",
    "Debuff",
    "Stun",
    "Root",
    "Sleep",
    "Fear",
    "Mute",
    "PhysicalMute",
    "Paralyze",
    "Petrification",
    "Bluff",
    "Confuse",
    "Distrust",
    "Disarm",
    "Betray",
    "SkillTurning",
    "Hide",
    "ImmobileBuff",
    "ImmobilePetBuff",
    "Invincible",
    "EnlargeAbnormalSlot",
    "AttackTrait",
    "DefenceTrait",
    "ResistSkill",
    "DetectHiddenObjects",
    "Detection",
    "Flag",
    "Grow",
    "MaxHp",
    "MaxCp",
    "Lucky",
    "CubicMastery",
    "EnableCloak",
    "ChangeFace",
    "ChangeHairColor",
    "ChangeHairStyle",
    "ChangeFishingMastery",
    "CrystalGradeModify",
    "DamOverTime",
    "DamOverTimePercent",
    "ManaDamOverTime",
    "CpHealOverTime",
    "HealOverTime",
    "ManaHealOverTime",
    "Passive",
    # Recovery
    "Heal",
    "HealPercent",
    "CpHeal",
    "CpHealPercent",
    "ManaHeal",
    "ManaHealPercent",
    "ManaHealByLevel",
    "Recovery",
    "RebalanceHP",
    # Toggles
    "FakeDeath",
    "SilentMove",
    "Relax",
    "ChameleonRest",
    "MpConsumePerLevel",
    "Pumping",
    "Reeling",
    # Force / charges
    "FocusEnergy",
    # Dispel / cancel family
    "DispelBySlot",
    "DispelByCategory",
    "DispelAll",
    "DispelBySlotProbability",
    # Hostile utility (ZoneWorldSkills.Effects / derangement)
    "GetAgro",
    "TargetMe",
    "TargetMeProbability",
    "TargetCancel",
    "DeleteHate",
    "DeleteHateOfMe",
    "RandomizeHate",
    "AddHate",
    "TransferHate",
    "CpDamPercent",
    "Lethal",
    "RunAway",
    "HpDrain",
    "HpByLevel",
    "ConsumeBody",
    "PolearmSingleTarget",
    # Summon / spoil / utility casts
    "Summon",
    "SummonNpc",
    "SummonCubic",
    "SummonPet",
    "SummonTrap",
    "SummonAgathion",
    "Unsummon",
    "UnsummonAgathion",
    "Spoil",
    "Sweeper",
    "Sow",
    "ElementSeed",
    "Restoration",
    "RestorationRandom",
    "Resurrection",
    "ResurrectionSpecial",
    "OpenChest",
    "OpenDoor",
    "Escape",
    "Teleport",
    "TeleportToTarget",
    "CallPc",
    "CallParty",
    "CallSkill",
    "TakeCastle",
    "HeadquarterCreate",
    "ClanGate",
    "Ride",
    "Fishing",
    "Harvesting",
    "GiveSp",
    "VitalityPointUp",
    "OpenCommonRecipeBook",
    "OpenDwarfRecipeBook",
    "TrapDetect",
    "TrapRemove",
    "TransferDamage",
    "StealAbnormal",
    "BlockParty",
    "BlockChat",
    "BlockResurrection",
    # Damage triggers on abnormal
    "TriggerSkillByDamage",
    "TriggerSkillByDamageDealt",
    "TriggerSkillByDamageReceived",
    "TriggerSkillBySkill",
    "TriggerSkillByAvoid",
    "TriggerSkillByAttack",
    "BlockBuffSlot",
    "NoblesseBless",
    "ProtectionBlessing",
}

PARTIAL = {
    "ServitorShare": "pet stat share not implemented",
    "ApplySkillEffects": "nested skill apply not implemented",
}

# Interlude datapack typos / unused Mobius handlers with zero XML rows
DOCUMENTED_UNSUPPORTED = {
    "#effectname1": "corrupt datapack row skill 5404",
    "FocusMaxEnergy": "no CT_0 Interlude skill XML row",
}


def load_effect_counts() -> tuple[Counter, dict[str, set[int]]]:
    root = json.loads(EFFECTS_PATH.read_text(encoding="utf-8"))
    skills = root["skills"]
    counts: Counter = Counter()
    by_skill: dict[str, set[int]] = defaultdict(set)
    for row in skills:
        sid = int(row["skillId"])
        for eff in row.get("effects", []):
            name = eff["name"]
            counts[name] += 1
            by_skill[name].add(sid)
    return counts, by_skill


def load_coverage() -> dict:
    if not COVERAGE_PATH.exists():
        return {"effects": {}, "documentedUnsupported": list(DOCUMENTED_UNSUPPORTED.keys())}
    return json.loads(COVERAGE_PATH.read_text(encoding="utf-8"))


def default_status(name: str) -> str:
    if name in DOCUMENTED_UNSUPPORTED:
        return "unsupported"
    if name in PARTIAL:
        return "partial"
    if name in FULL_ROUTES:
        return "full"
    return "missing"


def write_defaults(counts: Counter) -> None:
    effects = {}
    for name in sorted(counts.keys()):
        status = default_status(name)
        entry = {"status": status}
        if status == "partial":
            entry["note"] = PARTIAL[name]
        elif status == "unsupported":
            entry["note"] = DOCUMENTED_UNSUPPORTED[name]
        effects[name] = entry
    doc = {
        "reference": "Mobius CT_0 Interlude effect handlers vs Fineage zone pipelines",
        "effects": effects,
        "documentedUnsupported": sorted(DOCUMENTED_UNSUPPORTED.keys()),
    }
    COVERAGE_PATH.write_text(json.dumps(doc, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {COVERAGE_PATH} ({len(effects)} effect types)")


def mobius_handler_names(handler_dir: Path) -> set[str]:
    if not handler_dir.is_dir():
        return set()
    return {p.stem for p in handler_dir.glob("*.java")}


def main() -> None:
    write = "--write-defaults" in sys.argv
    counts, by_skill = load_effect_counts()
    if write:
        write_defaults(counts)

    coverage = load_coverage()
    catalog = coverage.get("effects", {})
    unsupported = set(coverage.get("documentedUnsupported", []))

    rows = []
    status_counts: Counter = Counter()
    missing_names: list[str] = []

    for name, count in counts.most_common():
        meta = catalog.get(name)
        if meta is None:
            status = "uncataloged"
            note = "add to skill-effect-coverage.json"
        else:
            status = meta.get("status", "missing")
            note = meta.get("note", "")
        status_counts[status] += 1
        if status in ("missing", "uncataloged"):
            missing_names.append(name)
        rows.append(
            {
                "effect": name,
                "instances": count,
                "skills": len(by_skill[name]),
                "status": status,
                "note": note,
            }
        )

    handler_dir = MOBIUS_DEFAULT
    for i, arg in enumerate(sys.argv):
        if arg == "--mobius-handlers" and i + 1 < len(sys.argv):
            handler_dir = Path(sys.argv[i + 1])

    java_handlers = mobius_handler_names(handler_dir)
    xml_names = set(counts.keys())
    mobius_only = sorted(java_handlers - xml_names - {"AbstractEffect"})
    xml_only = sorted(xml_names - java_handlers)

    report = {
        "effectTypesInDatapack": len(counts),
        "statusSummary": dict(status_counts),
        "missingOrUncataloged": missing_names,
        "rows": rows,
        "mobiusJavaHandlers": len(java_handlers),
        "handlersWithoutDatapackRow": mobius_only[:50],
        "datapackWithoutJavaHandler": xml_only,
    }
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUT_PATH.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    print(f"Datapack effect types: {len(counts)}")
    for st, n in sorted(status_counts.items(), key=lambda x: (-x[1], x[0])):
        print(f"  {st}: {n}")
    if missing_names:
        print(f"FAIL catalog: {', '.join(missing_names)}")
        sys.exit(1)
    print(f"Wrote {OUT_PATH}")


if __name__ == "__main__":
    main()