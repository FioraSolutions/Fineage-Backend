#!/usr/bin/env python3
"""Generate data/skill-stat-effects.json from L2J Mobius CT0 Interlude non-passive skills.
Usage: gen_skill_stat_effects.py <mobius dist/game/data/stats/skills dir> > data/skill-stat-effects.json"""
import glob, json, sys, xml.etree.ElementTree as ET

STATS = {"pAtk", "pDef", "mAtk", "mDef", "pAtkSpd", "mAtkSpd", "accCombat", "rEvas", "critRate", "runSpd",
         "mCritRate", "mCritPower"}
OPS = {"add": "add", "sub": "sub", "mul": "mul", "set": "set"}

out = []
for f in sorted(glob.glob(sys.argv[1] + "/*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        sets = {s.get("name"): s.get("val") for s in sk.findall("set")}
        if sets.get("operateType") == "P":
            continue
        levels = int(sk.get("levels", "1"))
        tables = {t.get("name"): t.text.split() for t in sk.findall("table")}
        fr = sk.find("for")
        if fr is None:
            continue
        funcs = []
        for parent in [fr] + list(fr.findall("effect")):
            for fn in parent:
                if fn.tag not in OPS or fn.get("stat") not in STATS:
                    continue
                v = fn.get("val", "0")
                try:
                    vals = [float(x) for x in tables[v]] if v.startswith("#") else [float(v)] * levels
                except (KeyError, ValueError):
                    continue
                if len(vals) == 1 and levels > 1:
                    vals *= levels
                funcs.append({"stat": fn.get("stat"), "op": OPS[fn.tag], "values": vals})
        if funcs:
            out.append({"skillId": int(sk.get("id")), "name": sk.get("name"), "levels": levels, "effects": funcs})

reference = ("Combat stat functions (mul/add/sub/set on pAtk, pDef, mAtk, mDef, pAtkSpd, mAtkSpd, accCombat, rEvas, critRate, runSpd, "
             "mCritRate, mCritPower) inside <for><effect> of non-passive skills, transcribed from L2J Mobius CT 0 Interlude "
             "dist/game/data/stats/skills. values are per skill level (index 0 = level 1). Conditions, enchant routes and "
             "non-combat stats are not included.")
json.dump({"reference": reference, "skills": out}, sys.stdout, indent=1)
