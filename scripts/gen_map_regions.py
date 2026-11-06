#!/usr/bin/env python3
"""Generate data/map-regions.json from the Mobius CT0 Interlude data/mapregion files.

Mobius MapRegionManager is what "Scroll of Escape" and a village restart resolve against:
`getTeleToLocation(creature, TOWN)` finds the player's 32768x32768 grid cell
(`getMapRegionX = (x >> 15) + 20`, `getMapRegionY = (y >> 15) + 18`), takes that region's first
normal respawn point (or a random one when RandomRespawnInTownEnabled), and falls back to
talking_island_town when the cell belongs to no region.

Usage: gen_map_regions.py [mobius data dir] > data/map-regions.json
"""
import glob, json, pathlib, sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/data"

regions = []
for f in sorted(glob.glob(str(MOBIUS / "mapregion/*.xml"))):
    root = ET.parse(f).getroot()
    for region in root.findall("region"):
        points = []
        for p in region.findall("respawnPoint"):
            kind = "normal"
            if p.get("isOther") == "true":
                kind = "other"
            elif p.get("isChaotic") == "true":
                kind = "chaotic"
            elif p.get("isBanish") == "true":
                kind = "banish"
            points.append({
                "x": int(p.get("X")),
                "y": int(p.get("Y")),
                "z": int(p.get("Z")),
                "kind": kind,
            })
        cells = [[int(m.get("X")), int(m.get("Y"))] for m in region.findall("map")]
        banned = {}
        for b in region.findall("banned"):
            banned[b.get("race")] = b.get("point")
        regions.append({
            "name": region.get("name"),
            "town": region.get("town"),
            "castle": int(region.get("castle", "0") or 0),
            "locId": int(region.get("locId", "0") or 0),
            "maps": cells,
            "respawnPoints": points,
            "bannedRace": banned or None,
        })

doc = {
    "reference": (
        "L2J Mobius CT0 Interlude data/mapregion/*.xml. A region owns the 32768-unit grid cells listed in maps; "
        "the cell of a position is (x >> 15) + 20, (y >> 15) + 18. Respawn points marked chaotic are the "
        "karma/outer ring Mobius uses instead of the town square."
    ),
    "defaultRegion": "talking_island_town",
    "regions": regions,
}
print(json.dumps(doc, indent=2, ensure_ascii=False))
