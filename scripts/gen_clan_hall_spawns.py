#!/usr/bin/env python3
"""Centroid + radius per clanHallId from data/doors.json; Mobius owner restart footprint."""
from __future__ import annotations

import json
import math
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOORS = ROOT / "data" / "doors.json"
OUT = ROOT / "data" / "clan-hall-spawns.json"

DEFAULT_RADIUS = 1200

raw = json.loads(DOORS.read_text(encoding="utf-8"))
doors = raw if isinstance(raw, list) else raw.get("doors", [])
by_hall: dict[int, list[tuple[int, int, int]]] = defaultdict(list)
for row in doors:
    hid = int(row.get("clanHallId") or 0)
    if hid <= 0:
        continue
    by_hall[hid].append((int(row["x"]), int(row["y"]), int(row.get("z", 0))))

spawns = []
for hall_id in sorted(by_hall):
    xs, ys, zs = zip(*by_hall[hall_id])
    cx = sum(xs) // len(xs)
    cy = sum(ys) // len(ys)
    cz = sum(zs) // len(zs)
    max_d = 0.0
    for x, y, _ in by_hall[hall_id]:
        d = math.hypot(x - cx, y - cy)
        max_d = max(max_d, d)
    radius = max(DEFAULT_RADIUS, int(math.ceil(max_d + 200)))
    spawns.append(
        {
            "hallId": hall_id,
            "x": cx,
            "y": cy,
            "z": cz,
            "radius": radius,
            "centroid": {"x": cx, "y": cy},
        }
    )

doc = {
    "source": "door centroids from data/doors.json clanHallId",
    "defaultRadius": DEFAULT_RADIUS,
    "halls": spawns,
}
OUT.write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
print(f"wrote {OUT} ({len(spawns)} halls)")