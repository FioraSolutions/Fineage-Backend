#!/usr/bin/env python3
"""Append Mobius CT_0_Interlude EtcItem templates referenced by data/drops/*.json or data/quests/*/quest.json (questItems + give/take ops) or data/village-masters.json (proof + reward items) but missing from data/items.json.
Maps name/icon/is_stackable/price/weight into our etc item shape. Existing rows are never touched.
Usage: gen_drop_items.py [mobius items xml dir] (default ref/mobius-ct0-interlude/data/stats/items)"""
import json, sys, pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
src = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "ref/mobius-ct0-interlude/dist/game/data/stats/items"
if not src.is_dir():
    src = ROOT / "ref/mobius-ct0-interlude/data/stats/items"
path = ROOT / "data/items.json"
data = json.loads(path.read_text())
have = {i["id"] for i in data["items"]}

want = set()
for p in (ROOT / "data/drops").glob("*.json"):
    d = json.loads(p.read_text())
    rows = [i for g in d["death"]["groups"] for i in g["items"]] + d.get("corpse", {}).get("items", [])
    want |= {r["itemId"] for r in rows if r["itemId"] not in have}

sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402
for quest in questio.load_all_quests():
    ids = list(quest.get("questItems", []))
    ids += [op[k] for d in quest.get("dialogs", []) for op in d.get("ops", []) for k in ("itemId", "rewardItemId") if op.get(k)]
    want |= {i for i in ids if i not in have}

trees = ROOT / "data/skill-trees.json"
if trees.exists():
    for tree in json.loads(trees.read_text()).get("trees", []):
        for row in tree.get("skills", []):
            want |= {i["itemId"] for i in row.get("requiredItems", []) if i["itemId"] not in have}

vm = ROOT / "data/village-masters.json"
if vm.exists():
    for c in json.loads(vm.read_text()).get("changes", []):
        ids = [c.get("rewardItemId", 0)]
        for o in c.get("options", []):
            ids += o.get("proofItemIds", [])
        want |= {i for i in ids if i and i not in have}

# Mobius has no icon for for_npc herbs 8600-8607 and BranchSys2.* (i01) for 8608/8611; the client ships these
# textures (Fineage l2-unity SysTextures/Icon), so pin icons the client can render.
ICONS = {8600: "icon.etc_herb_red_i00", 8601: "icon.etc_herb_red_i00", 8602: "icon.etc_herb_red_i00",
         8603: "icon.etc_herb_blue_i00", 8604: "icon.etc_herb_blue_i00", 8605: "icon.etc_herb_blue_i00",
         8606: "icon.etc_herb_green_i00", 8607: "icon.etc_herb_green_i00",
         8608: "icon.etc_attack_sp_herb_i00", 8611: "icon.etc_move_herb_i00"}

added, skipped = [], []
for f in sorted(src.glob("*.xml")):
    for e in ET.parse(f).getroot().iter("item"):
        iid = int(e.get("id"))
        if iid not in want:
            continue
        want.discard(iid)
        if e.get("type") != "EtcItem":
            if "convert" not in globals():
                argv = sys.argv[:]
                sys.argv = ["gen_gmshop_gear_items.py"]
                sys.path.insert(0, str(ROOT / "scripts"))
                from gen_gmshop_gear_items import convert as _convert
                sys.argv = argv
                globals()["convert"] = _convert
            row = convert(e)
            if row is None:
                skipped.append((iid, e.get("type")))
                continue
            added.append(row)
            continue
        s = {x.get("name"): x.get("val") for x in e.findall("set")}
        row = {"id": iid, "name": e.get("name"), "kind": "etc"}
        if s.get("is_stackable") == "true":
            row["stackable"] = True
        icon = ICONS.get(iid) or s.get("icon")
        if not icon:
            skipped.append((iid, "no icon"))
            continue
        row["icon"] = icon
        row["price"] = int(s.get("price", 0))
        row["weight"] = int(s.get("weight", 0))
        added.append(row)

data["items"].extend(added)
data["items"].sort(key=lambda i: i["id"])
path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
print(f"added={len(added)} skipped(non-etc)={skipped} not-in-mobius={sorted(want)}")
for r in added:
    print(r["id"], r["name"])
