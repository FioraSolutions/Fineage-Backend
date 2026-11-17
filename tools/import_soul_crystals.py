#!/usr/bin/env python3
"""Soul crystal levelling table and crystal items from the Mobius CT_0 datapack (git object in /workspace/mobius).

Writes data/soul-crystals.json (values transcribed from LevelUpCrystalData.xml: crystal ladder, per-monster chance,
absorb mode, whether the Soul Crystal skill must be used, crystal levels the monster accepts) and adds the crystal
etc items to data/items.json. Usage: python3 tools/import_soul_crystals.py
"""
import json, os, re, subprocess
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOBIUS = os.environ.get("MOBIUS_REPO", "/workspace/mobius")
BASE = "L2J_Mobius_CT_0_Interlude/dist/game/data"


def show(path):
    return subprocess.run(["git", "-C", MOBIUS, "show", f"HEAD:{BASE}/{path}"], capture_output=True, text=True,
                          check=True).stdout


def main():
    root = ET.fromstring(show("LevelUpCrystalData.xml"))
    crystals = [{"itemId": int(i.get("itemId")), "level": int(i.get("level")), "next": int(i.get("leveledItemId"))}
                for i in root.find("crystal")]
    npcs = []
    for item in root.find("npc"):
        for d in item.findall("detail"):
            if d.get("maxLevel") is not None:
                levels = list(range(0, int(d.get("maxLevel")) + 1))
            else:
                levels = [int(x) for x in d.get("levelList").split(",")]
            npcs.append({"npcId": int(item.get("npcId")), "chance": min(100, int(d.get("chance", "5"))),
                         "skill": d.get("skill") == "true", "absorb": d.get("absorbType", "LAST_HIT"),
                         "levels": levels})
    out = {"reference": "L2J Mobius CT_0 Interlude LevelUpCrystalData.xml (values transcribed): chance is percent "
                        "(Mobius getRandom(100) <= chance), skill = the monster must have been hit by Soul Crystal (2096) "
                        "at no more than half HP, levels = crystal levels the monster can raise.",
           "crystals": crystals, "npcs": npcs}
    with open(f"{ROOT}/data/soul-crystals.json", "w", encoding="utf-8") as f:
        f.write(json.dumps(out, indent=2) + "\n")

    path = f"{ROOT}/data/items.json"
    raw = open(path, encoding="utf-8").read()
    data = json.loads(raw)
    assert json.dumps(data, indent=2, ensure_ascii=False) + "\n" == raw, "items.json is not in canonical form"
    have = {r["id"] for r in data["items"]}
    ids = sorted({c["itemId"] for c in crystals} | {c["next"] for c in crystals})
    for item_id in ids:
        if item_id in have:
            continue
        lo = item_id // 100 * 100
        xml = show(f"stats/items/{lo:05d}-{lo + 99:05d}.xml")
        m = re.search(rf'<item id="{item_id}" type="EtcItem".*?</item>', xml, re.S)
        sets = dict(re.findall(r'<set name="(\w+)" val="([^"]*)"', m.group(0)))
        name = re.search(r'name="([^"]*)"', m.group(0)).group(1)
        data["items"].append({"id": item_id, "name": name, "kind": "etc", "icon": sets.get("icon", ""),
                              "price": int(sets.get("price", "0")), "weight": int(sets.get("weight", "0"))})
        print("added:", item_id, name)
    data["items"].sort(key=lambda r: r["id"])
    with open(path, "w", encoding="utf-8") as f:
        f.write(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    print(f"{len(crystals)} crystal steps, {len(npcs)} monsters")


if __name__ == "__main__":
    main()
