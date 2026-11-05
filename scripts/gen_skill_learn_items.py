#!/usr/bin/env python3
"""Copy Mobius CT0 Interlude skill-tree learn requirements into data/skill-trees.json.

Adds `requiredItems` (id/count pairs) and `preReqSkills` (skillId/level pairs) to each row that
Mobius lists under `<skill>`; rows without children stay clean so the JSON does not grow noise.
Matches rows by (classId, skillId, level). Some trees (fishing, pledge) have no classId and are skipped.

Usage: gen_skill_learn_items.py [mobius skillTrees dir]
       (default ref/mobius-ct0-interlude/data/skillTrees)"""
import json, sys, pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
src = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/data/skillTrees"

items: dict[tuple[int, int, int], list[dict]] = {}
prereqs: dict[tuple[int, int, int], list[dict]] = {}
for path in src.rglob("*.xml"):
    for tree in ET.parse(path).getroot().iter("skillTree"):
        if tree.get("type") != "classSkillTree" or tree.get("classId") is None:
            continue
        class_id = int(tree.get("classId"))
        for skill in tree.iter("skill"):
            key = (class_id, int(skill.get("skillId")), int(skill.get("skillLevel")))
            for child in skill:
                tag = child.tag.split("}")[-1]
                if tag == "item":
                    items.setdefault(key, []).append(
                        {"itemId": int(child.get("id")), "count": int(child.get("count", "1"))})
                elif tag == "preRequisiteSkill":
                    prereqs.setdefault(key, []).append(
                        {"skillId": int(child.get("id")), "level": int(child.get("lvl", "1"))})

path = ROOT / "data/skill-trees.json"
data = json.loads(path.read_text())
hit_items = hit_prereqs = 0
for tree in data["trees"]:
    for row in tree["skills"]:
        key = (tree["classId"], row["skillId"], row["level"])
        if key in items:
            row["requiredItems"] = items[key]; hit_items += 1
        else:
            row.pop("requiredItems", None)
        if key in prereqs:
            row["preReqSkills"] = prereqs[key]; hit_prereqs += 1
        else:
            row.pop("preReqSkills", None)

path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
print(f"requiredItems rows={hit_items} preReqSkills rows={hit_prereqs} "
      f"(mobius listed {len(items)} / {len(prereqs)})")
