#!/usr/bin/env python3
"""Generate data/item-skills.json — what Mobius does when an EtcItem is used (handler ItemSkills).

Mobius UseItem: a non-equippable item goes through ItemHandler. For handler="ItemSkills" the handler casts every
<set name="item_skill"> reference (skillId-level) on the player and, when the cast succeeds, destroys one item.
This script flattens that chain into one semantic row per item so the zone can execute the effect without a full
skill engine (buff / healOverTime / heal / manaHeal / escape / teleport / dispel / restoration / recovery).

Only items present in data/items.json are emitted, and herbs (data/herbs.json, used on pickup, never in a bag) are
skipped because the pickup path already owns them.

Usage: gen_item_skills.py [mobius data dir] > data/item-skills.json
"""
import glob, json, pathlib, sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/data"

items_by_id = {i["id"]: i for i in json.loads((ROOT / "data/items.json").read_text())["items"]}
herbs = set()
herb_path = ROOT / "data/herbs.json"
if herb_path.exists():
    herbs = {i["itemId"] for i in json.loads(herb_path.read_text())["items"]}

skills = {}
for f in sorted(glob.glob(str(MOBIUS / "stats/skills/**/*.xml"), recursive=True)):
    for sk in ET.parse(f).getroot():
        if sk.tag == "skill":
            skills[int(sk.get("id"))] = sk

# Effects the zone can execute today. Anything else is reported on stderr and left out of the row, so an item whose
# only skill is unsupported stays unusable instead of silently doing nothing.
SUPPORTED = {
    "Buff", "HealOverTime", "Heal", "ManaHeal", "HealPercent", "ManaHealPercent",
    "Escape", "Teleport", "DispelBySlot", "Restoration", "Recovery",
}


def table(sk, name):
    name = name.lstrip("#")
    for t in sk.findall("table"):
        if (t.get("name") or "").lstrip("#") == name:
            return t.text.split()
    return None


def at(values, level):
    if values is None or not values:
        return None
    return values[min(max(level, 1), len(values)) - 1]


def funcs(sk):
    """Flatten <for> children (Buff funcs directly under <for>, others under <effect>)."""
    out = []
    fr = sk.find("for")
    if fr is None:
        return out
    for node in fr:
        if node.tag == "effect":
            params = {}
            for p in node:
                if p.tag == "param":
                    params.update(p.attrib)
            out.append((node.get("name"), node, params))
        else:
            out.append((node.tag, node, {}))
    return out


def param_int(sk, params, level, default=0, attr="power"):
    """Reads <effect><param power="8"/> through the per-level table form power="#power"."""
    value = params.get(attr)
    if value is None:
        return default
    if value.startswith("#"):
        value = at(table(sk, value), level) or default
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def set_int(sk, sets, name, level, default=0):
    """<set name="abnormalLevel" val="#abnormalLevels"> resolves through the per-level table."""
    value = sets.get(name)
    if value is None:
        return default
    if value.startswith("#"):
        value = at(table(sk, value[1:]), level) or default
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def flatten(sk, skill_id, level, item_name):
    sets = {s.get("name"): s.get("val") for s in sk.findall("set")}
    duration = set_int(sk, sets, "abnormalTime", level)
    rows = []
    for name, node, params in funcs(sk):
        if name in ("add", "mul", "sub", "set", "div"):
            continue
        if name not in SUPPORTED:
            print(f"skip {item_name}: unsupported effect {name} (skill {skill_id})", file=sys.stderr)
            continue
        row = {"kind": None, "skillId": skill_id, "skillLevel": level, "name": sk.get("name")}
        if name == "Buff":
            row["kind"] = "buff"
            row["abnormalLevel"] = max(1, set_int(sk, sets, "abnormalLevel", level, 1))
            row["durationSeconds"] = duration or 1200
        elif name == "HealOverTime":
            row.update(kind="healOverTime", power=param_int(sk, params, level),
                       ticks=int(node.get("ticks", "1") or 1), durationSeconds=duration or 15)
        elif name in ("Heal", "ManaHeal", "HealPercent", "ManaHealPercent"):
            row.update(kind={
                "Heal": "heal", "ManaHeal": "manaHeal",
                "HealPercent": "healPercent", "ManaHealPercent": "manaHealPercent",
            }[name], power=param_int(sk, params, level))
        elif name == "Escape":
            kind = params.get("escapeType", "TOWN")
            if kind != "TOWN":
                print(f"skip {item_name}: escape type {kind} (skill {skill_id})", file=sys.stderr)
                continue
            row.update(kind="escapeTown")
        elif name == "Teleport":
            coords = []
            for axis in ("x", "y", "z"):
                value = params.get(axis)
                if value is None:
                    coords.append(None)
                elif value.startswith("#"):
                    item = at(table(sk, value), level)
                    coords.append(int(item) if item is not None else None)
                else:
                    coords.append(int(value))
            row.update(kind="teleport", x=coords[0], y=coords[1], z=coords[2])
        elif name == "DispelBySlot":
            slot, _, count = params.get("dispel", "").partition(",")
            row.update(kind="dispel", slot=slot.strip(), count=int(count or 1))
        elif name == "Restoration":
            row.update(kind="restoration",
                       itemId=int(params.get("itemId", "0")), count=int(params.get("itemCount", "1")))
        elif name == "Recovery":
            row.update(kind="recovery")
        rows.append(row)
    return rows


out = []
for f in sorted(glob.glob(str(MOBIUS / "stats/items/*.xml"))):
    for item in ET.parse(f).getroot():
        if item.tag != "item":
            continue
        item_id = int(item.get("id"))
        if item_id not in items_by_id or item_id in herbs:
            continue
        sets = {s.get("name"): s.get("val") for s in item.findall("set")}
        if sets.get("handler") != "ItemSkills" or not sets.get("item_skill"):
            continue
        refs = []
        for part in sets["item_skill"].split(";"):
            part = part.strip()
            if not part:
                continue
            sid, _, level = part.partition("-")
            refs.append((int(sid), int(level) if level else 1))
        name = item.get("name")
        effects = []
        for sid, level in refs:
            sk = skills.get(sid)
            if sk is None:
                print(f"skip {name}: skill {sid} is not in the reference pack", file=sys.stderr)
                continue
            effects.extend(flatten(sk, sid, level, name))
        if not effects:
            continue
        out.append({
            "itemId": item_id,
            "name": name,
            "etcType": sets.get("etcitem_type"),
            "reuseDelayMs": int(sets.get("reuse_delay", "0") or 0),
            "effects": effects,
        })

doc = {
    "reference": (
        "L2J Mobius CT0 Interlude: stats/items handler ItemSkills + item_skill, resolved against stats/skills "
        "(including skills/custom). Effects are flattened to what the zone executes: buff, healOverTime, heal, "
        "manaHeal, escapeTown, teleport, dispel, restoration, recovery. Mobius UseItem consumes one item on success."
    ),
    "items": sorted(out, key=lambda r: r["itemId"]),
}
print(json.dumps(doc, indent=2, ensure_ascii=False))
