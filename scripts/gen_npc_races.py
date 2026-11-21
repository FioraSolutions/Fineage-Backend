#!/usr/bin/env python3
"""Merge Mobius CT_0 Interlude NPC <race> into data/npcs.json."""
from __future__ import annotations

import json
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NPCS = ROOT / "data" / "npcs.json"
MOBIUS = Path(
    "/agent/repos/Fineage-Backend/.cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/npcs"
)

def main() -> None:
    races: dict[int, str] = {}
    for path in sorted(MOBIUS.glob("*.xml")):
        for node in ET.parse(path).getroot().findall("npc"):
            npc_id = int(node.get("id"))
            race_el = node.find("race")
            if race_el is not None and race_el.text:
                races[npc_id] = race_el.text.strip().upper()

    raw = NPCS.read_text()
    data = json.loads(raw)
    indent = 1 if raw.startswith("{\n \"") else 2
    n = 0
    for row in data.get("npcs", []):
        rid = row.get("npcId")
        if rid in races:
            row["race"] = races[rid]
            n += 1
    NPCS.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
    print(f"race on {n} npc rows")


if __name__ == "__main__":
    main()