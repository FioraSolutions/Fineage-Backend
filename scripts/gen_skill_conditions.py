#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude weapon/armor `using` skill conditions to data/skill-combat.json as `usingKinds`.
Only plain <conditions><using kind=".."/></conditions> blocks are taken (no and/or/not trees).
Usage: gen_skill_conditions.py <mobius skills xml dir>"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}
path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skill-combat.json"
data = json.loads(path.read_text())
n = skipped = 0
for s in data["skills"]:
    s.pop("usingKinds", None)
    body = bodies.get(s["skillId"])
    if not body:
        continue
    c = re.search(r"<conditions[^>]*>(.*?)</conditions>", body, re.S)
    if not c:
        continue
    inner = c.group(1).strip()
    m = re.fullmatch(r'<using kind="([A-Z_ ,]+)"\s*/>', inner)
    if not m:
        if "<using" in inner:
            skipped += 1
        continue
    s["usingKinds"] = [k.strip() for k in m.group(1).split(",") if k.strip()]
    n += 1
path.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n")
print(f"usingKinds on {n} skills; {skipped} compound conditions skipped")
