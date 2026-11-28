#!/usr/bin/env python3
"""Patch data/npcs.json with Mobius equipment (rhand/lhand), attack type and range.

Usage: gen_npc_weapons.py <mobius stats/npcs dir> [npcs.json]
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def load_mobius(npcs_dir: Path) -> dict[int, dict]:
    out: dict[int, dict] = {}
    for f in sorted(npcs_dir.glob("*.xml")):
        root = ET.parse(f).getroot()
        # Direct children only: <parameters><minions><npc id=.../> are references, not templates.
        for npc in root.findall("npc"):
            nid = int(npc.get("id"))
            eq = npc.find("equipment")
            rhand = lhand = 0
            if eq is not None:
                rhand = int(eq.get("rhand") or 0)
                lhand = int(eq.get("lhand") or 0)
            typ, rng = "", None
            stats = npc.find("stats")
            atk = stats.find("attack") if stats is not None else None
            if atk is not None:
                typ = (atk.get("type") or "").strip()
                if atk.get("range"):
                    rng = int(float(atk.get("range")))
            out[nid] = {"rhand": rhand, "lhand": lhand, "attackType": typ, "range": rng}
    return out


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: gen_npc_weapons.py <mobius stats/npcs dir> [npcs.json]", file=sys.stderr)
        return 2
    mob = load_mobius(Path(sys.argv[1]))
    path = Path(sys.argv[2] if len(sys.argv) > 2 else "data/npcs.json")
    data = json.loads(path.read_text())
    base_keys = list(data["npcs"][0].keys())
    extra = ["attackType", "rhand", "lhand"]
    for n in data["npcs"]:
        m = mob.get(n["npcId"])
        if not m:
            continue
        if m["range"] is not None:
            n["attackRange"] = m["range"]
        if m["attackType"]:
            n["attackType"] = m["attackType"]
        if m["rhand"]:
            n["rhand"] = m["rhand"]
        if m["lhand"]:
            n["lhand"] = m["lhand"]

    def dump_obj(n: dict) -> str:
        keys: list[str] = []
        seen: set[str] = set()
        for k in base_keys:
            keys.append(k)
            seen.add(k)
            if k == "attackRange":
                for e in extra:
                    if e in n and e not in seen:
                        keys.append(e)
                        seen.add(e)
        for k in n:
            if k not in seen:
                keys.append(k)
        parts = []
        for k in keys:
            if k not in n:
                continue
            if k in extra and (n[k] == "" or n[k] == 0):
                continue
            parts.append(json.dumps(k, ensure_ascii=False) + ": " + json.dumps(n[k], ensure_ascii=False))
        return "    {" + ", ".join(parts) + "}"

    lines = ["{", "  \"source\": " + json.dumps(data["source"], ensure_ascii=False) + ",", "  \"npcs\": ["]
    for i, n in enumerate(data["npcs"]):
        comma = "," if i < len(data["npcs"]) - 1 else ""
        lines.append(dump_obj(n) + comma)
    lines.extend(["  ]", "}", ""])
    path.write_text("\n".join(lines))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
