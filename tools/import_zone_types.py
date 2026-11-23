#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude zone rules into data/zone-types.json.

Reads zones/*.xml from a Mobius datapack (not copied into this repo) and writes original JSON.
Movement zones that already have their own catalogs (water, peace, arena, siege, swamp, damage,
no-landing, mother tree) stay in those files; this importer covers every other Interlude zone type.

Usage:
  python3 tools/import_zone_types.py /path/to/mobius/dist/game/data [out=data/zone-types.json]
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Types already applied by a dedicated catalog. Emitting them again would double-apply.
CATALOG_BACKED = {
    "WaterZone",
    "PeaceZone",
    "ArenaZone",
    "SiegeZone",
    "SwampZone",
    "DamageZone",
    "NoLandingZone",
    "MotherTreeZone",
    "FishingZone",
    "OlympiadStadiumZone",
    "JailZone",
    "HqZone",
}


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def zones(data: Path) -> list[dict]:
    out: list[dict] = []
    folder = data / "zones"
    if not folder.is_dir():
        raise SystemExit(f"missing {folder}")
    for path in sorted(folder.glob("*.xml")):
        for z in ET.parse(path).getroot():
            if local(z.tag) != "zone":
                continue
            type_name = z.attrib.get("type", "")
            if type_name in CATALOG_BACKED:
                continue
            row = one(z, path.name)
            if row is not None:
                out.append(row)
    return out


def one(z, file_name: str) -> dict | None:
    a = z.attrib
    type_name = a.get("type", "")
    name = a.get("name", "")
    shape = a.get("shape", "")
    if not type_name or not name or shape not in ("NPoly", "Cuboid", "Cylinder"):
        print(f"skip {file_name} type={type_name!r} name={name!r} shape={shape!r}", file=sys.stderr)
        return None
    nodes = [[int(n.attrib["X"]), int(n.attrib["Y"])] for n in z if local(n.tag) == "node"]
    stats: dict[str, str] = {}
    races: list[dict] = []
    spawns: list[dict] = []
    for child in z:
        tag = local(child.tag)
        if tag == "stat":
            stats[child.attrib["name"]] = child.attrib["val"]
        elif tag == "race":
            races.append({"race": child.attrib.get("name", ""), "point": child.attrib.get("point", "")})
        elif tag == "spawn":
            spawns.append({
                "x": int(child.attrib["X"]),
                "y": int(child.attrib["Y"]),
                "z": int(child.attrib["Z"]),
            })
    try:
        min_z = int(a["minZ"])
        max_z = int(a["maxZ"])
    except KeyError:
        print(f"skip {file_name} {type_name} {name}: missing minZ/maxZ", file=sys.stderr)
        return None
    if min_z > max_z:
        min_z, max_z = max_z, min_z
    if shape == "NPoly" and len(nodes) < 3:
        print(f"skip {file_name} {type_name} {name}: npoly needs 3 nodes", file=sys.stderr)
        return None
    if shape == "Cuboid" and len(nodes) < 2:
        print(f"skip {file_name} {type_name} {name}: cuboid needs 2 nodes", file=sys.stderr)
        return None
    if shape == "Cylinder" and (len(nodes) < 1 or "rad" not in a):
        print(f"skip {file_name} {type_name} {name}: cylinder needs a node and rad", file=sys.stderr)
        return None
    row = {
        "type": type_name,
        "name": name,
        "shape": shape,
        "minZ": min_z,
        "maxZ": max_z,
        "nodes": nodes,
        "stats": stats,
    }
    if "id" in a:
        row["id"] = int(a["id"])
    if shape == "Cylinder":
        row["rad"] = int(a["rad"])
    if races:
        row["races"] = races
    if spawns:
        row["spawns"] = spawns
    return row


def main() -> None:
    data = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("data/zone-types.json")
    rows = zones(data)
    payload = {"zones": rows}
    text = json.dumps(payload, separators=(",", ":"), ensure_ascii=False)
    out.write_text(text + "\n")
    counts: dict[str, int] = {}
    for row in rows:
        counts[row["type"]] = counts.get(row["type"], 0) + 1
    summary = " ".join(f"{k}={v}" for k, v in sorted(counts.items()))
    print(f"wrote {len(rows)} zones to {out} ({summary})")


if __name__ == "__main__":
    main()
