#!/usr/bin/env python3
"""Resolve per-level ``#table`` powers of Mobius instant recovery effects in data/skill-effects.json.

Mobius writes ``<effect name="ManaHeal"><power>#amount</power></effect>`` with ``<table name="#amount">``
on the skill; skill-effects.json kept the literal "#amount", so the zone had no amount to restore. This
replaces such strings with the per-level value list (transcribed values only, no GPL source).

Usage: sync_recovery_effect_powers_from_mobius.py [--skills-dir DIR]
"""
from __future__ import annotations

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SKILLS = Path("/workspace/mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills")
EFFECTS_JSON = ROOT / "data/skill-effects.json"

# Must stay in lock-step with ZoneWorld.RecoveryEffectNames.
RECOVERY = {
    "Heal", "HealPercent", "CpHeal", "CpHealPercent", "ManaHeal", "ManaHealPercent", "ManaHealByLevel",
}


def number(text: str):
    v = float(text)
    return int(v) if v.is_integer() else v


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skills-dir", type=Path, default=DEFAULT_SKILLS)
    args = ap.parse_args()

    tables: dict[int, dict[str, list[str]]] = {}
    for f in sorted(args.skills_dir.glob("*.xml")):
        for sk in ET.parse(f).getroot().iter("skill"):
            tables[int(sk.get("id"))] = {
                t.get("name"): (t.text or "").split() for t in sk.findall("table") if t.get("name")
            }

    data = json.loads(EFFECTS_JSON.read_text())
    patched = 0
    for row in data["skills"]:
        for effect in row["effects"]:
            power = effect.get("power")
            if effect["name"] not in RECOVERY or not isinstance(power, str) or not power.startswith("#"):
                continue
            values = tables.get(row["skillId"], {}).get(power)
            if not values:
                print(f"skill {row['skillId']} {effect['name']}: table {power} not found")
                continue
            effect["power"] = [number(v) for v in values]
            patched += 1
    EFFECTS_JSON.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    print(f"recovery powers resolved: {patched}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
