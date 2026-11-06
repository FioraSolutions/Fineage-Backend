#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude per-level `magicLevel` to data/skill-combat.json.
Used by calcMagicSuccess / Lethal (CreatureStat / Formulas). Usage:
  gen_skill_magic_levels.py <mobius dist/game/data/stats/skills dir>
"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}
path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skill-combat.json"
data = json.loads(path.read_text())


def table(body, name):
    m = re.search(r'<table name="#%s">([^<]*)</table>' % re.escape(name), body)
    return [int(float(x)) for x in m.group(1).split()] if m else None


def magic_level(body, levels):
    m = re.search(r'<set name="magicLevel" val="([^"]+)"\s*/>', body)
    if not m:
        return None
    v = m.group(1).strip()
    if v.startswith("#"):
        t = table(body, v[1:])
        return t
    return [int(float(v))] * levels


n = 0
missing = 0
for s in data["skills"]:
    levels = int(s["levels"])
    body = bodies.get(s["skillId"], "")
    ml = magic_level(body, levels) if body else None
    if not ml:
        s.pop("magicLevel", None)
        missing += 1
        continue
    if len(ml) == 1 and levels > 1:
        ml = ml * levels
    if len(ml) != levels:
        # Blow rows already carry a correct table; keep the shorter/longer clamped by catalog At().
        pass
    s["magicLevel"] = ml
    n += 1

path.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n")
print(f"magicLevel on {n}/{len(data['skills'])} skills ({missing} without Mobius magicLevel)")
