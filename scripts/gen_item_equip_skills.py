#!/usr/bin/env python3
"""Generate data/item-equip-skills.json — Mobius Inventory item_skill on Weapon/Armor.

While a piece is equipped, Mobius grants each <set name="item_skill"> (addSkill false).
Consumable UseItem chains stay in item-skills.json.

Only items present in data/items.json are emitted.

Usage: python3 scripts/gen_item_equip_skills.py [mobius data dir] > data/item-equip-skills.json
"""
from __future__ import annotations

import glob
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/dist/game/data"

items_by_id = {i["id"]: i for i in json.loads((ROOT / "data/items.json").read_text())["items"]}

rows: list[dict] = []
for path in sorted(glob.glob(str(MOBIUS / "stats/items/**/*.xml"), recursive=True)):
    root = ET.parse(path).getroot()
    for item in root.iter("item"):
        kind = (item.get("type") or "").strip()
        if kind not in ("Weapon", "Armor"):
            continue
        try:
            item_id = int(item.get("id") or 0)
        except ValueError:
            continue
        if item_id not in items_by_id:
            continue
        skills = []
        for s in item.findall("set"):
            if (s.get("name") or "") != "item_skill":
                continue
            # Mobius: <set name="item_skill" val="3566-1" /> (also value= / multi via ;,)
            raw = (s.get("val") or s.get("value") or "").strip()
            if not raw:
                continue
            for part in raw.replace(",", ";").split(";"):
                part = part.strip()
                if not part or "-" not in part:
                    continue
                sid_s, lvl_s = part.split("-", 1)
                try:
                    sid, lvl = int(sid_s), int(lvl_s)
                except ValueError:
                    continue
                if sid > 0 and lvl > 0:
                    skills.append({"id": sid, "level": lvl})
        if not skills:
            continue
        # de-dupe keeping first
        seen: set[int] = set()
        uniq = []
        for sk in skills:
            if sk["id"] in seen:
                continue
            seen.add(sk["id"])
            uniq.append(sk)
        rows.append({
            "itemId": item_id,
            "name": items_by_id[item_id].get("name") or item.get("name") or "",
            "skills": uniq,
        })

rows.sort(key=lambda r: r["itemId"])
out = {
    "reference": (
        "L2J Mobius CT0 Interlude: stats/items <set name=\"item_skill\"> on Weapon/Armor. "
        "Granted while equipped (Inventory.addSkill false). Consumable UseItem chains stay in item-skills.json."
    ),
    "items": rows,
}
json.dump(out, sys.stdout, indent=2, ensure_ascii=False)
sys.stdout.write("\n")
print(f"wrote {len(rows)} equip-skill items", file=sys.stderr)
