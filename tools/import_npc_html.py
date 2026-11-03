#!/usr/bin/env python3
"""Copy Mobius CT0 Interlude NPC dialog HTML for the NPCs of one or more spawn files into data/html.
Same scope rule as the Talking Island import (data/html/SOURCE.txt): only NPCs actually spawned in the
given area, their {id}.htm / {id}-{page}.htm in the folder NpcHtmlPaths.Folder picks for the NPC type,
and the `Link <path>` targets those pages reference (followed transitively). Existing files are kept.
Usage: import_npc_html.py data/spawns/elven_village.json [...] [--mobius ref/mobius-ct0-interlude/data/html]"""
import json, re, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
args = sys.argv[1:]
mob = ROOT / "ref/mobius-ct0-interlude/data/html"
if "--mobius" in args:
    i = args.index("--mobius"); mob = pathlib.Path(args[i + 1]); del args[i:i + 2]
dst = ROOT / "data/html"
FOLDER = {"Merchant": "merchant", "Fisherman": "fisherman", "Warehouse": "warehouse", "Teleporter": "teleporter",
          "Guard": "guard", "Trainer": "trainer", "Adventurer": "adventurer_guildsman", "PetManager": "petmanager",
          "Doorman": "doorman", "CastleDoorman": "doorman", "ClanHallDoorman": "doorman",
          "SignsPriest": "seven_signs", "DawnPriest": "seven_signs", "DuskPriest": "seven_signs"}
def folder(t):
    if t and t.startswith("VillageMaster"): return "villagemaster"
    return FOLDER.get(t or "", "default")

npcs = {n["npcId"]: n for n in json.loads((ROOT / "data/npcs.json").read_text())["npcs"]}
ids = set()
for a in args:
    ids |= {r["npcId"] for r in json.loads(pathlib.Path(a).read_text())["npcs"]}

queue = []
for i in sorted(ids):
    n = npcs.get(i)
    if not n or n.get("attackable"):
        continue
    for f in (folder(n.get("type")), "default"):
        d = mob / f
        hits = sorted(d.glob(f"{i}.htm")) + sorted(d.glob(f"{i}-*.htm")) + sorted(d.glob(f"{i}-*.html"))
        if hits:
            queue += [p.relative_to(mob).as_posix() for p in hits]
            break

# Mobius CT0 gatekeeper pages still link legacy teleport.sql ids (`goto 1014`); data/teleports.json is
# list + 0-based index. Rewrite each goto by the location name in msg="811;<name>" (Token label -> NOBLES_TOKEN).
gates = {g["npcId"]: g["lists"] for g in json.loads((ROOT / "data/teleports.json").read_text())["gatekeepers"]}
GOTO = re.compile(r'npc_%objectId%_goto (\d+)"(\s+msg="811;([^"]+)")?>([^<]*)')
def rewrite_goto(rel, text):
    m = re.match(r"teleporter/(\d+)", rel)
    lists = gates.get(int(m.group(1))) if m else None
    if not lists:
        return text
    def one(g):
        name, label = (g.group(3) or "").strip(), g.group(4)
        order = ["NOBLES_TOKEN"] if "Token" in label else ["NORMAL", "NOBLES_ADENA"]
        for want in order:
            for lst in lists:
                if lst["name"] != want:
                    continue
                for i, loc in enumerate(lst["locations"]):
                    if loc["name"] == name:
                        return g.group(0).replace(f"_goto {g.group(1)}", f"_teleport {want} {i}", 1)
        print(f"  WARN {rel}: no teleports.json row for goto {g.group(1)} {name!r}")
        return g.group(0)
    return GOTO.sub(one, text)

LINK = re.compile(r"_Link\s+([\w./-]+\.html?)")
seen, added = set(), []
while queue:
    rel = queue.pop()
    if rel in seen or ".." in rel:
        continue
    seen.add(rel)
    src = mob / rel
    if not src.is_file():
        continue
    text = src.read_text(encoding="utf-8", errors="replace")
    queue += LINK.findall(text)
    out = dst / rel
    if out.exists():
        continue
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(rewrite_goto(rel, text), encoding="utf-8")
    added.append(rel)
print(f"npcs={len(ids)} files_seen={len(seen)} added={len(added)}")
for r in sorted(added): print(" ", r)
