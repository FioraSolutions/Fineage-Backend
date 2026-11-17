#!/usr/bin/env python3
"""Sanity check for hand-written quest packs: unknown items / NPCs, and NPCs that never spawn in the world
(neither in data/spawns nor raised by a spawn_npc op of the pack). Usage: python3 tools/quest_check.py 211 217 ..."""
import glob, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
items = {r["id"] for r in json.load(open(f"{ROOT}/data/items.json"))["items"]}
npcs_raw = json.load(open(f"{ROOT}/data/npcs.json"))
npcs = {r["npcId"] for r in (npcs_raw["npcs"] if isinstance(npcs_raw, dict) else npcs_raw)}
spawned = set()
for f in glob.glob(f"{ROOT}/data/spawns/*.json"):
    spawned |= {int(x) for x in re.findall(r'"npcId":\s*(\d+)', open(f).read())}
for arg in sys.argv[1:]:
    folder = glob.glob(f"{ROOT}/data/quests/{int(arg):05d}_*")[0]
    q = json.load(open(f"{folder}/quest.json"))
    text = json.dumps(q)
    used_items = {int(x) for x in re.findall(r'"(?:itemId|hasItem)": (\d+)', text)}
    for key in ("items", "hasItems", "lacksItems", "hasAny", "questItems", "weapons"):
        for m in re.findall(rf'"{key}": \[([\d, ]*)\]', text):
            used_items |= {int(x) for x in m.split(",") if x.strip()}
    for m in re.findall(r'"atLeast": \{([^}]*)\}', text):
        used_items |= {int(x) for x in re.findall(r'"(\d+)"', m)}
    raised = {o["npcId"] for d in q["dialogs"] for o in d.get("ops", []) if o["op"] == "spawn_npc"}
    used_npcs = {d["npcId"] for d in q["dialogs"]}
    print(arg, "unknown items:", sorted(used_items - items), "unknown npcs:", sorted(used_npcs - npcs),
          "never spawned:", sorted(used_npcs - spawned - raised))
