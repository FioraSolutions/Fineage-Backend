#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude cast metadata to data/skills.json: `magicType` (isMagic: 0 physical, 1 magic, 2 static)
and scalar `effectRange` (escape range checked at launch). Used for interrupt (breakCast: magic/static only) and
cancel-at-hitTime. Usage: gen_skill_cast_meta.py <mobius skills xml dir>"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}
path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skills.json"
raw = path.read_text()
data = json.loads(raw)
indent = 1 if raw.startswith("{\n \"") else 2
m_n = e_n = 0
for s in data["skills"]:
    s.pop("magicType", None)
    s.pop("effectRange", None)
    body = bodies.get(s["skillId"], "")
    m = re.search(r'<set name="isMagic" val="(\d)"', body)
    if m and m.group(1) != "0":
        s["magicType"] = int(m.group(1))
        m_n += 1
    e = re.search(r'<set name="effectRange" val="(-?\d+)"\s*/>', body)
    if e:
        s["effectRange"] = int(e.group(1))
        e_n += 1
path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
print(f"magicType on {m_n}, effectRange on {e_n} of {len(data['skills'])} skills")
