#!/usr/bin/env python3
"""Import Mobius transferSkillTree.xml into data/skill-transfer-trees.json and ensure pomander items exist.

Mobius CT0 Interlude references Holy Pomander ids on healer trainers but ships transferSkillTree.xml
in the CT2.6 High Five datapack (rows tagged Confirmed CT2.5). Certificate ids 15307–15309 match CT0 npc params.

Usage: gen_transfer_skill_trees.py [transferSkillTree.xml]
"""
from __future__ import annotations

import json
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_XML = (
    ROOT.parent.parent
    / "Fineage-Backend/.cache/l2j-mobius/L2J_Mobius_CT_2.6_HighFive/dist/game/data/stats/players/skillTrees/transferSkillTree.xml"
)
if not DEFAULT_XML.is_file():
    DEFAULT_XML = pathlib.Path(
        "/agent/repos/Fineage-Backend/.cache/l2j-mobius/L2J_Mobius_CT_2.6_HighFive/dist/game/data/stats/players/skillTrees/transferSkillTree.xml"
    )

src = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_XML
if not src.is_file():
    raise SystemExit(f"transferSkillTree.xml not found: {src}")

POMANDERS = {
    15307: "Holy Pomander - Cardinal",
    15308: "Holy Pomander - Eva's Saint",
    15309: "Holy Pomander - Shillien Saint",
}
POMANDER_ICONS = {
    15307: "icon.etc_saint_pomander_bishop_i00",
    15308: "icon.etc_saint_pomander_elder_i00",
    15309: "icon.etc_saint_pomander_silienelder_i00",
}

trees: list[dict] = []
certificates: dict[int, int] = {}
seen_rows: dict[tuple[int, int, int], dict] = {}

for tree in ET.parse(src).getroot().iter("skillTree"):
    if tree.get("type") != "transferSkillTree":
        continue
    class_id = int(tree.get("classId"))
    skills: list[dict] = []
    for skill in tree.iter("skill"):
        if skill.get("skillLevel") is None:
            continue
        sid = int(skill.get("skillId"))
        lvl = int(skill.get("skillLevel"))
        get_level = int(skill.get("getLevel", "76"))
        cert_id = None
        count = 1
        for child in skill:
            tag = child.tag.split("}")[-1]
            if tag == "item":
                cert_id = int(child.get("id"))
                count = int(child.get("count", "1"))
                break
        if cert_id is None:
            continue
        key = (class_id, sid, lvl)
        row = {
            "skillId": sid,
            "level": lvl,
            "getLevel": get_level,
            "certificateItemId": cert_id,
            "count": count,
        }
        seen_rows[key] = row
        certificates.setdefault(cert_id, class_id)
    skills = [seen_rows[k] for k in sorted(seen_rows) if k[0] == class_id]
    if skills:
        trees.append({"classId": class_id, "skills": skills})

out = {
    "reference": f"Mobius transferSkillTree from {src.name} (CT2.5 rows; pomanders match CT0 Interlude healer trainers).",
    "certificates": [
        {"itemId": item_id, "classId": class_id} for item_id, class_id in sorted(certificates.items())
    ],
    "trees": trees,
}
out_path = ROOT / "data/skill-transfer-trees.json"
out_path.write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n")
print(f"wrote {out_path} trees={len(trees)} skills={sum(len(t['skills']) for t in trees)} certs={len(certificates)}")

items_path = ROOT / "data/items.json"
data = json.loads(items_path.read_text())
by_id = {row["id"]: row for row in data["items"]}
added = 0
for item_id, name in POMANDERS.items():
    if item_id in by_id:
        continue
    by_id[item_id] = {
        "id": item_id,
        "name": name,
        "kind": "etc",
        "icon": POMANDER_ICONS[item_id],
        "weight": 10,
        "stackable": True,
    }
    added += 1
if added:
    data["items"] = sorted(by_id.values(), key=lambda r: r["id"])
    items_path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    print(f"items.json: added {added} pomander row(s)")