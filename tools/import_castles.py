#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude castle siege data into data/castles.json.

Reads the Mobius datapack (not copied into this repo) and writes original JSON (no GPL source copied).

Mobius reference (L2J_Mobius_CT_0_Interlude/dist):
  game/config/Siege.ini                           (<Castle>ControlTowerN / FlameTowerN = x,y,z,npcId[,zones], MaxMercenaries)
  game/data/Doors.xml                             (castle gates and walls: baseHpMax, basePDef, baseMDef, is_wall)
  game/data/zones/castle_hall.xml                 (CastleZone: owner restart points)
  game/data/zones/castle_hq.xml                   (HqZone polygons: where Build Headquarters may be cast)
  db_installer/sql/game/castle_siege_guards.sql   (isHired = 0 rows: the NPC-castle guards)

Usage:
  python3 tools/import_castles.py /path/to/mobius/dist [out=data/castles.json]
"""

from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

CASTLES = [
    (1, "Gludio", "gludio"),
    (2, "Dion", "dion"),
    (3, "Giran", "giran"),
    (4, "Oren", "oren"),
    (5, "Aden", "aden"),
    (6, "Innadril", "innadrile"),
    (7, "Goddard", "godad"),
    (8, "Rune", "rune"),
    (9, "Schuttgart", "schutt"),
]

# Holy artifacts (Mobius Artefact type) sit in data/spawns/castles_<name>.json.
ARTIFACTS = {1: [35063], 2: [35105], 3: [35147], 4: [35189], 5: [35233], 6: [35279], 7: [35322, 35323], 8: [35469], 9: [35514, 35515]}


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def siege_ini(path: Path) -> dict[str, str]:
    values = {}
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip()
    return values


def towers(ini: dict[str, str], name: str, kind: str) -> list[dict]:
    rows = []
    for i in range(1, 10):
        raw = ini.get(f"{name}{kind}{i}")
        if not raw:
            continue
        parts = [int(p) for p in raw.split(",")]
        rows.append({"npcId": parts[3], "x": parts[0], "y": parts[1], "z": parts[2]})
    return rows


def doors(path: Path, prefix: str) -> list[dict]:
    rows = []
    for el in ET.parse(path).getroot().iter():
        if local(el.tag) != "door":
            continue
        name = el.get("name", "")
        if not name.startswith(prefix + "_castle_") or "agit" in name:
            continue
        rows.append({
            "id": int(el.get("id")),
            "name": name,
            "hp": int(el.get("baseHpMax", "0")),
            "pDef": int(el.get("basePDef", "0")),
            "mDef": int(el.get("baseMDef", "0")),
            "wall": el.get("is_wall") == "true",
        })
    return rows


def restart_points(path: Path) -> dict[int, list[dict]]:
    out: dict[int, list[dict]] = {}
    for zone in ET.parse(path).getroot().iter():
        if local(zone.tag) != "zone" or zone.get("type") != "CastleZone":
            continue
        castle_id = 0
        for stat in zone:
            if local(stat.tag) == "stat" and stat.get("name") == "castleId":
                castle_id = int(stat.get("val"))
        points = [
            {"x": int(s.get("X")), "y": int(s.get("Y")), "z": int(s.get("Z"))}
            for s in zone if local(s.tag) == "spawn" and s.get("type") is None
        ]
        out[castle_id] = points
    return out


def hq_zones(path: Path) -> dict[int, list[dict]]:
    out: dict[int, list[dict]] = {}
    for zone in ET.parse(path).getroot().iter():
        if local(zone.tag) != "zone" or zone.get("type") != "HqZone":
            continue
        castle_id = 0
        nodes = []
        for child in zone:
            if local(child.tag) == "stat" and child.get("name") == "castleId":
                castle_id = int(child.get("val"))
            elif local(child.tag) == "node":
                nodes.append([int(child.get("X")), int(child.get("Y"))])
        out.setdefault(castle_id, []).append(
            {"minZ": int(zone.get("minZ")), "maxZ": int(zone.get("maxZ")), "nodes": nodes})
    return out


def guards(path: Path) -> dict[int, list[dict]]:
    out: dict[int, list[dict]] = {}
    row = re.compile(r"\((\d+),(\d+),(\d+),(-?\d+),(-?\d+),(-?\d+),(-?\d+),(\d+),([01])\)")
    for m in row.finditer(path.read_text(encoding="utf-8", errors="replace")):
        castle, _, npc, x, y, z, heading, respawn, hired = (int(v) for v in m.groups())
        if hired:
            continue
        out.setdefault(castle, []).append(
            {"npcId": npc, "x": x, "y": y, "z": z, "heading": heading, "respawnSeconds": respawn})
    return out


def main() -> None:
    dist = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("data/castles.json")
    ini = siege_ini(dist / "game/config/Siege.ini")
    restarts = restart_points(dist / "game/data/zones/castle_hall.xml")
    hq = hq_zones(dist / "game/data/zones/castle_hq.xml")
    guard_rows = guards(dist / "db_installer/sql/game/castle_siege_guards.sql")
    castles = []
    for castle_id, name, prefix in CASTLES:
        castles.append({
            "id": castle_id,
            "name": name,
            "artifactNpcIds": ARTIFACTS[castle_id],
            "controlTowers": towers(ini, name, "ControlTower"),
            "flameTowers": towers(ini, name, "FlameTower"),
            "maxMercenaries": int(ini.get(f"{name}MaxMercenaries", "0")),
            "doors": doors(dist / "game/data/Doors.xml", prefix),
            "ownerRestart": restarts.get(castle_id, []),
            "hqZones": hq.get(castle_id, []),
            "guards": guard_rows.get(castle_id, []),
        })
    doc = {
        "reference": "Mobius CT 0 Interlude Siege.ini, Doors.xml, zones/castle_hall.xml, zones/castle_hq.xml, castle_siege_guards.sql "
                     "(isHired = 0). Original JSON written by tools/import_castles.py, not a copy of the sources.",
        "castles": castles,
    }
    body = ",\n".join("  " + json.dumps(c, separators=(",", ":")) for c in castles)
    out.write_text('{\n "reference": ' + json.dumps(doc["reference"]) + ',\n "castles": [\n' + body + "\n ]\n}\n",
                   encoding="utf-8")
    print(f"wrote {out}: " + ", ".join(f"{c['name']} doors={len(c['doors'])} guards={len(c['guards'])}" for c in castles))


if __name__ == "__main__":
    main()
