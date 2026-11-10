#!/usr/bin/env python3
"""Generate data/skill-dots.json from Mobius DamOverTime / DamOverTimePercent effects.

Usage: gen_skill_dots.py <mobius dist/game/data/stats/skills dir> > data/skill-dots.json
"""
import glob, json, sys, xml.etree.ElementTree as ET

out = []
for f in sorted(glob.glob(sys.argv[1] + "/*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        fr = sk.find("for")
        if fr is None:
            continue
        levels = int(sk.get("levels", "1"))
        tables = {}
        for t in sk.findall("table"):
            name = t.get("name") or ""
            vals = (t.text or "").split()
            tables[name] = vals
            if name.startswith("#"):
                tables[name[1:]] = vals
            else:
                tables["#" + name] = vals

        for effect in fr.findall("effect"):
            name = effect.get("name") or ""
            if name not in ("DamOverTime", "DamOverTimePercent"):
                continue
            ticks = int(effect.get("ticks") or "1")
            power_raw = None
            for p in effect.findall("param"):
                if "power" in p.attrib:
                    power_raw = p.get("power")
                    break
            if power_raw is None:
                continue
            try:
                if power_raw.startswith("#"):
                    vals = [float(x) for x in tables[power_raw]]
                else:
                    vals = [float(power_raw)]
            except (KeyError, ValueError):
                continue
            if len(vals) == 1 and levels > 1:
                vals = vals * levels
            elif len(vals) < levels:
                vals = vals + [vals[-1]] * (levels - len(vals))
            out.append({
                "skillId": int(sk.get("id")),
                "name": sk.get("name"),
                "kind": "percent" if name == "DamOverTimePercent" else "flat",
                "tickSeconds": max(1, ticks),
                "powers": vals[:levels],
            })

json.dump({
    "reference": (
        "Mobius DamOverTime / DamOverTimePercent from CT0 Interlude stats/skills. "
        "kind=percent → MaxHp fraction per tick; kind=flat → absolute HP per tick. "
        "tickSeconds is the effect ticks= attribute (seconds between hits)."
    ),
    "skills": out,
}, sys.stdout, indent=1)
