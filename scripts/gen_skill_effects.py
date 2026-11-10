#!/usr/bin/env python3
"""Extract Mobius skill <effect> rows into data/skill-effects.json.

Used to route A1 skills that are not heals/nukes (DispelByCategory, MagicalAttackMp, …).
Also patches skills.json reuseDelayMs from Mobius when present.

Usage: gen_skill_effects.py <mobius dist/game/data/stats/skills dir>
"""
import json, sys, pathlib, xml.etree.ElementTree as ET

src = pathlib.Path(sys.argv[1])
root = pathlib.Path(__file__).resolve().parent.parent
skills_path = root / "data" / "skills.json"
out_path = root / "data" / "skill-effects.json"

effects_by_id = {}
reuse_by_id = {}
for f in sorted(src.glob("*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        sid = int(sk.get("id"))
        for s in sk.findall("set"):
            if s.get("name") == "reuseDelay":
                try:
                    reuse_by_id[sid] = int(s.get("val"))
                except (TypeError, ValueError):
                    pass
        rows = []
        for e in sk.iter("effect"):
            name = (e.get("name") or "").strip()
            if not name:
                continue
            row = {"name": name}
            for p in e.findall("param"):
                # Mobius uses either name=/val= or bare attrs (slot/rate/max).
                pname = p.get("name")
                pval = p.get("val")
                if pname and pval is not None:
                    row[pname] = pval
                for attr, val in p.attrib.items():
                    if attr in ("name", "val"):
                        continue
                    row[attr] = val
            # Also copy effect element attrs except name.
            for attr, val in e.attrib.items():
                if attr != "name" and attr not in row:
                    row[attr] = val
            rows.append(row)
        if rows:
            effects_by_id[sid] = rows

skills_raw = skills_path.read_text()
skills_data = json.loads(skills_raw)
indent = 1 if skills_raw.startswith("{\n \"") else 2
reuse_patched = 0
for s in skills_data["skills"]:
    sid = s["skillId"]
    if sid in reuse_by_id and s.get("reuseDelayMs") != reuse_by_id[sid]:
        s["reuseDelayMs"] = reuse_by_id[sid]
        reuse_patched += 1

skills_path.write_text(json.dumps(skills_data, indent=indent, ensure_ascii=False) + "\n")

out = {"skills": []}
for sid in sorted(effects_by_id):
    out["skills"].append({"skillId": sid, "effects": effects_by_id[sid]})
out_path.write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n")

# Summary of routing-critical effects
from collections import Counter
c = Counter()
for rows in effects_by_id.values():
    for r in rows:
        c[r["name"]] += 1
print(f"skill-effects.json: {len(effects_by_id)} skills, reuseDelay patched={reuse_patched}")
print("top:", c.most_common(15))
cancel = effects_by_id.get(1056)
burn = effects_by_id.get(1398)
print("1056", cancel)
print("1398", burn)
