#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude `<cond msgId="113"><using kind=".."/></cond>` weapon gates to data/skills.json as
`usingKinds`.

Mobius Skill.checkCondition runs this for EVERY skill, not only for damage nukes: Trick (11), Switch (12) and
Bluff (358) are derangement skills that still require a dagger. Only plain `<using kind=".."/>` bodies are taken
(no and/or/not condition trees).

Usage: gen_skill_using_kinds.py <mobius dist/game/data/stats/skills dir>
"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}

path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skills.json"
raw = path.read_text()
data = json.loads(raw)
indent = 1 if raw.startswith("{\n \"") else 2

n = 0
for s in data["skills"]:
    s.pop("usingKinds", None)
    body = bodies.get(s["skillId"])
    if not body:
        continue
    c = re.search(r'<cond[^>]*msgId="113"[^>]*>(.*?)</cond>', body, re.S)
    if not c:
        continue
    m = re.fullmatch(r'\s*<using kind="([A-Z_ ,]+)"\s*/>\s*', c.group(1))
    if not m:
        continue
    kinds = [k.strip() for k in m.group(1).split(",") if k.strip()]
    if kinds:
        s["usingKinds"] = kinds
        n += 1

path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
print(f"usingKinds on {n} of {len(data['skills'])} skills")
