#!/usr/bin/env python3
"""Add Mobius periodOfDay to data/spawns/*.json from CT_0 Interlude XML.

Matches spawn rows by (npcId, x, y, z). Does not create rows; only patches existing JSON.
Usage: python3 scripts/patch_spawn_period_of_day.py
"""
from __future__ import annotations

import json
import pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/spawns"
SPAWNS = ROOT / "data" / "spawns"


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def load_period_index() -> dict[tuple[int, int, int, int], str]:
    idx: dict[tuple[int, int, int, int], str] = {}
    if not MOBIUS.is_dir():
        raise SystemExit(f"Missing Mobius spawns: {MOBIUS}")
    for xml in MOBIUS.rglob("*.xml"):
        try:
            root = ET.parse(xml).getroot()
        except ET.ParseError:
            continue
        for npc in root.iter():
            if local(npc.tag) != "npc":
                continue
            period = npc.attrib.get("periodOfDay")
            if not period:
                continue
            try:
                nid = int(npc.attrib["id"])
                x, y, z = int(npc.attrib["x"]), int(npc.attrib["y"]), int(npc.attrib["z"])
            except (KeyError, ValueError):
                continue
            idx[(nid, x, y, z)] = period
    return idx


def main() -> None:
    idx = load_period_index()
    patched = 0
    for path in sorted(SPAWNS.glob("*.json")):
        data = json.loads(path.read_text())
        changed = False
        for row in data.get("npcs") or []:
            key = (row.get("npcId"), row.get("x"), row.get("y"), row.get("z"))
            period = idx.get(key)
            if period and row.get("periodOfDay") != period:
                row["periodOfDay"] = period
                patched += 1
                changed = True
        if changed:
            path.write_text(json.dumps(data, indent=2) + "\n")
    print(f"patched {patched} spawn rows with periodOfDay")


if __name__ == "__main__":
    main()