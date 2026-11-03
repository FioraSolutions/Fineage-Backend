#!/usr/bin/env python3
"""Generate data/skill-passives.json from L2J Mobius CT0 Interlude passive (operateType P) skills.
Usage: gen_skill_passives.py <mobius dist/game/data/stats/skills dir> > data/skill-passives.json"""
import glob, json, sys, xml.etree.ElementTree as ET
STATS = {"runSpd","pAtk","pDef","mAtk","mDef","pAtkSpd","mAtkSpd","accCombat","rEvas","critRate",
         "regHp","regMp","weightLimit","weightPenalty"}
OPS = {"add":"add","sub":"sub","mul":"mul","set":"set"}
def kinds_of(node):
    """Returns (ok, kinds) where kinds is None for unconditional; ok False for unsupported conditions."""
    conds = [c for c in node if c.tag not in OPS and c.tag not in ("effect",)]
    if not conds: return True, None
    if len(conds) == 1 and conds[0].tag == "using" and conds[0].get("kind"):
        return True, [k.strip().upper() for k in conds[0].get("kind").split(",")]
    return False, None
out = []
for f in sorted(glob.glob(sys.argv[1] + "/*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        sets = {s.get("name"): s.get("val") for s in sk.findall("set")}
        if sets.get("operateType") != "P": continue
        levels = int(sk.get("levels", "1"))
        tables = {t.get("name"): t.text.split() for t in sk.findall("table")}
        skill_cond = None
        cond = sk.find("cond")
        if cond is not None:
            ok, skill_cond = kinds_of(cond)
            if not ok: continue
        funcs = []
        fr = sk.find("for")
        if fr is None: continue
        for parent in [fr] + list(fr.findall("effect")):
            for fn in parent:
                if fn.tag not in OPS or fn.get("stat") not in STATS: continue
                ok, kinds = kinds_of(fn)
                if not ok: continue
                v = fn.get("val", "0")
                try:
                    vals = [float(x) for x in tables[v]] if v.startswith("#") else [float(v)]
                except (KeyError, ValueError):
                    continue
                kinds = kinds or skill_cond
                row = {"stat": fn.get("stat"), "op": OPS[fn.tag], "values": vals}
                if kinds: row["using"] = kinds
                funcs.append(row)
        if funcs:
            out.append({"skillId": int(sk.get("id")), "name": sk.get("name"), "levels": levels, "effects": funcs})
json.dump({"reference": "Combat stat funcs of passive (operateType P) skills from L2J Mobius CT 0 Interlude dist/game/data/stats/skills. 'using' = Mobius <using kind> condition (weapon type of the right hand, armor type of chest+legs, SHIELD). Funcs with other conditions are skipped.",
           "skills": out}, sys.stdout, indent=1)
