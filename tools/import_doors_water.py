#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude doors, water, and peace zones into data JSON.

Reads the Mobius datapack (not copied into this repo) and writes original JSON (no GPL source copied).

Mobius reference (L2J_Mobius_CT_0_Interlude):
  dist/game/data/Doors.xml        (DoorData: node1..4 polygon, nodeZ, height, default_status; missing = close)
  dist/game/data/zones/water.xml  (ZoneManager WaterZone: Cuboid nodes + minZ/maxZ)
  dist/game/data/zones/peace.xml  (PeaceZone NPoly + minZ/maxZ)
  dist/game/data/zones/ssq.xml    (PeaceZone rows only)

Usage:
  python3 tools/import_doors_water.py /path/to/mobius/data [out_dir=data]
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def doors(data: Path) -> list[dict]:
    out = []
    for d in ET.parse(data / "Doors.xml").getroot():
        if local(d.tag) != "door":
            continue
        a = d.attrib
        x, y, z = (int(v) for v in a["pos"].split(";"))
        nodes = [[int(v) for v in a[f"node{i}"].split(",")] for i in range(1, 5)]
        row = {
            "id": int(a["id"]),
            "name": a.get("name", ""),
            "x": x, "y": y, "z": z,
            "nodes": nodes,
            "nodeZ": int(a["nodeZ"]),
            "height": int(a.get("height", "150")),
            "open": a.get("default_status", "close") == "open",
        }
        if a.get("is_wall") == "true":
            row["wall"] = True
        if "open_time" in a:
            row["openTime"] = int(a["open_time"])
        if "close_time" in a:
            row["closeTime"] = int(a["close_time"])
        if "clanhall_id" in a:
            row["clanHallId"] = int(a["clanhall_id"])
        out.append(row)
    return out


def peace(data: Path) -> list[dict]:
    """Mobius PeaceZone NPoly from zones/peace.xml and zones/ssq.xml."""
    out = []
    for file in ("peace.xml", "ssq.xml"):
        for z in ET.parse(data / "zones" / file).getroot():
            if local(z.tag) != "zone" or z.attrib.get("type") != "PeaceZone":
                continue
            a = z.attrib
            if a.get("shape") != "NPoly":
                print(f"skip non-NPoly peace zone {a.get('name')}", file=sys.stderr)
                continue
            nodes = [[int(n.attrib["X"]), int(n.attrib["Y"])] for n in z if local(n.tag) == "node"]
            if len(nodes) < 3:
                continue
            out.append({
                "name": a.get("name", ""),
                "minZ": int(a["minZ"]),
                "maxZ": int(a["maxZ"]),
                "nodes": nodes,
            })
    return out


def water(data: Path) -> list[dict]:
    out = []
    for z in ET.parse(data / "zones" / "water.xml").getroot():
        if local(z.tag) != "zone" or z.attrib.get("type") != "WaterZone":
            continue
        a = z.attrib
        if a.get("shape") != "Cuboid":
            print(f"skip non-cuboid water zone {a.get('name')}", file=sys.stderr)
            continue
        pts = [(int(n.attrib["X"]), int(n.attrib["Y"])) for n in z if local(n.tag) == "node"]
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        out.append({
            "name": a.get("name", ""),
            "minX": min(xs), "maxX": max(xs), "minY": min(ys), "maxY": max(ys),
            "minZ": int(a["minZ"]), "maxZ": int(a["maxZ"]),
        })
    return out


def main() -> None:
    data = Path(sys.argv[1])
    out_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("data")
    d, w, p = doors(data), water(data), peace(data)
    (out_dir / "doors.json").write_text(json.dumps(d, separators=(",", ":"), ensure_ascii=False, indent=None).replace("},{", "},\n{") + "\n")
    (out_dir / "water-zones.json").write_text(json.dumps(w, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "peace-zones.json").write_text(json.dumps(p, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    print(f"doors={len(d)} water={len(w)} peace={len(p)}")


if __name__ == "__main__":
    main()
