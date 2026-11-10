#!/usr/bin/env python3
"""Patch data/skills.json with Mobius abnormal / land-rate metadata.

Adds:
  - abnormalType (POISON, TURN_FLEE, …) → ZoneAbnormalEntry.Slot
  - trait (POISON, HOLD, DERANGEMENT, …) → calcGeneralTraitBonus type=3
  - activateRate / lvlBonusRate / basicProperty → calcSkillSuccess
  - per-level magicLevel in levelData when the XML table exists

Same enrichment pattern as gen_skill_cast_meta.py.

Usage: gen_skill_abnormal_meta.py <mobius dist/game/data/stats/skills dir>
"""
import json, re, sys, pathlib, xml.etree.ElementTree as ET

src = pathlib.Path(sys.argv[1])
path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skills.json"
raw = path.read_text()
data = json.loads(raw)
indent = 1 if raw.startswith("{\n \"") else 2

# skillId → parsed sets + tables
meta = {}
for f in sorted(src.glob("*.xml")):
    for sk in ET.parse(f).getroot().iter("skill"):
        sid = int(sk.get("id"))
        sets = {s.get("name"): s.get("val") for s in sk.findall("set")}
        tables = {}
        for t in sk.findall("table"):
            name = t.get("name") or ""
            vals = (t.text or "").split()
            tables[name] = vals
            if name.startswith("#"):
                tables[name[1:]] = vals
            else:
                tables["#" + name] = vals
        meta[sid] = (sets, tables, int(sk.get("levels", "1")))

counts = {k: 0 for k in ("abnormalType", "trait", "activateRate", "magicLevel")}
for s in data["skills"]:
    for key in ("abnormalType", "trait", "activateRate", "lvlBonusRate", "basicProperty"):
        s.pop(key, None)
    for lv in s.get("levelData") or []:
        lv.pop("magicLevel", None)

    sets, tables, levels = meta.get(s["skillId"], ({}, {}, 1))
    if not sets:
        continue

    if abn := (sets.get("abnormalType") or "").strip():
        s["abnormalType"] = abn
        counts["abnormalType"] += 1
    if trait := (sets.get("trait") or "").strip():
        s["trait"] = trait.upper()
        counts["trait"] += 1
    if (ar := sets.get("activateRate")) and str(ar).lstrip("-").isdigit():
        s["activateRate"] = int(ar)
        counts["activateRate"] += 1
    if (lbr := sets.get("lvlBonusRate")) and str(lbr).isdigit():
        s["lvlBonusRate"] = int(lbr)
    if bp := (sets.get("basicProperty") or "").strip():
        s["basicProperty"] = bp.upper()

    ml_raw = sets.get("magicLevel")
    ml_vals = None
    if ml_raw:
        if str(ml_raw).startswith("#"):
            ml_vals = tables.get(ml_raw) or tables.get(ml_raw[1:])
        elif str(ml_raw).isdigit():
            ml_vals = [ml_raw]
    if ml_vals:
        counts["magicLevel"] += 1
        for i, lv in enumerate(s.get("levelData") or []):
            try:
                lv["magicLevel"] = int(float(ml_vals[i] if i < len(ml_vals) else ml_vals[-1]))
            except (ValueError, IndexError):
                pass

path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
print(
    f"abnormalType={counts['abnormalType']} trait={counts['trait']} "
    f"activateRate={counts['activateRate']} magicLevel={counts['magicLevel']} "
    f"of {len(data['skills'])} skills"
)
