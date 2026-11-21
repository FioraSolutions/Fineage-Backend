#!/usr/bin/env python3
"""Build data/seven-signs-festival.json from Mobius CT_0 SevenSignsFestival.java constants."""
from __future__ import annotations

import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
JAVA = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/mechanics/sevensigns/SevenSignsFestival.java"
OUT = ROOT / "data/seven-signs-festival.json"

ARRAY_RE = re.compile(
    r"(?:public |protected )?static final int\[\]\[\] (FESTIVAL_(?:DAWN|DUSK)_(?:PLAYER|WITCH)_SPAWNS)\s*=\s*\{([^}]+)\}",
    re.DOTALL,
)
ROW_RE = re.compile(r"\{\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)(?:\s*,\s*(-?\d+))?\s*\}")


def parse_arrays(text: str) -> dict[str, list[list[int]]]:
    out: dict[str, list[list[int]]] = {}
    for m in ARRAY_RE.finditer(text):
        name = m.group(1)
        rows = []
        for row in ROW_RE.finditer(m.group(2)):
            cells = [int(row.group(1)), int(row.group(2)), int(row.group(3))]
            if row.group(4) is not None:
                cells.append(int(row.group(4)))
            rows.append(cells)
        out[name] = rows
    return out


def main() -> None:
    text = JAVA.read_text(encoding="utf-8", errors="replace")
    arrays = parse_arrays(text)
    required = [
        "FESTIVAL_DAWN_PLAYER_SPAWNS",
        "FESTIVAL_DUSK_PLAYER_SPAWNS",
        "FESTIVAL_DAWN_WITCH_SPAWNS",
        "FESTIVAL_DUSK_WITCH_SPAWNS",
    ]
    for key in required:
        if key not in arrays or len(arrays[key]) != 5:
            raise SystemExit(f"Missing or invalid {key} in {JAVA}")

    doc = {
        "version": 1,
        "offeringItemId": 5901,
        "offeringValue": 5,
        "maxOffset": 230,
        "levelScores": [60, 70, 100, 120, 150],
        "festivalLengthMinutes": 18,
        "cycleLengthMinutes": 228,
        "tiers": [],
    }
    level_labels = ["31", "42", "53", "64", "none"]
    for i in range(5):
        dawn = arrays["FESTIVAL_DAWN_PLAYER_SPAWNS"][i]
        dusk = arrays["FESTIVAL_DUSK_PLAYER_SPAWNS"][i]
        witch_d = arrays["FESTIVAL_DAWN_WITCH_SPAWNS"][i]
        witch_u = arrays["FESTIVAL_DUSK_WITCH_SPAWNS"][i]
        doc["tiers"].append(
            {
                "id": i,
                "levelLabel": level_labels[i],
                "dawn": {
                    "playerSpawn": {"x": dawn[0], "y": dawn[1], "z": dawn[2]},
                    "witchNpcId": witch_d[4] if len(witch_d) > 3 else witch_d[3],
                    "witchX": witch_d[0],
                    "witchY": witch_d[1],
                    "witchZ": witch_d[2],
                },
                "dusk": {
                    "playerSpawn": {"x": dusk[0], "y": dusk[1], "z": dusk[2]},
                    "witchNpcId": witch_u[4] if len(witch_u) > 3 else witch_u[3],
                    "witchX": witch_u[0],
                    "witchY": witch_u[1],
                    "witchZ": witch_u[2],
                },
            }
        )

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {OUT} ({len(doc['tiers'])} tiers)")


if __name__ == "__main__":
    main()