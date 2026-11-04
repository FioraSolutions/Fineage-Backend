#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude blow data (FatalBlow/Backstab effect, blowChance, baseCritRate, Lethal full/half,
magicLevel table) to data/skill-combat.json. Usage: gen_skill_blows.py <mobius skills xml dir>"""
import json, re, sys, pathlib

IDS = [16, 30, 263, 321, 344, 409]
src = pathlib.Path(sys.argv[1])
xml = "".join(p.read_text() for p in sorted(src.glob("*.xml")))
xml = re.sub(r"<!--.*?-->", "", xml, flags=re.S)
path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skill-combat.json"
data = json.loads(path.read_text())
by_id = {s["skillId"]: s for s in data["skills"]}

def table(body, name):
    m = re.search(r'<table name="#%s">([^<]*)</table>' % name, body)
    return [float(x) for x in m.group(1).split()] if m else None

def scalar(body, tag, levels, cast=int):
    m = re.search(r"<%s>([^<]*)</%s>" % (tag, tag), body)
    if not m:
        return None
    v = m.group(1).strip()
    if v.startswith("#"):
        t = table(body, v[1:])
        return [cast(x) for x in t] if t else None
    return cast(float(v))

for sid in IDS:
    m = re.search(r'<skill id="%d" levels="(\d+)" name="([^"]+)".*?</skill>' % sid, xml, re.S)
    if not m:
        raise SystemExit(f"skill {sid} not found")
    body, levels, name = m.group(0), int(m.group(1)), m.group(2)
    effect = "backstab" if '<effect name="Backstab"' in body else "fatalBlow"
    entry = by_id.get(sid)
    fresh = entry is None
    if fresh:
        entry = {"skillId": sid, "name": name, "levels": levels, "kind": "blow", "magic": False}
        data["skills"].append(entry)
        by_id[sid] = entry
    # Existing catalog rows keep their power/MP/timing; only skills missing from the catalog (Backstab) get them here.
    base = entry if not fresh else None
    if fresh:
      entry["power"] = scalar(body, "power", levels, float)
      entry["mpConsume"] = scalar(body, "mpConsume", levels)
      entry.setdefault("mpInitialConsume", [0] * levels)
      entry["hitTimeMs"] = scalar(body, "hitTime", levels)
      entry["coolTimeMs"] = scalar(body, "coolTime", levels) or 0
      entry["reuseDelayMs"] = scalar(body, "reuseDelay", levels)
      entry["castRange"] = scalar(body, "castRange", levels)
      entry["baseCritRate"] = scalar(body, "baseCritRate", levels) or 0
    entry["blowEffect"] = effect
    entry["blowChance"] = scalar(body, "blowChance", levels) or 0
    ml = scalar(body, "magicLevel", levels)
    entry["magicLevel"] = ml if isinstance(ml, list) else [ml or 0] * levels
    lm = re.search(r'<effect name="Lethal">(.*?)</effect>', body, re.S)
    entry["fullLethal"] = int(re.search(r"<fullLethal>(\d+)", lm.group(1)).group(1)) if lm and "<fullLethal>" in lm.group(1) else 0
    entry["halfLethal"] = int(re.search(r"<halfLethal>(\d+)", lm.group(1)).group(1)) if lm and "<halfLethal>" in lm.group(1) else 0
    for k in ("power", "mpConsume"):
        v = entry[k]
        if not isinstance(v, list):
            entry[k] = [v] * levels
    if fresh:
        entry["overHit"] = "<overHit>true</overHit>" in body
    print(sid, name, effect, entry["blowChance"], entry["baseCritRate"], entry["fullLethal"], entry["halfLethal"], len(entry["power"]))

data["skills"].sort(key=lambda s: s["skillId"])
path.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n")
