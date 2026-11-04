#!/usr/bin/env python3
"""Copy Mobius CT0 Interlude class skill tree levelUpSp into data/skill-trees.json (field levelUpSp on each row).
Matches rows by (classId, skillId, level). Rows Mobius lists without levelUpSp (autoGet) stay without it.
Usage: gen_skill_sp.py [mobius skillTrees dir] (default ref/mobius-ct0-interlude/data/skillTrees)"""
import json, sys, pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
src = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/data/skillTrees"
sp = {}
for f in src.rglob("*.xml"):
    for tree in ET.parse(f).getroot().iter("skillTree"):
        if tree.get("type") != "classSkillTree" or tree.get("classId") is None:
            continue
        cid = int(tree.get("classId"))
        for s in tree.iter("skill"):
            if s.get("levelUpSp") is not None:
                sp[(cid, int(s.get("skillId")), int(s.get("skillLevel")))] = int(s.get("levelUpSp"))

path = ROOT / "data/skill-trees.json"
data = json.loads(path.read_text())
hit = miss = 0
for tree in data["trees"]:
    for row in tree["skills"]:
        key = (tree["classId"], row["skillId"], row["level"])
        if key in sp:
            row["levelUpSp"] = sp[key]; hit += 1
        elif row.get("learnedByNpc"):
            miss += 1
path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
print(f"levelUpSp set={hit} npc-rows-without-sp={miss}")
