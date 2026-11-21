#!/usr/bin/env python3
"""Build data/dimensional-rift.json from Mobius CT_0 Interlude dimensional_rift.sql + DimensionalRift.xml."""
from __future__ import annotations

import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude"
SQL = MOBIUS / "dist/db_installer/sql/game/dimensional_rift.sql"
XML = MOBIUS / "dist/game/data/DimensionalRift.xml"
OUT = ROOT / "data/dimensional-rift.json"

ROW_RE = re.compile(
    r"\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*\)"
)


def parse_sql(text: str) -> list[dict]:
    rooms = []
    for m in ROW_RE.finditer(text):
        t, r = int(m.group(1)), int(m.group(2))
        rooms.append(
            {
                "type": t,
                "room": r,
                "xMin": int(m.group(3)),
                "xMax": int(m.group(4)),
                "yMin": int(m.group(5)),
                "yMax": int(m.group(6)),
                "zMin": int(m.group(7)),
                "zMax": int(m.group(8)),
                "teleport": {"x": int(m.group(9)), "y": int(m.group(10)), "z": int(m.group(11))},
                "boss": bool(int(m.group(12))),
            }
        )
    if not rooms:
        raise SystemExit(f"No rooms parsed from {SQL}")
    return rooms


def parse_xml(path: pathlib.Path) -> list[dict]:
    tree = ET.parse(path)
    spawns: list[dict] = []
    for area in tree.getroot().findall("area"):
        t = int(area.get("type"))
        for room in area.findall("room"):
            rid = int(room.get("id"))
            for spawn in room.findall("spawn"):
                spawns.append(
                    {
                        "type": t,
                        "room": rid,
                        "mobId": int(spawn.get("mobId")),
                        "count": int(spawn.get("count")),
                        "respawnDelaySeconds": int(spawn.get("delay")),
                    }
                )
    return spawns


def main() -> None:
    if not SQL.is_file():
        raise SystemExit(f"Missing Mobius SQL: {SQL}")
    if not XML.is_file():
        raise SystemExit(f"Missing Mobius XML: {XML}")
    rooms = parse_sql(SQL.read_text(encoding="utf-8", errors="replace"))
    spawns = parse_xml(XML)
    doc = {
        "version": 1,
        "source": "Mobius CT_0 Interlude dimensional_rift.sql + DimensionalRift.xml (transcribed, not copied).",
        "fragmentItemId": 7079,
        "rooms": rooms,
        "spawns": spawns,
    }
    OUT.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {OUT} ({len(rooms)} rooms, {len(spawns)} spawn rows)")


if __name__ == "__main__":
    main()