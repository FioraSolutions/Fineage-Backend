#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude walking routes into data/walking-routes.json.

Reads the Mobius datapack (not copied into this repo) and writes original JSON. No GPL
source is copied.

Mobius reference (L2J_Mobius_CT_0_Interlude):
  dist/game/data/Routes.xml   WalkingManager routes (targets + patrol points)

Usage:
  python3 tools/import_walking_routes.py [path/to/L2J_Mobius_CT_0_Interlude]
  python3 tools/import_walking_routes.py [path/to/dist/game/data/Routes.xml]
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MOBIUS = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude"
ROUTES_XML = DEFAULT_MOBIUS / "dist/game/data/Routes.xml"


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def resolve_routes_xml(arg: str | None) -> Path:
    if arg:
        p = Path(arg)
        if p.is_file():
            return p
        candidate = p / "dist/game/data/Routes.xml"
        if candidate.is_file():
            return candidate
        raise SystemExit(f"missing Routes.xml under {p}")
    if ROUTES_XML.is_file():
        return ROUTES_XML
    raise SystemExit(
        f"missing {ROUTES_XML}; pass path to L2J_Mobius_CT_0_Interlude or Routes.xml"
    )


def parse_routes(path: Path) -> list[dict]:
    routes: list[dict] = []
    for route_el in ET.parse(path).getroot():
        if local(route_el.tag) != "route":
            continue
        repeat_style = (route_el.attrib.get("repeatStyle") or "back").lower()
        if repeat_style not in ("back", "cycle", "conveyor", "random"):
            repeat_style = "back"
        row: dict = {
            "name": route_el.attrib["name"],
            "repeat": route_el.attrib.get("repeat", "false").lower() == "true",
            "repeatStyle": repeat_style,
            "targets": [],
            "points": [],
        }
        for child in route_el:
            tag = local(child.tag)
            if tag == "target":
                a = child.attrib
                row["targets"].append(
                    {
                        "npcId": int(a["id"]),
                        "spawnX": int(a["spawnX"]),
                        "spawnY": int(a["spawnY"]),
                        "spawnZ": int(a["spawnZ"]),
                    }
                )
            elif tag == "point":
                a = child.attrib
                pt: dict = {
                    "x": int(a["X"]),
                    "y": int(a["Y"]),
                    "z": int(a["Z"]),
                    "delaySec": int(a.get("delay", "0")),
                    "run": a.get("run", "false").lower() == "true",
                }
                chat = a.get("string")
                if chat:
                    pt["chat"] = chat
                row["points"].append(pt)
        routes.append(row)
    return routes


def main() -> None:
    arg = sys.argv[1] if len(sys.argv) > 1 else None
    routes_path = resolve_routes_xml(arg)
    routes = parse_routes(routes_path)
    out = ROOT / "data" / "walking-routes.json"
    out.write_text(json.dumps(routes, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out} ({len(routes)} routes)")


if __name__ == "__main__":
    main()