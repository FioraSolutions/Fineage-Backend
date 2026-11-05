#!/usr/bin/env python3
"""Q406 Path of the Elven Knight, Q407 Path of the Elven Scout, Q409 Path of the Elven Oracle -> data/quests.json
(replaces those ids in place). Same pattern as gen_q408.py: Mobius CT0 Interlude onAdvEvent/onTalk/onKill branches
transcribed in order as dialog gates (classes, minLevel/maxLevel, hasItem+minCount, hasItems, lacksItems, atLeast,
whenVar) and the Mobius HTML with links rewritten to quest_event:<id>:<event> ('.htm(l)' dropped).
Run scripts/gen_drop_items.py and scripts/gen_quest_item_flags.py afterwards."""
import json, pathlib, re

ROOT = pathlib.Path(__file__).resolve().parent.parent
QDIR = ROOT / "ref/mobius-ct0-interlude/data/scripts/quests"


class Gen:
    def __init__(self, qid, script):
        self.qid, self.script, self.src, self.rows = qid, script, QDIR / script, []

    def html(self, name):
        for ext in ("", ".htm", ".html"):
            p = self.src / (name + ext)
            if p.exists():
                text = p.read_text(encoding="utf-8").strip()
                return re.sub(rf'bypass -h Quest {self.script} ([A-Za-z0-9_\-]+)(?:\.html?)?',
                              lambda m: f"quest_event:{self.qid}:{m.group(1)}", text)
        raise SystemExit(f"{self.script}: missing html {name}")

    def d(self, npc, hook, page, when="started", ops=None, event=None, raw=None, **gates):
        row = {"npcId": npc, "hook": hook, "when": when}
        if event:
            row["event"] = event
        if "atLeast" in gates:
            gates["atLeast"] = {str(k): v for k, v in gates["atLeast"].items()}
        row.update(gates)
        row["html"] = raw if raw is not None else (self.html(page) if page else "")
        if ops:
            row["ops"] = ops
        self.rows.append(row)

    def accept(self, npc, base, job, brooch, pages):
        """Mobius ACCEPT: class != base ? (job ? 02a : 02) : level < 18 ? 03 : proof ? 04 : start (pages['start'], ops)."""
        start_page, start_ops = pages
        self.d(npc, "event", start_page, when="absent", event="ACCEPT", classes=[base], minLevel=18, lacksItems=[brooch], ops=start_ops)
        p = f"{npc}-"
        self.d(npc, "event", p + "04.htm", when="any", event="ACCEPT", classes=[base], minLevel=18)
        self.d(npc, "event", p + "03.htm", when="any", event="ACCEPT", classes=[base])
        self.d(npc, "event", p + "02a.htm", when="any", event="ACCEPT", classes=[job])
        self.d(npc, "event", p + "02.htm", when="any", event="ACCEPT")

    def reward(self, npc, page, before_ops, proof, exps, **gates):
        for (lo, hi), (exp, sp) in zip(((20, 0), (19, 19), (0, 18)), exps):
            g = dict(gates)
            if lo:
                g["minLevel"] = lo
            if hi:
                g["maxLevel"] = hi
            self.d(npc, "talk", page, ops=before_ops + [{"op": "give_adena", "count": 163800}, give(proof),
                                                     {"op": "give_exp_sp", "exp": exp, "sp": sp}, {"op": "complete"}], **g)

    def quest(self, name, items):
        return {"id": self.qid, "name": name, "repeatable": False, "abortable": True, "questItems": items, "dialogs": self.rows}


give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
setv = lambda k, v: {"op": "set", "key": k, "value": str(v)}
START = [{"op": "create"}, {"op": "start"}]
ELVEN_FIGHTER, ELVEN_KNIGHT, ELVEN_SCOUT, ELVEN_MAGE, ORACLE = 18, 19, 22, 25, 29


def q406():
    g = Gen(406, "Q00406_PathOfTheElvenKnight")
    KLUTO, SORIUS = 30317, 30327
    LETTER, BOX, TOPAZ, EMERALD, MEMO, BROOCH = 1202, 1203, 1205, 1206, 1276, 1204
    g.d(SORIUS, "talk", "30327-01.htm", when="absent")
    g.d(SORIUS, "talk", "30327-01.htm", when="completed")
    g.accept(SORIUS, ELVEN_FIGHTER, ELVEN_KNIGHT, BROOCH, ("30327-05.htm", None))
    g.d(SORIUS, "event", "30327-06.htm", when="absent", event="30327-06", classes=[ELVEN_FIGHTER], minLevel=18, lacksItems=[BROOCH], ops=START)
    g.reward(SORIUS, "30327-10.html", [], BROOCH, ((320534, 23152), (456128, 29850), (591724, 33328)), hasItem=BOX)
    g.d(SORIUS, "talk", "30327-07.html", lacksItems=[TOPAZ])
    g.d(SORIUS, "talk", "30327-09.html", atLeast={TOPAZ: 20}, lacksItems=[MEMO, LETTER], ops=[give(LETTER), cond(3)])
    g.d(SORIUS, "talk", "30327-11.html", atLeast={TOPAZ: 20})
    g.d(SORIUS, "talk", "30327-08.html", hasItem=TOPAZ)
    g.d(KLUTO, "talk", "30317-06.html", hasItem=BOX)
    g.d(KLUTO, "talk", "30317-01.html", hasItem=LETTER, atLeast={TOPAZ: 20})
    g.d(KLUTO, "talk", "30317-05.html", hasItem=MEMO, atLeast={TOPAZ: 20, EMERALD: 20},
        ops=[give(BOX), take(TOPAZ, 20), take(EMERALD, 20), take(MEMO), cond(6)])
    g.d(KLUTO, "talk", "30317-04.html", hasItems=[MEMO, EMERALD], atLeast={TOPAZ: 20})
    g.d(KLUTO, "talk", "30317-03.html", hasItem=MEMO, lacksItems=[EMERALD], atLeast={TOPAZ: 20})
    g.d(KLUTO, "event", "30317-02.html", event="30317-02", hasItem=LETTER, lacksItems=[MEMO], ops=[take(LETTER), give(MEMO), cond(4)])
    g.d(KLUTO, "event", "30317-02.html", event="30317-02", hasItem=LETTER, ops=[take(LETTER), cond(4)])
    for mob in (20035, 20042, 20045, 20051, 20054, 20060):  # skeletons / Ruin Spartoi / Salamander Noble
        g.d(mob, "kill", None, lacksItems=[BOX], ops=[{"op": "drop_item", "itemId": TOPAZ, "count": 1, "chance": 0.7, "limit": 20, "condAtLimit": 2}])
    g.d(20782, "kill", None, hasItem=MEMO, ops=[{"op": "drop_item", "itemId": EMERALD, "count": 1, "chance": 0.5, "limit": 20, "condAtLimit": 5}])
    return g.quest("Path of the Elven Knight", [LETTER, BOX, TOPAZ, EMERALD, MEMO])


def q407():
    g = Gen(407, "Q00407_PathOfTheElvenScout")
    REORIA, BABENCO, MORETTI, PRIAS = 30328, 30334, 30337, 30426
    LETTER, T1, T2, T3, T4, HERB, MLETTER, PLETTER, GUARD, REC, KEY = 1207, 1208, 1209, 1210, 1211, 1212, 1214, 1215, 1216, 1217, 1293
    TORN = [T1, T2, T3, T4]
    v1 = dict(whenVar="variable", whenValue="1")
    g.d(REORIA, "talk", "30328-01.htm", when="absent")
    g.d(REORIA, "talk", "30328-01.htm", when="completed")
    g.accept(REORIA, ELVEN_FIGHTER, ELVEN_SCOUT, REC, ("30328-05.htm", START + [give(LETTER)]))
    g.d(REORIA, "talk", "30328-06.html", hasItem=LETTER)
    g.reward(REORIA, "30328-07.html", [take(GUARD)], REC, ((320534, 19932), (456128, 26630), (591724, 33328)), hasItem=GUARD)
    g.d(REORIA, "talk", "30328-08.html", **v1)
    g.d(BABENCO, "talk", "30334-01.html", **v1)
    g.d(MORETTI, "talk", "30337-01.html", hasItem=LETTER, lacksItems=TORN)
    g.d(MORETTI, "talk", "30337-04.html", lacksItems=[MLETTER, PLETTER, GUARD] + TORN, **v1)
    g.d(MORETTI, "talk", "30337-06.html", hasItems=TORN, lacksItems=[MLETTER, PLETTER, GUARD],
        ops=[take(t) for t in TORN] + [give(HERB), give(MLETTER), cond(4)], **v1)
    g.d(MORETTI, "talk", "30337-05.html", lacksItems=[MLETTER, PLETTER, GUARD], **v1)
    g.d(MORETTI, "talk", "30337-07.html", hasItem=PLETTER, ops=[take(PLETTER), give(GUARD), cond(8)])
    g.d(MORETTI, "talk", "30337-09.html", hasItems=[HERB, MLETTER])
    g.d(MORETTI, "talk", "30337-08.html", hasItem=GUARD)
    g.d(MORETTI, "event", "30337-02.html", event="30337-02")
    g.d(MORETTI, "event", "30337-03.html", event="30337-03", hasItem=LETTER, ops=[take(LETTER), setv("variable", 1), cond(2)])
    g.d(PRIAS, "talk", "30426-02.html", hasItems=[MLETTER, HERB, KEY], ops=[take(KEY), take(HERB), take(MLETTER), give(PLETTER), cond(7)])
    g.d(PRIAS, "talk", "30426-01.html", hasItems=[MLETTER, HERB], ops=[cond(5)])
    g.d(PRIAS, "talk", "30426-04.html", hasItem=PLETTER)
    # Ol Mahum Patrol 20053 (cond 2): next missing torn letter; the 4th sets cond 3.
    for i, t in enumerate(TORN):
        ops = [give(t)] + ([cond(3)] if i == 3 else [])
        g.d(20053, "kill", None, cond=2, hasItems=TORN[:i] or None, lacksItems=[t], ops=ops)
    g.rows = [{k: v for k, v in r.items() if v is not None} for r in g.rows]
    # Ol Mahum Sentry 27031 (cond 5): 60% Rusted Key once, cond 6.
    g.d(27031, "kill", None, cond=5, hasItems=[HERB, MLETTER], lacksItems=[KEY],
        ops=[{"op": "drop_item", "itemId": KEY, "count": 1, "chance": 0.6, "limit": 1, "condAtLimit": 6}])
    return g.quest("Path of the Elven Scout", [LETTER, T1, T2, T3, T4, HERB, MLETTER, PLETTER, GUARD, KEY])


def q409():
    g = Gen(409, "Q00409_PathOfTheElvenOracle")
    MANUEL, ALLANA, PERRIN = 30293, 30424, 30428
    MEDAL, MONEY, DIARY, ORDER, LEAF, HALF, NECK = 1231, 1232, 1233, 1234, 1235, 1236, 1275
    m = lambda v: dict(whenVar="memo", whenValue=str(v))
    no_spawn = lambda who: f"<html><body>{who}:<br>(The quest monsters for this step are not spawned on this server yet.)</body></html>"
    g.d(MANUEL, "talk", "30293-01.htm", when="absent", lacksItems=[LEAF])
    g.d(MANUEL, "talk", "30293-04.htm", when="absent")
    g.d(MANUEL, "talk", "30293-01.htm", when="completed", lacksItems=[LEAF])
    g.d(MANUEL, "talk", "30293-04.htm", when="completed")
    g.accept(MANUEL, ELVEN_MAGE, ORACLE, LEAF, ("30293-05.htm", START + [setv("memo", 1), give(MEDAL)]))
    four = [MONEY, DIARY, ORDER, HALF]
    g.d(MANUEL, "talk", "30293-09.html", hasItem=MEDAL, lacksItems=four, ops=[setv("memo", 1), cond(8)], **m(2))
    g.d(MANUEL, "talk", "30293-06.html", hasItem=MEDAL, lacksItems=four, ops=[setv("memo", 1)])
    g.reward(MANUEL, "30293-08.html", [], LEAF, ((320534, 20392), (456128, 27090), (591724, 33788)),
             hasItem=MEDAL, hasItems=[MONEY, DIARY, ORDER], lacksItems=[HALF])
    g.d(MANUEL, "talk", "30293-07.html", hasItem=MEDAL)
    g.d(ALLANA, "talk", "30424-05.html", hasItem=MEDAL, lacksItems=four, **m(2))
    g.d(ALLANA, "talk", "30424-01.html", hasItem=MEDAL, lacksItems=four, ops=[cond(2)], **m(1))
    g.d(ALLANA, "talk", "30424-02.html", hasItems=[MEDAL, ORDER], lacksItems=[MONEY, DIARY, HALF], ops=[setv("memo", 2), give(HALF), cond(4)])
    g.d(ALLANA, "talk", "30424-06.html", hasItems=[MEDAL, ORDER, HALF], lacksItems=[MONEY, DIARY, NECK], ops=[setv("memo", 2), cond(4)], **m(3))
    g.d(ALLANA, "talk", "30424-03.html", hasItems=[MEDAL, ORDER, HALF], lacksItems=[MONEY, DIARY])
    g.d(ALLANA, "talk", "30424-04.html", hasItems=[MEDAL, MONEY, ORDER, HALF], lacksItems=[DIARY], ops=[give(DIARY), take(HALF), cond(9)])
    g.d(ALLANA, "talk", "30424-05.html", hasItems=[MEDAL, MONEY, ORDER, DIARY], ops=[cond(7)])
    for ev in ("30424-08", "30424-09"):
        g.d(ALLANA, "event", ev + ".html", event=ev)
    g.d(ALLANA, "event", "30424-07.html", event="30424-07", **m(1))
    # Mobius replay_1 spawns Lizardman Warrior/Scout/Soldier 27032-27034 at Allana; no quest-spawn op yet.
    g.d(ALLANA, "event", None, event="replay_1", raw=no_spawn("Allana"), ops=[setv("memo", 2)])
    g.d(PERRIN, "talk", "30428-04.html", hasItems=[MEDAL, ORDER, HALF, NECK], ops=[give(MONEY), take(NECK), cond(6)])
    g.d(PERRIN, "talk", "30428-05.html", hasItems=[MEDAL, ORDER, HALF, MONEY])
    g.d(PERRIN, "talk", "30428-06.html", hasItems=[MEDAL, ORDER, HALF], **m(3))
    g.d(PERRIN, "talk", "30428-01.html", hasItems=[MEDAL, ORDER, HALF])
    for ev in ("30428-02", "30428-03"):
        g.d(PERRIN, "event", ev + ".html", event=ev, **m(2))
    # Mobius replay_2 spawns Tamil 27035 at Perrin.
    g.d(PERRIN, "event", None, event="replay_2", raw=no_spawn("Perrin"), ops=[setv("memo", 3)], **m(2))
    for mob in (27032, 27033, 27034):
        g.d(mob, "kill", None, lacksItems=[ORDER], ops=[give(ORDER), cond(3)])
    g.d(27035, "kill", None, lacksItems=[NECK], ops=[give(NECK), cond(5)])
    return g.quest("Path of the Elven Oracle", [MEDAL, MONEY, DIARY, ORDER, HALF, NECK])


path = ROOT / "data/quests.json"
data = json.loads(path.read_text())
new = [q406(), q407(), q409()]
ids = {q["id"] for q in new}
data["quests"] = sorted([q for q in data["quests"] if q["id"] not in ids] + new, key=lambda q: q["id"])
path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
for q in new:
    print(f"quest {q['id']}: {len(q['dialogs'])} dialogs")
