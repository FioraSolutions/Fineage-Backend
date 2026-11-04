#!/usr/bin/env python3
"""Split the Elven Underground Fortress (Mobius Others/20_20.xml, underground rows) out of data/spawns/others_20_20.json
into data/spawns/elven_fortress.json so local Net can enable it alongside the elven maps without the rest of the world.
Idempotent: rows already moved are not moved twice. Object ids are kept (no duplicates: rows leave others_20_20)."""
import json, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent / "data" / "spawns"
BOX = (5000, 60000, 35000, 90000)  # x0, y0, x1, y1
MAX_Z = -3700                      # underground only


def inside(r):
    return BOX[0] <= r["x"] <= BOX[2] and BOX[1] <= r["y"] <= BOX[3] and r["z"] < MAX_Z


src_path, dst_path = ROOT / "others_20_20.json", ROOT / "elven_fortress.json"
src = json.loads(src_path.read_text())
dst = json.loads(dst_path.read_text()) if dst_path.exists() else {
    "mapId": "elven_fortress",
    "enabled": True,
    "comment": "Mobius CT 0 Interlude spawns/Others/20_20.xml, Elven Underground Fortress (z < -3700, x 5000..35000, y 60000..90000), "
               "split from others_20_20 by scripts/gen_elven_fortress.py. Q316 Sukar Wererats 20040/20047 + Varool Foulclaw 27020, "
               "Q408 Sukar Wererat Leader 20047. Entrance: Elven Village gatekeeper 'Elven Fortress' (29294, 74968, -3776).",
    "npcs": [],
}
moved = [r for r in src["npcs"] if inside(r)]
src["npcs"] = [r for r in src["npcs"] if not inside(r)]
known = {r["objectId"] for r in dst["npcs"]}
dst["npcs"] += [r for r in moved if r["objectId"] not in known]
dst["npcs"].sort(key=lambda r: r["objectId"])
src_path.write_text(json.dumps(src, indent=2) + "\n")
dst_path.write_text(json.dumps(dst, indent=2) + "\n")
print(f"elven_fortress={len(dst['npcs'])} others_20_20={len(src['npcs'])}")
