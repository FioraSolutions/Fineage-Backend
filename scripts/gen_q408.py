#!/usr/bin/env python3
"""Q408 Path of the Elven Wizard -> data/quests/ pack (questio.upsert) (replaces quest 408 in place).
Transcribes Mobius CT0 Interlude Q00408_PathOfTheElvenWizard.java branch for branch as dialog gates
(classes / minLevel / maxLevel / hasItem+minCount / hasItems / lacksItems) and its HTML (links rewritten to
quest_event:408:<event>, '.htm(l)' dropped from event names). Run scripts/gen_drop_items.py and
scripts/gen_quest_item_flags.py afterwards so the quest items exist and carry the quest flag."""
import json, pathlib, re

ROOT = pathlib.Path(__file__).resolve().parent.parent
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import questio

QROOT = questio.mobius_quest_root()
SCRIPT = "Q00408_PathOfTheElvenWizard"
Q = 408
ROSSELA, GREENIS, THALIA, NORTHWIND = 30414, 30157, 30371, 30423
LETTER, RED_DOWN, RUBY, AQUAMARINE, APPLE, GOLD_LEAVES, LOVE, AMETHYST, NOBILITY, PERIDOT = 1218, 1219, 1220, 1221, 1222, 1223, 1224, 1225, 1226, 1229
CHARM, SAP, POTPOURRI, DIAMOND = 1272, 1273, 1274, 1230
ELVEN_MAGE, ELVEN_WIZARD = 25, 26
CARRIERS = [LETTER, APPLE, LOVE, CHARM, SAP, POTPOURRI]


def html(name):
    return questio.mobius_quest_html(QROOT, SCRIPT, Q, name)


def d(npc, hook, page, when="started", ops=None, event=None, **gates):
    row = {"npcId": npc, "hook": hook, "when": when}
    if event:
        row["event"] = event
    row.update(gates)
    row["html"] = html(page) if page else ""
    if ops:
        row["ops"] = ops
    return row


give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}

dialogs = []
# Rossela: start page (created/completed in Mobius both show 30414-01).
dialogs += [d(ROSSELA, "talk", "30414-01.htm", when="absent"), d(ROSSELA, "talk", "30414-01.htm", when="completed")]
# ACCEPT: class != ELVEN_MAGE ? (ELVEN_WIZARD ? 02a : 03) : level < 18 ? 04 : diamond ? 05 : start + peridot, 06
dialogs += [
    d(ROSSELA, "event", "30414-06.htm", when="absent", event="ACCEPT", classes=[ELVEN_MAGE], minLevel=18, lacksItems=[DIAMOND],
      ops=[{"op": "create"}, {"op": "start"}, give(PERIDOT)]),
    d(ROSSELA, "event", "30414-05.htm", when="any", event="ACCEPT", classes=[ELVEN_MAGE], minLevel=18),
    d(ROSSELA, "event", "30414-04.htm", when="any", event="ACCEPT", classes=[ELVEN_MAGE]),
    d(ROSSELA, "event", "30414-02a.htm", when="any", event="ACCEPT", classes=[ELVEN_WIZARD]),
    d(ROSSELA, "event", "30414-03.htm", when="any", event="ACCEPT"),
    d(ROSSELA, "event", "30414-02.htm", when="any", event="30414-02"),
]
# Symbol picks: show the done page, or hand out the carrier item once (Mobius "if !has give").
for ev, done, carrier, page in (("30414-10", RUBY, LETTER, "30414-07.htm"), ("30414-14", AQUAMARINE, APPLE, "30414-13.htm"),
                                ("30414-18", NOBILITY, LOVE, "30414-17.htm")):
    dialogs += [
        d(ROSSELA, "event", ev + ".htm", event=ev, hasItem=done),
        d(ROSSELA, "event", page, event=ev, hasItem=PERIDOT, lacksItems=[done, carrier], ops=[give(carrier)]),
        d(ROSSELA, "event", page, event=ev, hasItem=PERIDOT, lacksItems=[done]),
    ]
# Rossela started talk. Reward branch first (all four symbols, no carrier), then Mobius order.
reward_gates = dict(hasItems=[PERIDOT, RUBY, NOBILITY, AQUAMARINE], lacksItems=CARRIERS)
for lo, hi, exp, sp in ((20, 0, 320534, 22532), (19, 19, 456128, 29230), (0, 18, 591724, 35928)):
    g = dict(reward_gates)
    if lo:
        g["minLevel"] = lo
    if hi:
        g["maxLevel"] = hi
    dialogs.append(d(ROSSELA, "talk", "30414-20.htm", ops=[{"op": "give_adena", "count": 163800}, give(DIAMOND),
                                                          {"op": "give_exp_sp", "exp": exp, "sp": sp}, {"op": "complete"}], **g))
dialogs += [
    d(ROSSELA, "talk", "30414-11.htm", hasItem=PERIDOT, lacksItems=CARRIERS),
    d(ROSSELA, "talk", "30414-08.htm", hasItem=LETTER),
    d(ROSSELA, "talk", "30414-20.htm", hasItem=RED_DOWN, minCount=5, hasItems=[CHARM]),
    d(ROSSELA, "talk", "30414-09.htm", hasItem=CHARM),
    d(ROSSELA, "talk", "30414-14.htm", hasItem=APPLE),
    d(ROSSELA, "talk", "30414-24.htm", hasItem=GOLD_LEAVES, minCount=5, hasItems=[SAP]),
    d(ROSSELA, "talk", "30414-15.htm", hasItem=SAP),
    d(ROSSELA, "talk", "30414-18.htm", hasItem=LOVE),
    d(ROSSELA, "talk", "30414-25.htm", hasItem=AMETHYST, minCount=2, hasItems=[POTPOURRI]),
    d(ROSSELA, "talk", "30414-19.htm", hasItem=POTPOURRI),
]
# Greenis: letter -> charm; 5 red down + charm -> ruby.
dialogs += [
    d(GREENIS, "talk", "30157-01.htm", hasItem=LETTER),
    d(GREENIS, "talk", "30157-04.htm", hasItem=RED_DOWN, minCount=5, hasItems=[CHARM], lacksItems=[RUBY],
      ops=[take(RED_DOWN, 5), give(RUBY), take(CHARM)]),
    d(GREENIS, "talk", "30157-04.htm", hasItem=RED_DOWN, minCount=5, hasItems=[CHARM], ops=[take(RED_DOWN, 5), take(CHARM)]),
    d(GREENIS, "talk", "30157-03.htm", hasItem=CHARM),
    d(GREENIS, "event", "30157-02.htm", event="30157-02", hasItem=LETTER, lacksItems=[CHARM], ops=[take(LETTER), give(CHARM)]),
    d(GREENIS, "event", "30157-02.htm", event="30157-02", hasItem=LETTER, ops=[take(LETTER)]),
    d(GREENIS, "event", "30157-02.htm", event="30157-02"),
]
# Thalia: apple -> sap; 5 gold leaves + sap -> aquamarine.
dialogs += [
    d(THALIA, "talk", "30371-01.htm", hasItem=APPLE),
    d(THALIA, "talk", "30371-04.htm", hasItem=GOLD_LEAVES, minCount=5, hasItems=[SAP], lacksItems=[AQUAMARINE],
      ops=[give(AQUAMARINE), take(GOLD_LEAVES, 5), take(SAP)]),
    d(THALIA, "talk", "30371-04.htm", hasItem=GOLD_LEAVES, minCount=5, hasItems=[SAP], ops=[take(GOLD_LEAVES, 5), take(SAP)]),
    d(THALIA, "talk", "30371-03.htm", hasItem=SAP),
    d(THALIA, "event", "30371-02.htm", event="30371-02", hasItem=APPLE, lacksItems=[SAP], ops=[take(APPLE), give(SAP)]),
    d(THALIA, "event", "30371-02.htm", event="30371-02", hasItem=APPLE, ops=[take(APPLE)]),
    d(THALIA, "event", "30371-02.htm", event="30371-02"),
]
# Northwind: immortal love -> potpourri; 2 amethyst + potpourri -> nobility amethyst.
dialogs += [
    d(NORTHWIND, "talk", "30423-01.htm", hasItem=LOVE, lacksItems=[POTPOURRI], ops=[take(LOVE), give(POTPOURRI)]),
    d(NORTHWIND, "talk", "30423-01.htm", hasItem=LOVE, ops=[take(LOVE)]),
    d(NORTHWIND, "talk", "30423-03.htm", hasItem=AMETHYST, minCount=2, hasItems=[POTPOURRI], lacksItems=[NOBILITY],
      ops=[take(AMETHYST, 2), give(NOBILITY), take(POTPOURRI)]),
    d(NORTHWIND, "talk", "30423-03.htm", hasItem=AMETHYST, minCount=2, hasItems=[POTPOURRI], ops=[take(AMETHYST, 2), take(POTPOURRI)]),
    d(NORTHWIND, "talk", "30423-02.htm", hasItem=POTPOURRI),
]
# Kills (killer only, Mobius onKill + ALT_PARTY_RANGE check on the killer).
drop = lambda i, ch, lim: [{"op": "drop_item", "itemId": i, "count": 1, "chance": ch, "limit": lim}]
dialogs += [
    d(20019, "kill", None, hasItem=SAP, ops=drop(GOLD_LEAVES, 0.4, 5)),       # Dryad Elder
    d(20047, "kill", None, hasItem=POTPOURRI, ops=drop(AMETHYST, 0.4, 2)),    # Sukar Wererat Leader (Elven Ruins, not spawned)
    d(20466, "kill", None, hasItem=CHARM, ops=drop(RED_DOWN, 0.7, 5)),        # Pincer Spider
]

quest = {
    "id": Q, "name": "Path of the Elven Wizard", "repeatable": False, "abortable": True,
    "questItems": [LETTER, RED_DOWN, RUBY, AQUAMARINE, APPLE, GOLD_LEAVES, LOVE, AMETHYST, NOBILITY, PERIDOT, CHARM, SAP, POTPOURRI],
    "dialogs": dialogs,
}
questio.upsert_quest(quest)
print(f"quest {Q}: {len(dialogs)} dialogs")
