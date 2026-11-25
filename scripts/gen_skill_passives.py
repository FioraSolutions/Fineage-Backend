#!/usr/bin/env python3
"""Generate data/skill-passives.json from L2J Mobius CT0 Interlude passive (operateType P) skills.
Usage: gen_skill_passives.py <mobius dist/game/data/stats/skills dir> > data/skill-passives.json

Combat <for> funcs go into effects[]. DefenceTrait / AttackTrait params (POISON/HOLD/…) are kept on
defenceTraits / attackTraits so jewelry resists (Queen Ant, Antharas, Zaken, Orfen, Core) are not dropped.
"""
import glob, json, sys, xml.etree.ElementTree as ET
STATS = {"runSpd","pAtk","pDef","mAtk","mDef","pAtkSpd","mAtkSpd","accCombat","rEvas","critRate",
         "regHp","regMp","regCp","weightLimit","weightPenalty","mCritRate","mCritPower","critDmg","critDmgAdd","blowRate","pSkillEvas"}
OPS = {"add":"add","sub":"sub","mul":"mul","set":"set"}

def kinds_of(node):
    """Returns (ok, kinds, resting) where kinds is None for unconditional; ok False for unsupported conditions.

    Mobius conditions we can honour: ``<using kind="DAGGER"/>`` (right-hand weapon / chest+legs armor / shield) and
    ``<player resting="true"/>`` (Vital Force 148 regen only while seated).
    """
    conds = [c for c in node if c.tag not in OPS and c.tag not in ("effect",)]
    if not conds: return True, None, False
    kinds = None
    resting = False
    for c in conds:
        if c.tag == "using" and c.get("kind"):
            kinds = [k.strip().upper() for k in c.get("kind").split(",")]
        elif c.tag == "player" and c.get("resting", "false").lower() == "true":
            resting = True
        elif c.tag == "player" and len(c.attrib) == 0:
            continue
        else:
            return False, None, False
    return True, kinds, resting

def resolve_table(tables, raw, levels):
    if raw is None:
        return None
    raw = raw.strip()
    if raw.startswith("#"):
        vals = tables.get(raw)
        if not vals:
            return None
        out = []
        for i in range(levels):
            out.append(float(vals[i] if i < len(vals) else vals[-1]))
        return out
    try:
        v = float(raw)
    except ValueError:
        return None
    return [v] * levels

def trait_map(effect_node, tables, levels):
    """Mobius <param POISON="30"/> attributes on DefenceTrait/AttackTrait children."""
    if effect_node is None:
        return None
    traits = {}
    for child in list(effect_node):
        if child.tag != "param":
            continue
        for key, raw in child.attrib.items():
            vals = resolve_table(tables, raw, levels)
            if vals is None:
                continue
            traits[key.lower()] = vals
    return traits or None

out = []
for f in sorted(glob.glob(sys.argv[1] + "/*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        sets = {s.get("name"): s.get("val") for s in sk.findall("set")}
        if sets.get("operateType") != "P": continue
        levels = int(sk.get("levels", "1"))
        tables = {t.get("name"): t.text.split() for t in sk.findall("table")}
        # table names in XML are "#dot" style in val but table name attr is without hash sometimes
        tables = {("#" + k if not k.startswith("#") else k): v for k, v in tables.items()}
        # also keep unhashed for resolve of "#traits"
        for k, v in list(tables.items()):
            if k.startswith("#"):
                tables[k[1:]] = v
        skill_cond = None
        skill_resting = False
        cond = sk.find("cond")
        if cond is not None:
            ok, skill_cond, skill_resting = kinds_of(cond)
            if not ok: continue
        funcs = []
        fr = sk.find("for")
        defence = None
        attack = None
        if fr is not None:
            for parent in [fr] + list(fr.findall("effect")):
                ename = parent.get("name") if parent is not fr else None
                if ename == "DefenceTrait":
                    defence = trait_map(parent, tables, levels)
                    continue
                if ename == "AttackTrait":
                    attack = trait_map(parent, tables, levels)
                    continue
                for fn in parent:
                    if fn.tag not in OPS or fn.get("stat") not in STATS: continue
                    ok, kinds, resting = kinds_of(fn)
                    if not ok: continue
                    v = fn.get("val", "0")
                    try:
                        vals = [float(x) for x in tables[v]] if v.startswith("#") else [float(v)]
                    except (KeyError, ValueError):
                        continue
                    kinds = kinds or skill_cond
                    resting = resting or skill_resting
                    row = {"stat": fn.get("stat"), "op": OPS[fn.tag], "values": vals}
                    if kinds: row["using"] = kinds
                    if resting: row["resting"] = True
                    funcs.append(row)
        # Every passive row is kept, including the ones whose Mobius funcs are not combat stat mods
        # (Lucky 194, Expertise D 239, Create Item 172, Divine Inspiration 1405, Equip Set Items 3006).
        # Dropping them made the stat fold warn "passive has no combat mods" for a skill that simply has
        # no combat func — a false alarm. A row without "effects"/traits is an explicit "grants nothing".
        row = {"skillId": int(sk.get("id")), "name": sk.get("name"), "levels": levels}
        if funcs:
            row["effects"] = funcs
        if defence:
            row["defenceTraits"] = defence
        if attack:
            row["attackTraits"] = attack
        out.append(row)

json.dump({"reference": "Combat stat funcs of passive (operateType P) skills from L2J Mobius CT 0 Interlude dist/game/data/stats/skills. 'using' = Mobius <using kind> condition (weapon type of the right hand, armor type of chest+legs, SHIELD). Funcs with other conditions are skipped. defenceTraits/attackTraits carry Mobius DefenceTrait/AttackTrait percent tables (POISON/HOLD/SHOCK/…). A row with no effects and no traits is a real passive that grants no combat stat mod (Lucky, Expertise D, Create Item, Divine Inspiration, Equip Set Items 3006); it is kept so the catalog answers for every passive id.",
           "skills": out}, sys.stdout, indent=1)
