#!/usr/bin/env python3
"""Sync Mobius cast <conditions> trees into data/skill-combat.json as `castCondition`.

Full and/or/not trees (no flat usingKinds skip). Copies from data/skills.json when present
(same canonical trees as runtime SkillCastConditionEvaluator).

Usage: gen_skill_conditions.py <mobius skills xml dir>
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from mobius_condition_parse import conditions_tree_from_skill

ROOT = Path(__file__).resolve().parent.parent
SKILLS_JSON = ROOT / "data" / "skills.json"
COMBAT_JSON = ROOT / "data" / "skill-combat.json"


def load_skill_bodies(src: Path) -> dict[int, ET.Element]:
    bodies: dict[int, ET.Element] = {}
    for path in sorted(src.glob("*.xml")):
        root = ET.parse(path).getroot()
        for node in root.findall("skill"):
            bodies[int(node.get("id"))] = node
    return bodies


def main() -> None:
    src = Path(sys.argv[1])
    combat = json.loads(COMBAT_JSON.read_text())
    skills_by_id = {
        s["skillId"]: s.get("castCondition")
        for s in json.loads(SKILLS_JSON.read_text()).get("skills", [])
    }
    bodies = load_skill_bodies(src)

    imported = compound = from_catalog = skipped = 0
    for row in combat["skills"]:
        row.pop("usingKinds", None)
        row.pop("castCondition", None)
        sid = row["skillId"]
        tree = skills_by_id.get(sid)
        if tree:
            from_catalog += 1
        else:
            skill = bodies.get(sid)
            if skill is not None:
                tree = conditions_tree_from_skill(skill)
        if not tree:
            continue
        row["castCondition"] = tree
        imported += 1
        if tree.get("op") in ("and", "or", "not"):
            compound += 1
        elif tree.get("op") == "using" and tree.get("kinds"):
            pass
        else:
            skipped += 0

    COMBAT_JSON.write_text(json.dumps(combat, indent=1, ensure_ascii=False) + "\n")
    print(
        f"castCondition on {imported} combat skills ({compound} compound; "
        f"{from_catalog} from skills.json) → {COMBAT_JSON.name}"
    )


if __name__ == "__main__":
    main()