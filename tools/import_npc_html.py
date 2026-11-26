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
          # Mobius DungeonGatekeeper overrides getHtmlPath to data/html/teleporter (its pages are the
          # Necropolis/Catacomb ziggurat pages), so it must not fall back to default/.
          "DungeonGatekeeper": "teleporter",
          # Mobius SepulcherNpc writes to data/html/SepulcherNpc.
          "SepulcherNpc": "SepulcherNpc",
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
def norm(text):
    return re.sub(r"[^0-9a-z]", "", (text or "").lower())
UNMAPPED = []
def rewrite_goto(rel, text):
    m = re.match(r"teleporter/(\d+)", rel)
    lists = gates.get(int(m.group(1))) if m else None
    # A page whose destinations Mobius stores in the legacy teleport.sql table (the Necropolis/Catacomb
    # ziggurats link `necro 262` / `cata 250` / `exit 275`) has no name to match in teleports.json. Those
    # books are dropped later; the bypass itself is a separate gap.
    if not lists or re.search(r"npc_%objectId%_(necro|cata|exit) ", text):
        return text
    def one(g):
        name, label = (g.group(3) or "").strip(), g.group(4)
        # The msg name and the visible label (before the " - <fee>" suffix) both name the destination; the
        # datapack spells them with and without apostrophes, so compare letters and digits only.
        wanted = {norm(name), norm(label.split(" - ")[0])} - {""}
        order = ["NOBLES_TOKEN"] if "Token" in label else ["NORMAL", "NOBLES_ADENA"]
        for want in order:
            for lst in lists:
                if lst["name"] != want:
                    continue
                for i, loc in enumerate(lst["locations"]):
                    if norm(loc["name"]) in wanted:
                        return g.group(0).replace(f"_goto {g.group(1)}", f"_teleport {want} {i}", 1)
        print(f"  WARN {rel}: no teleports.json row for goto {g.group(1)} {name!r}")
        UNMAPPED.append((rel, g.group(1), name, label.strip()))
        return g.group(0)
    return GOTO.sub(one, text)

LINK = re.compile(r"_Link\s+([\w./-]+\.html?)")
# Mobius Link bypass only serves paths whitelisted in the handler. A page whose link is not listed sends an
# empty NpcHtmlMessage, which the client draws as an empty window, so the dead link must not be imported.
VALID_LINKS = {
    "adventurer_guildsman/AboutHighLevelGuildsman.htm", "adventurer_guildsman/AboutHighLevelGuilds.htm",
    "adventurer_guildsman/AboutNewLifeCrystals.htm", "clanHallDoorman/evolve.htm",
    "common/augmentation_01.htm", "common/augmentation_02.htm", "common/crafting_01.htm",
    "common/duals_01.htm", "common/duals_02.htm", "common/duals_03.htm", "common/g_cube_warehouse001.htm",
    "common/weapon_sa_01.htm", "common/welcomeback002.htm", "common/welcomeback003.htm",
    "default/BlessingOfProtection.htm", "default/SupportMagic.htm", "fisherman/fishing_championship.htm",
    "fortress/foreman.htm", "olympiad/hero_main2.htm", "seven_signs/blkmrkt_1.htm", "seven_signs/blkmrkt_2.htm",
    "seven_signs/mammblack_1a.htm", "seven_signs/mammblack_1b.htm", "seven_signs/mammblack_1c.htm",
    "seven_signs/mammblack_2a.htm", "seven_signs/mammblack_2b.htm", "seven_signs/mammmerch_1.htm",
    "seven_signs/mammmerch_1a.htm", "seven_signs/mammmerch_1b.htm", "teleporter/separatedsoul.htm",
    "warehouse/clanwh.htm", "warehouse/privatewh.htm",
}
DEAD_LINKS = []


def drop_dead_links(rel, text):
    def one(m):
        target = m.group(1)
        if target in VALID_LINKS:
            return m.group(0)
        print(f"  DROP {rel}: dead _Link {target} (not in the Mobius Link whitelist)")
        DEAD_LINKS.append((rel, target))
        return ""
    return LINK.sub(one, text)


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
    out.write_text(drop_dead_links(rel, rewrite_goto(rel, text)), encoding="utf-8")
    added.append(rel)
print(f"npcs={len(ids)} files_seen={len(seen)} added={len(added)} unmapped_goto={len(UNMAPPED)} dead_link={len(DEAD_LINKS)}")
# Gap report: legacy goto links with no data/teleports.json row. The zone refuses `goto` with a logged reason
# (there is no legacy id table), so each row here is a destination the gatekeeper page offers but Fineage lacks.
gap = ROOT / "docs/gaps/npc-html-unmapped-goto.tsv"
if UNMAPPED:
    gap.parent.mkdir(parents=True, exist_ok=True)
    rows = {}
    if gap.exists():
        for line in gap.read_text(encoding="utf-8").splitlines()[1:]:
            parts = line.split("\t")
            rows[(parts[0], parts[1])] = line
    for rel, gid, name, label in UNMAPPED:
        rows[(rel, gid)] = "\t".join([rel, gid, name, label])
    gap.write_text("page\tlegacy_goto_id\tmsg_name\tlabel\n" + "\n".join(rows[k] for k in sorted(rows)) + "\n", encoding="utf-8")

# Gap report: `Link` rows the page had but the zone cannot serve (Mobius' whitelist does not list them).
dead = ROOT / "docs/gaps/npc-html-dead-links.tsv"
if DEAD_LINKS:
    dead.parent.mkdir(parents=True, exist_ok=True)
    rows = {}
    if dead.exists():
        for line in dead.read_text(encoding="utf-8").splitlines()[1:]:
            parts = line.split("\t")
            rows[(parts[0], parts[1])] = line
    for rel, target in DEAD_LINKS:
        rows[(rel, target)] = "\t".join([rel, target])
    dead.write_text("page\ttarget\n" + "\n".join(rows[k] for k in sorted(rows)) + "\n", encoding="utf-8")
for r in sorted(added): print(" ", r)
