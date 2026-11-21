#!/usr/bin/env python3
"""Import Mobius CT_0_Interlude skill <conditions> trees into data/skills.json as `castCondition`.

Transcribes and/or/not, using, player, target, and game nodes (Mobius DocumentBase.parseCondition).
Skips per-skill custom msgId blocks outside <conditions> (item func conds stay in stat-effect import).

Usage: gen_skill_cast_conditions.py <mobius dist/game/data/stats/skills dir>
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from mobius_condition_parse import conditions_tree_from_skill, tag

ROOT = Path(__file__).resolve().parent.parent
SKILLS_JSON = ROOT / "data" / "skills.json"


def main() -> None:
    src = Path(sys.argv[1])
    raw = SKILLS_JSON.read_text()
    data = json.loads(raw)
    indent = 1 if raw.startswith("{\n \"") else 2

    bodies: dict[int, ET.Element] = {}
    for path in sorted(src.glob("*.xml")):
        root = ET.parse(path).getroot()
        for node in root.findall("skill"):
            sid = int(node.get("id"))
            bodies[sid] = node

    imported = compound = stripped = 0
    for row in data.get("skills", []):
        row.pop("castCondition", None)
        sid = row.get("skillId")
        if sid not in bodies:
            continue
        tree = conditions_tree_from_skill(bodies[sid])
        if tree:
            row["castCondition"] = tree
            imported += 1
            if tree.get("op") in ("and", "or", "not"):
                compound += 1
            if row.pop("usingKinds", None) is not None:
                stripped += 1

    SKILLS_JSON.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
    print(
        f"castCondition on {imported} skills ({compound} compound trees); "
        f"removed legacy usingKinds on {stripped} skills → {SKILLS_JSON.name}"
    )


if __name__ == "__main__":
    main()