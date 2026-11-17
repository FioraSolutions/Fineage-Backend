#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude `effectPoint` to data/skills.json as `effectPoint`.

Mobius Creature.onMagicHitTimer runs `addDamageHate(this, 0, -effectPoint)` for a negative effectPoint and
`reduceHate(this, effectPoint)` for a positive one, BEFORE the skill's effect payloads. Veil (106) has no effectPoint
at all — casting it must not put the victim on the caster, while Trick (11) / Switch (12) / Bluff (358) carry a small
negative value that their own DeleteHateOfMe payload then wipes again.

Usage: gen_skill_effect_point.py <mobius dist/game/data/stats/skills dir>
"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}

path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skills.json"
raw = path.read_text()
data = json.loads(raw)
indent = 1 if raw.startswith("{\n \"") else 2

patched = 0
for s in data["skills"]:
    s.pop("effectPoint", None)
    body = bodies.get(s["skillId"], "")
    m = re.search(r'<set name="effectPoint" val="([^"]+)"\s*/>', body)
    if not m:
        continue
    v = m.group(1).strip()
    if v.startswith("#"):
        # Per-level table: keep the whole list so the zone can pick the cast level's entry.
        t = re.search(r'<table name="%s">([^<]*)</table>' % re.escape(v), body)
        if not t:
            continue
        vals = [int(x) for x in t.group(1).split()]
        if not vals:
            continue
        # One entry when the table is flat, the whole per-level list otherwise (EffectPointAt clamps the index).
        s["effectPoint"] = vals[:1] if all(v2 == vals[0] for v2 in vals) else vals
    else:
        try:
            s["effectPoint"] = [int(v)]
        except ValueError:
            continue
    patched += 1

path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
print(f"effectPoint on {patched} of {len(data['skills'])} skills")
