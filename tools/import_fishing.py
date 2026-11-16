#!/usr/bin/env python3
"""Transcribe the Mobius CT 0 Interlude fishing data into data/fishing.json and add what it needs elsewhere.

Reads the Mobius datapack (not copied into this repo) and writes original JSON. No GPL source is copied.

Mobius references (L2J_Mobius_CT_0_Interlude/dist/game/data):
  stats/fishing/fishes.xml            FishData (level, group, grade, guts, hp, regen, timers)
  stats/fishing/fishingRods.xml       FishingRodsData (rod level, rod damage)
  stats/fishing/fishingMonsters.xml   FishingMonstersData (level band → monster, chance)
  zones/fishing.xml                   FishingZone NPoly + minZ/maxZ
  stats/players/skillTrees/fishingSkillTree.xml   Fishing / Pumping / Reeling / Fishing Expertise rows
  stats/skills/*.xml                  skills 1312–1315 (mp, reuse, power table) and the fish item skills
                                      (RestorationRandom product groups)
  stats/items/*.xml                   fish, lures and fish products (EtcItem name / icon / price / weight)
  html/fisherman/*.htm                Fishing Guild Member pages

Writes:
  data/fishing.json                   fish, rods, monsters, zones, fishing skill tree, skill power table
  data/items.json                     + missing fish / lure / product etc rows; fishing shots get "fishShot"
  data/skills.json                    + 1312, 1313, 1314, 1315
  data/item-skills.json               + one "restorationRandom" row per fish item
  data/html/fisherman/*.htm           + missing pages (existing files are kept)

Usage:
  python3 tools/import_fishing.py /path/to/dist/game/data
"""

from __future__ import annotations

import json
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

GROUPS = {"wide": 0, "swift": 1, "ugly": 2, "fish_box": 3, "easy_wide": 4, "easy_swift": 5, "easy_ugly": 6,
          "hard_wide": 7, "hard_swift": 8, "hard_ugly": 9, "hs_fish": 10}
GRADES = {"fish_easy": 0, "fish_normal": 1, "fish_hard": 2}
FISHING_SKILLS = (1312, 1313, 1314, 1315)


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def item_templates(item_dir: Path) -> dict[int, dict]:
    out = {}
    for f in sorted(item_dir.glob("*.xml")):
        for it in ET.parse(f).getroot():
            if local(it.tag) != "item":
                continue
            row = {"type": it.get("type"), "name": it.get("name"), "skills": []}
            for s in it:
                if local(s.tag) == "set":
                    row[s.get("name")] = s.get("val")
                elif local(s.tag) == "skills":
                    row["skills"] = [(int(k.get("id")), int(k.get("level", "1"))) for k in s]
            out[int(it.get("id"))] = row
    return out


def skill_nodes(skill_dir: Path) -> dict[int, ET.Element]:
    out = {}
    for f in sorted(skill_dir.rglob("*.xml")):
        for sk in ET.parse(f).getroot():
            if local(sk.tag) == "skill":
                out[int(sk.get("id"))] = sk
    return out


def table(sk: ET.Element, name: str) -> list[str] | None:
    for t in sk:
        if local(t.tag) == "table" and t.get("name", "").lstrip("#") == name.lstrip("#"):
            return (t.text or "").split()
    return None


def scalar(sk: ET.Element, tag: str, level: int = 1, default: str = "0") -> str:
    for c in sk:
        if local(c.tag) == tag:
            text = (c.text or "").strip()
            if text.startswith("#"):
                values = table(sk, text) or [default]
                return values[min(level, len(values)) - 1]
            return text or default
    return default


def restoration_groups(sk: ET.Element, level: int) -> list[dict] | None:
    for effs in sk:
        if local(effs.tag) != "effects":
            continue
        for e in effs:
            if e.get("name") != "RestorationRandom":
                continue
            items = next((c for c in e if local(c.tag) == "items"), None)
            if items is None:
                return None
            value = next((v for v in items if local(v.tag) == "value" and int(v.get("level", "1")) == level), None)
            node = value if value is not None else items
            groups = []
            for g in node:
                if local(g.tag) != "item":
                    continue
                groups.append({
                    "chance": float(g.get("chance")),
                    "items": [{"itemId": int(i.get("id")), "count": int(i.get("count"))} for i in g if local(i.tag) == "item"],
                })
            return groups
    return None


def etc_row(iid: int, t: dict) -> dict:
    row = {"id": iid, "name": t["name"], "kind": "etc"}
    if t.get("bodypart") == "lhand":
        row["bodySlot"] = "lhand"
    if t.get("etcitem_type") == "LURE":
        row["etcType"] = "lure"
    if t.get("is_stackable") == "true":
        row["stackable"] = True
    row["icon"] = t.get("icon", "")
    row["price"] = int(t.get("price", 0))
    row["weight"] = int(t.get("weight", 0))
    return row


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    base = Path(sys.argv[1])
    templates = item_templates(base / "stats/items")
    skills = skill_nodes(base / "stats/skills")

    fish = []
    for f in ET.parse(base / "stats/fishing/fishes.xml").getroot():
        if local(f.tag) != "fish":
            continue
        a = f.attrib
        fish.append({
            "fishId": int(a["fishId"]), "itemId": int(a["itemId"]), "name": a["itemName"],
            "group": GROUPS[a["fishGroup"]], "grade": GRADES[a["fishGrade"]], "level": int(a["fishLevel"]),
            "guts": float(a["fishGuts"]), "hp": int(a["fishHp"]), "hpRegen": float(a["hpRegen"]),
            "startCombatTime": int(a["startCombatTime"]), "combatDuration": int(a["combatDuration"]),
            "gutsCheckTime": int(a["gutsCheckTime"]),
        })
    rods = [{"itemId": int(r.get("fishingRodItemId")), "name": r.get("fishingRodName"),
             "level": int(r.get("fishingRodLevel")), "damage": float(r.get("fishingRodDamage"))}
            for r in ET.parse(base / "stats/fishing/fishingRods.xml").getroot() if local(r.tag) == "fishingRod"]
    monsters = [{"minLevel": int(m.get("userMinLevel")), "maxLevel": int(m.get("userMaxLevel")),
                 "npcId": int(m.get("fishingMonsterId")), "probability": int(m.get("probability"))}
                for m in ET.parse(base / "stats/fishing/fishingMonsters.xml").getroot() if local(m.tag) == "fishingMonster"]
    zones = []
    for z in ET.parse(base / "zones/fishing.xml").getroot():
        if local(z.tag) != "zone" or z.get("type") != "FishingZone":
            continue
        nodes = [[int(n.get("X")), int(n.get("Y"))] for n in z if local(n.tag) == "node"]
        zones.append({"name": z.get("name"), "minZ": int(z.get("minZ")), "maxZ": int(z.get("maxZ")), "nodes": nodes})

    tree = []
    for t in ET.parse(base / "stats/players/skillTrees/fishingSkillTree.xml").getroot():
        for s in t:
            sid = int(s.get("skillId"))
            if sid not in FISHING_SKILLS:
                continue  # Expand Storage / Inventory / Craft / Trade: their effects are not modelled yet
            tree.append({"skillId": sid, "level": int(s.get("skillLevel")), "getLevel": int(s.get("getLevel")),
                         "items": [{"itemId": int(i.get("id")), "count": int(i.get("count"))} for i in s if local(i.tag) == "item"]})
    power = [float(v) for v in table(skills[1313], "#power")]

    out = ROOT / "data/fishing.json"
    out.write_text(json.dumps({
        "reference": "L2J Mobius CT0 Interlude stats/fishing (fishes, fishingRods, fishingMonsters), zones/fishing.xml, "
                     "fishingSkillTree.xml (1312-1315) and the Pumping/Reeling/Fishing Expertise #power table. "
                     "Generated by tools/import_fishing.py.",
        "skillPower": power,
        "rods": rods, "monsters": monsters, "skillTree": tree, "zones": zones, "fish": fish,
    }, indent=1, ensure_ascii=False) + "\n")

    # Items: fish, lures, fish products; shots flagged.
    items_path = ROOT / "data/items.json"
    items = json.loads(items_path.read_text())
    by_id = {i["id"]: i for i in items["items"]}
    item_skill_rows = []
    want = {f["itemId"] for f in fish}
    want |= {iid for iid, t in templates.items() if t.get("etcitem_type") == "LURE"}
    for f in sorted({f["itemId"] for f in fish}):
        t = templates.get(f)
        if not t or not t["skills"]:
            continue
        sid, lvl = t["skills"][0]
        groups = restoration_groups(skills[sid], lvl) if sid in skills else None
        if not groups:
            continue
        for g in groups:
            want |= {i["itemId"] for i in g["items"]}
        item_skill_rows.append({"itemId": f, "name": t["name"], "etcType": None, "reuseDelayMs": 0,
                                "effects": [{"kind": "restorationRandom", "skillId": sid, "skillLevel": lvl,
                                             "name": skills[sid].get("name"), "groups": groups}]})
    added = 0
    for iid in sorted(want - set(by_id)):
        t = templates.get(iid)
        if t is None or t["type"] != "EtcItem":
            continue
        row = etc_row(iid, t)
        items["items"].append(row)
        by_id[iid] = row
        added += 1
    shots = 0
    for iid, t in templates.items():
        if t.get("default_action") == "FISHINGSHOT" and iid in by_id and not by_id[iid].get("fishShot"):
            by_id[iid]["fishShot"] = True
            shots += 1
    items["items"].sort(key=lambda i: i["id"])
    items_path.write_text(json.dumps(items, indent=2, ensure_ascii=False) + "\n")

    # item-skills.json: fish open into products (Mobius ItemSkills → RestorationRandom).
    is_path = ROOT / "data/item-skills.json"
    item_skills = json.loads(is_path.read_text())
    have = {r["itemId"] for r in item_skills["items"]}
    new_rows = [r for r in item_skill_rows if r["itemId"] not in have]
    item_skills["items"] += new_rows
    item_skills["items"].sort(key=lambda r: r["itemId"])
    is_path.write_text(json.dumps(item_skills, indent=2, ensure_ascii=False) + "\n")

    # skills.json: 1312 Fishing, 1313 Pumping, 1314 Reeling, 1315 Fishing Expertise (passive).
    sk_path = ROOT / "data/skills.json"
    sk_doc = json.loads(sk_path.read_text())
    have_sk = {s["skillId"] for s in sk_doc["skills"]}
    for sid in FISHING_SKILLS:
        if sid in have_sk:
            continue
        node = skills[sid]
        levels = int(node.get("levels", "1"))
        passive = scalar(node, "operateType") == "P"
        sk_doc["skills"].append({
            "skillId": sid, "name": node.get("name"), "levels": levels, "passive": passive,
            "operateType": scalar(node, "operateType"), "targetType": scalar(node, "targetType"),
            "castRange": -1, "hitTimeMs": int(scalar(node, "hitTime")), "reuseDelayMs": int(scalar(node, "reuseDelay")),
            "levelData": [{"level": lv, "mpConsume": int(scalar(node, "mpConsume", lv)), "power": 0, "abnormalLevel": 0,
                           "magicLevel": int(scalar(node, "magicLevel", lv))} for lv in range(1, levels + 1)],
            "magicType": 2, "effectRange": -1,
        })
    sk_path.write_text(json.dumps(sk_doc, indent=2, ensure_ascii=False) + "\n")

    # html/fisherman pages.
    html_added = 0
    dst = ROOT / "data/html/fisherman"
    dst.mkdir(parents=True, exist_ok=True)
    for src in sorted((base / "html/fisherman").glob("*.htm")):
        target = dst / src.name
        if not target.exists():
            shutil.copyfile(src, target)
            html_added += 1

    print(f"fish={len(fish)} rods={len(rods)} monsters={len(monsters)} zones={len(zones)} tree={len(tree)} "
          f"items+={added} shots={shots} itemSkills+={len(new_rows)} html+={html_added}")


if __name__ == "__main__":
    main()
