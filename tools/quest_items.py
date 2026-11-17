#!/usr/bin/env python3
"""Quest item upkeep for items.json from the Mobius CT_0 item XML (git object in /workspace/mobius):
missing ids are added as quest etc items, and quest weapons still listed as `etc` become equippable weapons.
Usage: python3 tools/quest_items.py 3026 2638 ..."""
import json, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOBIUS = os.environ.get("MOBIUS_REPO", "/workspace/mobius")
STAT = {"pAtk": "pAtk", "mAtk": "mAtk", "pAtkSpd": "pAtkSpd", "critRate": "critRate", "pAtkRange": "attackRange",
        "randomDamage": "randomDamage"}


def xml_for(item_id, kind="Weapon"):
    lo = item_id // 100 * 100
    path = f"L2J_Mobius_CT_0_Interlude/dist/game/data/stats/items/{lo:05d}-{lo + 99:05d}.xml"
    text = subprocess.run(["git", "-C", MOBIUS, "show", f"HEAD:{path}"], capture_output=True, text=True, check=True).stdout
    m = re.search(rf'<item id="{item_id}" type="{kind}".*?</item>', text, re.S)
    return m.group(0) if m else None


def main(ids):
    path = f"{ROOT}/data/items.json"
    raw = open(path, encoding="utf-8").read()
    data = json.loads(raw)
    assert json.dumps(data, indent=2, ensure_ascii=False) + "\n" == raw, "items.json is not in canonical form"
    have = {row["id"] for row in data["items"]}
    added = set()
    for item_id in sorted(ids - have):
        xml = xml_for(item_id, "EtcItem") or xml_for(item_id, "Weapon")
        if xml is None:
            print(item_id, "not found in Mobius")
            continue
        name = re.search(r'name="([^"]*)"', xml).group(1)
        icon = dict(re.findall(r'<set name="(\w+)" val="([^"]*)"', xml)).get("icon", "")
        data["items"].append({"id": item_id, "name": name, "kind": "etc", "icon": icon, "price": 0, "weight": 0,
                              "stackable": True, "quest": True})
        added.add(item_id)
        print("added:", item_id, name)
    data["items"].sort(key=lambda r: r["id"])
    for i, row in enumerate(data["items"]):
        if row["id"] not in ids or row["id"] in added or row.get("kind") == "weapon":
            continue
        xml = xml_for(row["id"])
        if xml is None:
            print(row["id"], "is not a weapon in Mobius")
            continue
        sets = dict(re.findall(r'<set name="(\w+)" val="([^"]*)"', xml))
        new = {"id": row["id"], "name": row["name"], "kind": "weapon", "icon": sets.get("icon", row.get("icon", "")),
               "bodySlot": sets["bodypart"], "weaponType": {"dualfist": "fist"}.get(sets["weapon_type"].lower(), sets["weapon_type"].lower()),
               "price": int(sets.get("price", 0)), "weight": int(sets.get("weight", 0))}
        for stat, val in re.findall(r'<stat type="(\w+)">([-\d.]+)</stat>', xml):
            if stat in STAT:
                new[STAT[stat]] = int(float(val))
        new["soulshots"] = int(sets.get("soulshots", 0))
        new["spiritshots"] = int(sets.get("spiritshots", 0))
        if row.get("quest"):
            new["quest"] = True
        data["items"][i] = new
        print("weapon:", new)
    open(path, "w", encoding="utf-8").write(json.dumps(data, indent=2, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    main({int(a) for a in sys.argv[1:]})
