#!/usr/bin/env python3
"""Mark data/items.json rows "quest": true where Mobius CT0 Interlude items XML has is_questitem=true.
Usage: gen_quest_item_flags.py [mobius items xml dir]"""
import json, sys, pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
src = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/data/stats/items"
quest = set()
for f in src.glob("*.xml"):
    for e in ET.parse(f).getroot().iter("item"):
        if any(s.get("name") == "is_questitem" and s.get("val") == "true" for s in e.findall("set")):
            quest.add(int(e.get("id")))
path = ROOT / "data/items.json"
data = json.loads(path.read_text())
marked = []
for row in data["items"]:
    if row["id"] in quest:
        row["quest"] = True; marked.append(row["id"])
    else:
        row.pop("quest", None)
path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
print(f"quest items marked={len(marked)}: {marked}")
