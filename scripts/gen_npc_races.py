#!/usr/bin/env python3
"""Merge Mobius CT_0 Interlude NPC <race> into data/npcs.json.

Usage: gen_npc_races.py <mobius stats/npcs dir>  (normally run by tools/regen_npcs.py)
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NPCS = ROOT / "data" / "npcs.json"

def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: gen_npc_races.py <mobius stats/npcs dir>")
    mobius = Path(sys.argv[1])
    if "L2J_Mobius_CT_0_Interlude" not in mobius.resolve().parts:
        raise SystemExit(f"refusing {mobius}: not a CT_0_Interlude stats/npcs dir")
    races: dict[int, str] = {}
    for path in sorted(mobius.glob("*.xml")):
        for node in ET.parse(path).getroot().findall("npc"):
            npc_id = int(node.get("id"))
            race_el = node.find("race")
            if race_el is not None and race_el.text:
                races[npc_id] = race_el.text.strip().upper()

    data = json.loads(NPCS.read_text())
    n = 0
    for row in data.get("npcs", []):
        rid = row.get("npcId")
        if rid in races:
            row["race"] = races[rid]
            n += 1
    # One NPC per line, like tools/import_npc_templates.py.
    lines = ["{", "  \"source\": " + json.dumps(data.get("source", ""), ensure_ascii=False) + ",", "  \"npcs\": ["]
    for i, row in enumerate(data["npcs"]):
        comma = "," if i + 1 < len(data["npcs"]) else ""
        lines.append("    " + json.dumps(row, ensure_ascii=False, separators=(", ", ": ")) + comma)
    lines.extend(["  ]", "}"])
    NPCS.write_text("\n".join(lines) + "\n")
    print(f"race on {n} npc rows")


if __name__ == "__main__":
    main()