#!/usr/bin/env python3
"""Author the Possessor of a Precious Soul 1-4 quests (241, 242, 246, 247) as quest-engine data (Block G1).

The step tables below are written for this engine from the Mobius CT0 quest behaviour (talk/event/kill branches,
items, conds, rewards); no Java is copied. Dialog HTML text is taken from the Mobius quest folders and its
`bypass Script <quest> <page>` links are rewritten to `quest_event:<id>:<page>`.

Usage: python3 tools/author_precious_soul.py [/path/to/mobius git checkout]
"""
import json, pathlib, re, subprocess, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "/workspace/mobius")
QDIR = "L2J_Mobius_CT_0_Interlude/dist/game/data/scripts/quests"


def talk(npc, html, when="started", cond=None, sub=True, **gates):
    d = {"npcId": npc, "hook": "talk", "when": when}
    if cond is not None:
        d["cond"] = cond
    if sub:
        d["subClass"] = True
    d.update(gates)
    d["htmlFile"] = html + ".htm"
    return d


def event(npc, page, ops=(), when="started", cond=None, html=None, **gates):
    d = {"npcId": npc, "hook": "event", "when": when, "event": page}
    if cond is not None:
        d["cond"] = cond
    d.update(gates)
    if ops:
        d["ops"] = list(ops)
    d["htmlFile"] = (html or page) + ".htm"
    return d


def kill(npc, cond, item, chance, limit, cond_at_limit=None, party=True, **gates):
    op = {"op": "drop_item", "itemId": item, "count": 1, "chance": chance, "limit": limit}
    if cond_at_limit is not None:
        op["condAtLimit"] = cond_at_limit
    d = {"npcId": npc, "hook": "kill", "when": "started", "cond": cond, "subClass": True, "party": party}
    d.update(gates)
    d["html"] = ""
    d["ops"] = [op]
    return d


def cond(n): return {"op": "set_cond", "cond": n}
def take(i, c=1): return {"op": "take_item", "itemId": i, "count": c}
def give(i, c=1): return {"op": "give_item", "itemId": i, "count": c}
def exp(e): return {"op": "give_exp_sp", "exp": e, "sp": 0}
def setv(k, v): return {"op": "set", "key": k, "value": str(v)}
def unset(k): return {"op": "unset", "key": k}
START = [{"op": "create"}, {"op": "start"}]
DONE = {"op": "complete"}
SOCIAL_VICTORY = {"op": "social", "action": 3}


# --- 241: Talien sends the subclass on the Seventeen / Echo Crystal / Rahorakti errands; Caradine ends it. ---
TALIEN, GABRIELLE, GILMORE, KANTABILON, STEDMIEL = 31739, 30753, 30754, 31042, 30692
VIRGIL, OGMAR, RAHORAKTI, KASSANDRA, CARADINE, NOEL = 31742, 31744, 31336, 31743, 31740, 31272
LEGEND, CLAW, ECHO, POETRY, MOSS, MEDICINE, LUNARGENT, HELLFIRE, VIRGIL_LETTER = 7587, 7597, 7589, 7588, 7598, 7599, 6029, 6033, 7677
q241 = [
    talk(TALIEN, "31739-01", when="absent", minLevel=50),
    talk(TALIEN, "31739-02", when="absent", sub=None),
    event(TALIEN, "31739-03", START, when="absent", subClass=True, minLevel=50),
    *[talk(TALIEN, page, cond=c) for c, page in
      [(1, "31739-04"), (2, "31739-05"), (3, "31739-05"), (4, "31739-06"), (5, "31739-08"),
       (8, "31739-09"), (9, "31739-11"), (10, "31739-12"), (11, "31739-14")]],
    event(TALIEN, "31739-07", [take(LEGEND), cond(5)], cond=4, hasItem=LEGEND),
    event(TALIEN, "31739-10", [take(ECHO), cond(9)], cond=8, hasItem=ECHO),
    event(TALIEN, "31739-13", [take(POETRY), cond(11)], cond=10, hasItem=POETRY),
    talk(GABRIELLE, "30753-01", cond=1), talk(GABRIELLE, "30753-03", cond=2),
    event(GABRIELLE, "30753-02", [cond(2)], cond=1),
    talk(GILMORE, "30754-01", cond=2), talk(GILMORE, "30754-03", cond=3),
    event(GILMORE, "30754-02", [cond(3)], cond=2),
    kill(27113, 3, LEGEND, 1.0, 1, 4),  # Baraham
    talk(KANTABILON, "31042-01", cond=5), talk(KANTABILON, "31042-03", cond=6),
    talk(KANTABILON, "31042-04", cond=7), talk(KANTABILON, "31042-06", cond=8),
    event(KANTABILON, "31042-02", [cond(6)], cond=5),
    event(KANTABILON, "31042-05", [take(CLAW, 10), give(ECHO), cond(8)], cond=7, atLeast={str(CLAW): 10}),
    kill(20244, 6, CLAW, 0.61, 10, 7), kill(20245, 6, CLAW, 0.61, 10, 7),  # Malruk Succubus / Turen
    talk(STEDMIEL, "30692-01", cond=9), talk(STEDMIEL, "30692-03", cond=10),
    event(STEDMIEL, "30692-02", [give(POETRY), cond(10)], cond=9),
    talk(VIRGIL, "31742-01", cond=11), talk(VIRGIL, "31742-03", cond=12),
    talk(VIRGIL, "31742-04", cond=17), talk(VIRGIL, "31742-06", cond=18),
    event(VIRGIL, "31742-02", [cond(12)], cond=11), event(VIRGIL, "31742-05", [cond(18)], cond=17),
    talk(OGMAR, "31744-01", cond=12), talk(OGMAR, "31744-03", cond=13),
    event(OGMAR, "31744-02", [cond(13)], cond=12),
    talk(RAHORAKTI, "31336-01", cond=13), talk(RAHORAKTI, "31336-03", cond=14),
    talk(RAHORAKTI, "31336-04", cond=15), talk(RAHORAKTI, "31336-06", cond=16),
    event(RAHORAKTI, "31336-02", [cond(14)], cond=13),
    event(RAHORAKTI, "31336-05", [take(MOSS, 5), give(MEDICINE), cond(16)], cond=15, atLeast={str(MOSS): 5}),
    *[kill(n, 14, MOSS, 0.31, 5, 15) for n in (21508, 21509, 21510, 21511, 21512)],  # Splinter Stakato
    talk(KASSANDRA, "31743-01", cond=16), talk(KASSANDRA, "31743-03", cond=17),
    event(KASSANDRA, "31743-02", [take(MEDICINE), cond(17)], cond=16, hasItem=MEDICINE),
    talk(CARADINE, "31740-01", cond=18), talk(CARADINE, "31740-03", cond=19), talk(CARADINE, "31740-04", cond=21),
    event(CARADINE, "31740-02", [cond(19)], cond=18),
    event(CARADINE, "31740-05", [give(VIRGIL_LETTER), exp(263043), DONE], cond=21),
    talk(NOEL, "31272-01", cond=19),
    talk(NOEL, "31272-04", cond=20, hasItem=HELLFIRE, atLeast={str(LUNARGENT): 5}),
    talk(NOEL, "31272-03", cond=20), talk(NOEL, "31272-06", cond=21),
    event(NOEL, "31272-02", [cond(20)], cond=19),
    event(NOEL, "31272-05", [take(LUNARGENT, 5), take(HELLFIRE), cond(21)], cond=20,
          hasItem=HELLFIRE, atLeast={str(LUNARGENT): 5}),
    event(NOEL, "31272-05", cond=20, html="31272-07"),
]

# --- 242: Virgil's letter starts it; angel corpses, Kalis / Matild, the cornerstones and the unicorn. ---
KNIGHT, CORPSE, KALIS, MATILD, CORNERSTONE, FALLEN, PURE = 31751, 31752, 30759, 30738, 31748, 31746, 31747
HAIR, INGREDIENT, ORB, CARADINE_LETTER = 7590, 7596, 7595, 7678
q242 = [
    talk(VIRGIL, "31742-03", when="absent", minLevel=60, hasItem=VIRGIL_LETTER,
         ops=[*START, take(VIRGIL_LETTER)]),
    talk(VIRGIL, "31742-02", when="absent", sub=None, hasItem=VIRGIL_LETTER),
    talk(VIRGIL, "31742-04", cond=1), talk(VIRGIL, "31742-05", cond=2),
    talk(KASSANDRA, "31743-01", cond=1), talk(KASSANDRA, "31743-06", cond=2),
    talk(KASSANDRA, "31743-07", cond=11, ops=[give(CARADINE_LETTER), exp(455764), SOCIAL_VICTORY, DONE]),
    *[event(KASSANDRA, p, cond=1) for p in ("31743-02", "31743-03", "31743-04")],
    event(KASSANDRA, "31743-05", [cond(2)], cond=1),
    talk(OGMAR, "31744-01", cond=2), talk(OGMAR, "31744-03", cond=3),
    event(OGMAR, "31744-02", [cond(3)], cond=2),
    talk(KNIGHT, "31751-01", cond=3), talk(KNIGHT, "31751-03", cond=4),
    talk(KNIGHT, "31751-04", cond=5, hasItem=HAIR, ops=[cond(6)]),
    talk(KNIGHT, "31751-03", cond=5, ops=[cond(4)]),
    talk(KNIGHT, "31751-05", cond=6),
    event(KNIGHT, "31751-02", [cond(4), setv("angel", 0)], cond=3),
    # Four angel corpses give the golden hair (counter in the "angel" variable).
    *[talk(CORPSE, "31752-01", cond=4, whenVar="angel", whenValue=str(n), ops=[setv("angel", n + 1)]) for n in (0, 1, 2)],
    talk(CORPSE, "31752-02", cond=4, whenVar="angel", whenValue="3", ops=[unset("angel"), give(HAIR), cond(5)]),
    talk(CORPSE, "31752-01", cond=5),
    talk(KALIS, "30759-01", cond=6), talk(KALIS, "30759-03", cond=7),
    talk(KALIS, "30759-04", cond=8, hasItem=INGREDIENT),
    talk(KALIS, "30759-03", cond=8, ops=[cond(7)]),
    talk(KALIS, "30759-06", cond=9),
    event(KALIS, "30759-02", [cond(7)], cond=6),
    event(KALIS, "30759-05", [setv("orb", 0), setv("cornerstone", 0), take(HAIR), take(INGREDIENT), cond(9)],
          cond=8, hasItem=INGREDIENT),
    event(KALIS, "30759-05", [cond(7)], cond=8, html="30759-02"),
    talk(MATILD, "30738-01", cond=7), talk(MATILD, "30738-03", cond=8),
    event(MATILD, "30738-02", [give(INGREDIENT), cond(8)], cond=7),
    # Restrainer of Glory: at most four orbs per quest (counter in "orb").
    *[{"npcId": 27317, "hook": "kill", "when": "started", "cond": 9, "subClass": True, "whenVar": "orb",
       "whenValue": str(n), "html": "", "ops": [setv("orb", n + 1), give(ORB)]} for n in (0, 1, 2, 3)],
    talk(CORNERSTONE, "31748-02", cond=9, hasItem=ORB), talk(CORNERSTONE, "31748-01", cond=9),
    *[event(CORNERSTONE, "31748-03", [take(ORB), setv("cornerstone", n + 1)], cond=9, hasItem=ORB,
            whenVar="cornerstone", whenValue=str(n)) for n in (0, 1, 2)],
    event(CORNERSTONE, "31748-03", [take(ORB), unset("orb"), unset("cornerstone"), cond(10)], cond=9, hasItem=ORB,
          whenVar="cornerstone", whenValue="3"),
    talk(FALLEN, "31746-01", cond=9),
    # Mobius swaps the Fallen Unicorn for a Pure Unicorn; here the fallen one finishes the step directly.
    talk(FALLEN, "31746-02", cond=10, ops=[cond(11)]),
    talk(PURE, "31747-01", cond=10, ops=[cond(11)]), talk(PURE, "31747-02", cond=11),
]

# --- 246: Caradine's second letter; Ossian's relics and Barakiel's Rain Song; Ladd gives the third letter. ---
OSSIAN, LADD = 31741, 30721
WATERBINDER, EVERGREEN, RAIN_SONG, RELIC_BOX, CARADINE_LETTER_3 = 7591, 7592, 7593, 7594, 7679
q246 = [
    talk(CARADINE, "31740-01", when="absent", minLevel=65, hasItem=CARADINE_LETTER),
    talk(CARADINE, "31740-02", when="absent", sub=None, hasItem=CARADINE_LETTER),
    event(CARADINE, "31740-04", [*START, take(CARADINE_LETTER)], when="absent", subClass=True, minLevel=65,
          hasItem=CARADINE_LETTER),
    talk(CARADINE, "31740-05", cond=1),
    talk(OSSIAN, "31741-01", cond=1), talk(OSSIAN, "31741-03", cond=2),
    talk(OSSIAN, "31741-04", cond=3, hasItems=[WATERBINDER, EVERGREEN]),
    talk(OSSIAN, "31741-06", cond=4), talk(OSSIAN, "31741-07", cond=5, hasItem=RAIN_SONG),
    talk(OSSIAN, "31741-09", cond=6),
    event(OSSIAN, "31741-02", [cond(2)], cond=1),
    event(OSSIAN, "31741-05", [take(WATERBINDER), take(EVERGREEN), cond(4)], cond=3, hasItems=[WATERBINDER, EVERGREEN]),
    event(OSSIAN, "31741-08", [take(RAIN_SONG), give(RELIC_BOX), cond(6)], cond=5, hasItem=RAIN_SONG),
    talk(LADD, "30721-01", cond=6, hasItem=RELIC_BOX),
    event(LADD, "30721-02", [take(RELIC_BOX), give(CARADINE_LETTER_3), exp(719843), DONE], cond=6, hasItem=RELIC_BOX),
    # Pilgrim of Splendor → Waterbinder, Judge of Splendor → Evergreen (20%); both held → cond 3. Killer only.
    kill(21541, 2, WATERBINDER, 0.2, 1, 3, party=False, hasItem=EVERGREEN, lacksItems=[WATERBINDER]),
    kill(21541, 2, WATERBINDER, 0.2, 1, party=False, lacksItems=[WATERBINDER]),
    kill(21544, 2, EVERGREEN, 0.2, 1, 3, party=False, hasItem=WATERBINDER, lacksItems=[EVERGREEN]),
    kill(21544, 2, EVERGREEN, 0.2, 1, party=False, lacksItems=[EVERGREEN]),
    # Barakiel (raid): Rain Song for a party member at cond 4 (Mobius: every member in range; here one).
    {"npcId": 25325, "hook": "kill", "when": "started", "cond": 4, "party": True, "lacksItems": [RAIN_SONG],
     "html": "", "ops": [{"op": "drop_item", "itemId": RAIN_SONG, "count": 1, "chance": 1.0, "limit": 1, "condAtLimit": 5}]},
]

# --- 247: Caradine's third letter; the Lady of the Lake grants noblesse. ---
LADY, TIARA = 31745, 7694
q247 = [
    talk(CARADINE, "31740-01", when="absent", minLevel=75, hasItem=CARADINE_LETTER_3),
    talk(CARADINE, "31740-02", when="absent", sub=None, hasItem=CARADINE_LETTER_3),
    event(CARADINE, "31740-03", [*START, take(CARADINE_LETTER_3)], when="absent", subClass=True, minLevel=75,
          hasItem=CARADINE_LETTER_3),
    talk(CARADINE, "31740-04", cond=1), talk(CARADINE, "31740-06", cond=2),
    {"npcId": CARADINE, "hook": "event", "when": "started", "event": "TELEPORT", "html": "",
     "ops": [cond(2), {"op": "teleport", "x": 143209, "y": 43968, "z": -3038}]},
    talk(LADY, "31745-01", cond=2, minLevel=75), talk(LADY, "31745-06", cond=2),
    *[event(LADY, p, cond=2) for p in ("31745-02", "31745-03", "31745-04")],
    event(LADY, "31745-05", [{"op": "set_noble"}, give(TIARA), exp(93836), SOCIAL_VICTORY, DONE], cond=2,
          subClass=True, minLevel=75),
]

QUESTS = [
    (241, "00241_possessorofaprecioussoul1", "Q00241_PossessorOfAPreciousSoul1", "Possessor of a Precious Soul - 1",
     [LEGEND, CLAW, ECHO, POETRY, MOSS, MEDICINE], q241),
    (242, "00242_possessorofaprecioussoul2", "Q00242_PossessorOfAPreciousSoul2", "Possessor of a Precious Soul - 2",
     [HAIR, INGREDIENT, ORB], q242),
    (246, "00246_possessorofaprecioussoul3", "Q00246_PossessorOfAPreciousSoul3", "Possessor of a Precious Soul - 3",
     [WATERBINDER, EVERGREEN, RAIN_SONG, RELIC_BOX], q246),
    (247, "00247_possessorofaprecioussoul4", "Q00247_PossessorOfAPreciousSoul4", "Possessor of a Precious Soul - 4",
     [], q247),
]


def git_show(path):
    return subprocess.run(["git", "-C", str(MOBIUS), "show", f"HEAD:{path}"], check=True,
                          capture_output=True).stdout.decode("utf-8")


def main():
    for qid, folder, script, name, items, dialogs in QUESTS:
        out = ROOT / "data/quests" / folder
        for old in out.glob("*.htm"):
            old.unlink()
        pages = {d["htmlFile"] for d in dialogs if "htmlFile" in d}
        link = re.compile(r'bypass (?:-h )?Script ' + re.escape(script) + r' ([^"]+?)(?:\.htm)?"')
        for page in sorted(pages):
            text = git_show(f"{QDIR}/{script}/{page}")
            text = link.sub(lambda m: f'quest_event:{qid}:{m.group(1)}"', text)
            (out / page).write_text(text, encoding="utf-8")
        quest = {"id": qid, "name": name, "repeatable": False, "abortable": True, "questItems": items,
                 "dialogs": dialogs}
        (out / "quest.json").write_text(json.dumps(quest, indent=2) + "\n", encoding="utf-8")
        print(f"{folder}: {len(dialogs)} dialogs, {len(pages)} pages")


if __name__ == "__main__":
    main()
