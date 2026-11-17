#!/usr/bin/env python3
"""Pets (Mobius CT_0 stats/pets/*.xml) -> data/pets.json.

Per pet: npc, collar item and its summon skill (cast time), food items with how much meal each restores (the food
item's skill <feed>), hungry limit %, inventory load, pet skills, and per level the meal meter, exp, the owner's exp
share (get_exp_type), meal use, and the base stats. Values are transcribed as data; behaviour is in ZoneWorldPets.
"""
import json
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = "/workspace/mobius"
BASE = "L2J_Mobius_CT_0_Interlude/dist/game/data/stats"


def git(*args):
    return subprocess.run(["git", "-C", MOBIUS, *args], capture_output=True, text=True, check=True).stdout


def item_block(item_id):
    lo = item_id // 100 * 100
    text = git("show", f"HEAD:{BASE}/items/{lo:05d}-{lo + 99:05d}.xml")
    m = re.search(rf'<item id="{item_id}".*?</item>', text, re.S)
    return m.group(0) if m else ""


def skill_block(skill_id):
    lo = skill_id // 100 * 100
    text = git("show", f"HEAD:{BASE}/skills/{lo:05d}-{lo + 99:05d}.xml")
    m = re.search(rf'<skill id="{skill_id}".*?</skill>', text, re.S)
    return m.group(0) if m else ""


def item_skill(item_id):
    m = re.search(r'<skill id="(\d+)" level="(\d+)"', item_block(item_id))
    return (int(m.group(1)), int(m.group(2))) if m else (0, 0)


def tag(body, name, default=None):
    m = re.search(rf"<{name}>([^<]*)</{name}>", body)
    return m.group(1) if m else default


def add_items(ids):
    """Collars and pet food missing from items.json are added from the Mobius item XML (name, icon, price, weight)."""
    path = ROOT / "data/items.json"
    raw = path.read_text(encoding="utf-8")
    data = json.loads(raw)
    have = {row["id"] for row in data["items"]}
    for item_id in ids:
        if item_id in have:
            continue
        block = item_block(item_id)
        if not block:
            print(item_id, "not in Mobius")
            continue
        sets = dict(re.findall(r'<set name="(\w+)" val="([^"]*)" />', block))
        row = {"id": item_id, "name": re.search(r'name="([^"]*)"', block).group(1), "kind": "etc"}
        if sets.get("is_stackable") == "true":
            row["stackable"] = True
        row["icon"] = sets.get("icon", "")
        row["price"] = int(sets.get("price", 0))
        row["weight"] = int(sets.get("weight", 0))
        data["items"].append(row)
        print("item added:", item_id, row["name"])
    data["items"].sort(key=lambda r: r["id"])
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main():
    pets = []
    for path in git("ls-tree", "--name-only", "HEAD", BASE + "/pets/").split():
        text = git("show", f"HEAD:{path}")
        for m in re.finditer(r'<pet id="(\d+)" itemId="(\d+)" index="(\d+)">(.*?)</pet>', text, re.S):
            npc_id, item_id, body = int(m.group(1)), int(m.group(2)), m.group(4)
            sets = dict(re.findall(r'<set name="(\w+)" val="([^"]*)" />', re.sub(r"<stats>.*</stats>", "", body, flags=re.S)))
            if item_id <= 0:
                continue
            summon_skill, _ = item_skill(item_id)
            cast_ms = int(tag(skill_block(summon_skill), "hitTime", "5000")) if summon_skill else 5000
            food = []
            for fid in [int(x) for x in re.split(r"[;,\s]+", sets.get("food", "")) if x]:
                sid, _ = item_skill(fid)
                feed = int(float(tag(skill_block(sid), "feed", "0"))) if sid else 0
                food.append({"itemId": fid, "feed": feed})
            skills = [{"minLevel": int(a), "skillId": int(b), "skillLevel": int(c)}
                      for a, b, c in re.findall(r'<skill minLevel="(\d+)" skillId="(\d+)" skillLevel="(\d+)"', body)]
            levels = []
            for lm in re.finditer(r'<stat level="(\d+)">(.*?)</stat>', body, re.S):
                d = dict(re.findall(r'<set name="(\w+)" val="([^"]*)" />', lm.group(2)))
                f = lambda k: round(float(d.get(k, 0)), 4)
                levels.append({
                    "level": int(lm.group(1)),
                    "exp": int(d.get("exp", 0)),
                    "ownerExpTaken": int(d.get("get_exp_type", 100)),
                    "maxMeal": int(d.get("max_meal", 0)),
                    "mealBattle": int(d.get("consume_meal_in_battle", 0)),
                    "mealNormal": int(d.get("consume_meal_in_normal", 0)),
                    "hp": f("org_hp"), "mp": f("org_mp"),
                    "pAtk": f("org_pattack"), "pDef": f("org_pdefend"),
                    "mAtk": f("org_mattack"), "mDef": f("org_mdefend"),
                    "hpRegen": f("org_hp_regen"), "mpRegen": f("org_mp_regen"),
                })
            levels.sort(key=lambda r: r["level"])
            pets.append({
                "npcId": npc_id, "itemId": item_id, "index": int(m.group(3)),
                "summonSkillId": summon_skill, "summonCastMs": cast_ms,
                "food": food, "hungryLimit": int(sets.get("hungry_limit", 55)), "load": int(sets.get("load", 54510)),
                "syncLevel": sets.get("sync_level", "false") == "true",
                "skills": skills, "levels": levels,
            })
    pets.sort(key=lambda r: r["npcId"])
    add_items(sorted({p["itemId"] for p in pets} | {f["itemId"] for p in pets for f in p["food"]}))
    doc = {"reference": "Pets transcribed from Mobius CT 0 Interlude stats/pets (+ collar / food item skills).", "pets": pets}
    (ROOT / "data/pets.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
    print(len(pets), "pets")


if __name__ == "__main__":
    main()
