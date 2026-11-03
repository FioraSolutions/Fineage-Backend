#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude `effectRange` (escape range at launch, Creature.onMagicLaunchedTimer) to data/skill-combat.json.
Only scalar values are taken. Usage: gen_skill_effect_range.py <mobius skills xml dir>"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}
path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skill-combat.json"
data = json.loads(path.read_text())
n = 0
for s in data["skills"]:
    s.pop("effectRange", None)
    m = re.search(r'<set name="effectRange" val="(-?\d+)"\s*/>', bodies.get(s["skillId"], ""))
    if m:
        s["effectRange"] = int(m.group(1))
        n += 1
path.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n")
print(f"effectRange on {n}/{len(data['skills'])} skills")
