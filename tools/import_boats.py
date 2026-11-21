#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude boat vehicle scripts into data/boats.json.

Reads the Mobius datapack scripts (not copied into this repo) and writes original JSON.
No GPL Java source is copied — only path coordinates and timing metadata.

Mobius reference (L2J_Mobius_CT_0_Interlude):
  dist/game/data/scripts/vehicles/Boat*.java

Usage:
  python3 tools/import_boats.py [path/to/L2J_Mobius_CT_0_Interlude]
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MOBIUS = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude"
VEHICLES = DEFAULT_MOBIUS / "dist/game/data/scripts/vehicles"

POINT = re.compile(
    r"new\s+VehiclePathPoint\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)"
)
ARRAY_BLOCK = re.compile(
    r"static\s+final\s+VehiclePathPoint\[\]\s+(\w+)\s*=\s*\{([^}]*)\}",
    re.DOTALL,
)
SINGLE_ALIAS = re.compile(
    r"static\s+final\s+VehiclePathPoint\s+(\w+)\s*=\s*(\w+)\[(\w+)\.length\s*-\s*1\]",
)
GET_NEW_BOAT = re.compile(
    r"getNewBoat\s*\(\s*(\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)"
)
DOCK_SHIP_WAIT = re.compile(
    r"dockShip\s*\(\s*BoatManager\.(\w+)\s*,\s*true\s*\)[^}]*?"
    r"ThreadPool\.schedule\s*\(\s*this\s*,\s*(\d+)\s*\)",
    re.DOTALL,
)
ARRIVED_WAIT = re.compile(
    r"(?:broadcastPackets|broadcastPacket)\s*\([^;]*ARRIVED[^;]*;\s*"
    r"ThreadPool\.schedule\s*\(\s*this\s*,\s*(\d+)\s*\)",
    re.DOTALL,
)
HARBOR_CONST = {
    "TALKING_ISLAND": 1,
    "GLUDIN_HARBOR": 2,
    "RUNE_HARBOR": 3,
}


def local_harbor_id(dock_name: str) -> int:
    upper = dock_name.upper()
    if "TALKING" in upper:
        return 1
    if "GLUDIN" in upper:
        return 2
    if "RUNE" in upper:
        return 3
    # Primeval Isle and Innadril pleasure boat are not BoatManager harbor slots.
    return 0


def parse_points(block: str) -> list[dict]:
    return [
        {
            "x": int(m.group(1)),
            "y": int(m.group(2)),
            "z": int(m.group(3)),
            "heading": int(m.group(4)),
            "speed": int(m.group(5)),
        }
        for m in POINT.finditer(block)
    ]


def leg_speed(points: list[dict]) -> int:
    if not points:
        return 800
    speeds = [p["speed"] for p in points]
    return max(set(speeds), key=speeds.count)


def dock_wait_ms(source: str, harbor_id: int) -> int:
    for const, hid in HARBOR_CONST.items():
        if hid == harbor_id:
            m = re.search(
                rf"dockShip\s*\(\s*BoatManager\.{const}\s*,\s*true\s*\)[^}}]*?"
                rf"ThreadPool\.schedule\s*\(\s*this\s*,\s*(\d+)\s*\)",
                source,
                re.DOTALL,
            )
            if m:
                return int(m.group(1))
    if harbor_id == 0:
        m = ARRIVED_WAIT.search(source)
        if m:
            return int(m.group(1))
    return 300_000


def is_dock_array(name: str) -> bool:
    if name == "DOCK":
        return True
    return name.endswith("_DOCK") or name in ("PRIMEVAL_DOCK", "GIRAN_DOCK")


def parse_boat_file(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    arrays: dict[str, list[dict]] = {}
    for name, body in ARRAY_BLOCK.findall(text):
        pts = parse_points(body)
        if pts:
            arrays[name] = pts

    for alias, src, _ in SINGLE_ALIAS.findall(text):
        if src in arrays and arrays[src]:
            arrays[alias] = [arrays[src][-1]]

    boat_m = GET_NEW_BOAT.search(text)
    if not boat_m:
        raise ValueError(f"no getNewBoat() in {path.name}")
    boat_id = int(boat_m.group(1))
    spawn = {
        "x": int(boat_m.group(2)),
        "y": int(boat_m.group(3)),
        "z": int(boat_m.group(4)),
        "heading": int(boat_m.group(5)),
    }

    docks: list[dict] = []
    for name, points in sorted(arrays.items()):
        if not is_dock_array(name):
            continue
        harbor_id = local_harbor_id(name)
        docks.append(
            {
                "harborId": harbor_id,
                "points": points,
                "waitMs": dock_wait_ms(text, harbor_id),
            }
        )

    legs: list[dict] = []
    for name, points in sorted(arrays.items()):
        if is_dock_array(name) or len(points) < 2:
            continue
        legs.append({"points": points, "speed": leg_speed(points)})

    return {
        "id": boat_id,
        "boatNpcId": boat_id,
        "spawn": spawn,
        "docks": docks,
        "legs": legs,
    }


def resolve_vehicles_dir(arg: str | None) -> Path:
    if arg:
        base = Path(arg)
        if base.name == "vehicles" and base.is_dir():
            return base
        candidate = base / "dist/game/data/scripts/vehicles"
        if candidate.is_dir():
            return candidate
        raise SystemExit(f"missing vehicles dir under {base}")
    if VEHICLES.is_dir():
        return VEHICLES
    raise SystemExit(f"missing {VEHICLES}; pass path to L2J_Mobius_CT_0_Interlude")


def main() -> None:
    arg = sys.argv[1] if len(sys.argv) > 1 else None
    vehicle_dir = resolve_vehicles_dir(arg)
    boats = [parse_boat_file(p) for p in sorted(vehicle_dir.glob("Boat*.java"))]
    out = ROOT / "data" / "boats.json"
    out.write_text(json.dumps(boats, indent=2) + "\n", encoding="utf-8")
    leg_count = sum(len(b["legs"]) for b in boats)
    print(f"wrote {out} ({len(boats)} boats, {leg_count} legs)")


if __name__ == "__main__":
    main()