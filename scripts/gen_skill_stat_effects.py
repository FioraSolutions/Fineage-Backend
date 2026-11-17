#!/usr/bin/env python3
"""Generate data/skill-stat-effects.json from L2J Mobius CT0 Interlude non-passive skills.

Usage: gen_skill_stat_effects.py <mobius dist/game/data/stats/skills dir>

Conditions we can honour on a function:
  * ``<player front="true"/>`` / ``<player behind="true"/>`` → ``position`` front/behind
  * ``<and><player front="false"/><player behind="false"/></and>`` → ``position`` side
  * ``<using kind="DAGGER"/>`` (on the function or on the skill ``<cond>``) → ``using`` kinds

Everything else is skipped, so a conditional function never leaks into an unconditional fold.
"""
import glob, json, sys, xml.etree.ElementTree as ET

STATS = {"pAtk", "pDef", "mAtk", "mDef", "pAtkSpd", "mAtkSpd", "accCombat", "rEvas", "critRate", "runSpd",
         "mCritRate", "mCritPower", "critDmg", "critDmgAdd", "critRatePos", "critDmgPos", "blowRate", "pSkillEvas"}
OPS = {"add": "add", "sub": "sub", "mul": "mul", "set": "set"}


def kinds_of(node, tables):
    """(ok, using kinds or None, position or None) for the condition children of ``node``."""
    conds = [c for c in node if c.tag not in OPS and c.tag not in ("effect", "param")]
    if not conds:
        return True, None, None

    kinds = None
    front = behind = None
    side = False
    for c in conds:
        if c.tag == "using" and c.get("kind"):
            kinds = [k.strip().upper() for k in c.get("kind").split(",") if k.strip()]
        elif c.tag == "player" and "front" in c.attrib:
            front = c.get("front").lower() == "true"
        elif c.tag == "player" and "behind" in c.attrib:
            behind = c.get("behind").lower() == "true"
        elif c.tag == "and":
            ok, k, pos = kinds_of(c, tables)
            if not ok:
                return False, None, None
            kinds = kinds or k
            if pos == "side":
                side = True
            elif pos:
                return False, None, None
        else:
            return False, None, None

    if side:
        return True, kinds, "side"
    if front is True and behind is None:
        return True, kinds, "front"
    if behind is True and front is None:
        return True, kinds, "behind"
    if front is False and behind is False:
        return True, kinds, "side"
    if front is None and behind is None:
        return True, kinds, None
    return False, None, None


out = []
for f in sorted(glob.glob(sys.argv[1] + "/*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        sets = {s.get("name"): s.get("val") for s in sk.findall("set")}
        if sets.get("operateType") == "P":
            continue
        levels = int(sk.get("levels", "1"))
        tables = {t.get("name"): t.text.split() for t in sk.findall("table")}

        # Skill-level condition (Focus Chance / Focus Power: dagger required) applies to every function and also gates
        # the cast itself, so the row keeps it as its own "using".
        skill_using = None
        cond = sk.find("cond")
        if cond is not None:
            ok, skill_using, skill_pos = kinds_of(cond, tables)
            if not ok or skill_pos:
                skill_using = None

        fr = sk.find("for")
        if fr is None:
            continue
        funcs = []
        for parent in [fr] + list(fr.findall("effect")):
            for fn in parent:
                if fn.tag not in OPS or fn.get("stat") not in STATS:
                    continue
                ok, kinds, position = kinds_of(fn, tables)
                if not ok:
                    continue
                v = fn.get("val", "0")
                try:
                    vals = [float(x) for x in tables[v]] if v.startswith("#") else [float(v)] * levels
                except (KeyError, ValueError):
                    continue
                if len(vals) == 1 and levels > 1:
                    vals *= levels
                row = {"stat": fn.get("stat"), "op": OPS[fn.tag], "values": vals}
                kinds = kinds or skill_using
                if kinds:
                    row["using"] = kinds
                if position:
                    row["position"] = position
                funcs.append(row)
        if funcs:
            skill_row = {"skillId": int(sk.get("id")), "name": sk.get("name"), "levels": levels, "effects": funcs}
            if skill_using:
                skill_row["using"] = skill_using
            out.append(skill_row)

reference = ("Combat stat functions (mul/add/sub/set on pAtk, pDef, mAtk, mDef, pAtkSpd, mAtkSpd, accCombat, rEvas, critRate, runSpd, "
             "mCritRate, mCritPower, critDmg, critDmgAdd, critRatePos, critDmgPos, blowRate, pSkillEvas) inside <for><effect> "
             "of non-passive skills, transcribed from L2J Mobius CT 0 Interlude dist/game/data/stats/skills. values are per skill "
             "level (index 0 = level 1). 'position' = Mobius <player front|behind> condition (side = neither); 'using' = the "
             "skill's <using kind> weapon/armor gate. Enchant routes and non-combat stats are not included.")
json.dump({"reference": reference, "skills": out}, sys.stdout, indent=1)
