#!/usr/bin/env python3
"""Stub batch 3: Q127, Q384, Q386, Q422, Q648.

After: python3 scripts/gen_quests_stubs_batch3.py
       python3 scripts/gen_drop_items.py .cache/l2j-mobius/.../stats/items
       python3 tools/quest_status.py
"""
from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = questio.mobius_quest_root()

START = [{"op": "create"}, {"op": "start"}]
cond = lambda c: {"op": "set_cond", "cond": c}
DONE = {"op": "complete"}


def drop(i, chance, limit=0, at=None, count=1):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


def give(i, count=1):
    return {"op": "give_item", "itemId": i, "count": count}


def take(i, count=1):
    return {"op": "take_item", "itemId": i, "count": count}


def take_all(*ids: int):
    return {"op": "take_all", "items": list(ids)}


def adena(c):
    return {"op": "give_adena", "count": c}


def html_loader(script: str, qid: int):
    def html(name: str) -> str:
        return questio.mobius_quest_html(QROOT, script, qid, name)

    def d(npc, hook, page, when="started", ops=None, event=None, raw=None, **gates):
        row = {"npcId": npc, "hook": hook, "when": when}
        if event is not None:
            row["event"] = event
        row.update(gates)
        row["html"] = raw if raw is not None else (html(page) if page else "")
        if ops:
            row["ops"] = ops
        return row

    return html, d


def add_kills(dialogs, d, mobs: list[int], ore: int, chance: float = 1.0):
    for mob in mobs:
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(ore, chance)]))


Q386_MOBS = [
    20670, 20671, 20954, 20956, 20958, 20959, 20960, 20963, 20967, 20969,
    20970, 20971, 20974, 20975, 21001, 21003, 21005, 21020, 21021, 21108,
    21110, 21113, 21114, 21116, 21258, 21259,
]

Q384_MOBS = [
    20242, 20950, 20774, 20635, 20947, 20241, 20942, 20759, 20758, 20281,
    20605, 20282, 20945, 20773, 20760, 20944, 20946, 20556, 20948, 20286,
    20559, 20668, 20677, 20943, 20949, 20243,
]

Q648_MOBS = list(range(22080, 22099))
Q648_MOBS.remove(22091)  # not in Mobius CHANCES


def q127() -> dict:
    Q = 127
    _, d = html_loader("Q00127_KamaelAWindowToTheFuture", Q)
    DOMINIC, KLAUS, ALDER, AKLAN, OLTLIN, JURIS, RODEMAI = 31350, 30187, 32092, 31288, 30862, 30113, 30756
    M_DOM, M_HUM, M_DWARF, M_ELF, M_DELF, M_ORC = 8939, 8940, 8941, 8942, 8943, 8944
    marks = (M_HUM, M_DWARF, M_ELF, M_DELF, M_ORC, M_DOM)
    dialogs = [
        d(DOMINIC, "talk", "31350-01.htm", when="absent"),
        d(DOMINIC, "event", "31350-04.htm", when="absent", event="31350-04", ops=START + [cond(1), give(M_DOM)]),
        d(KLAUS, "talk", "30187-01.htm", cond=1),
        d(KLAUS, "event", "30187-06.htm", event="30187-06", cond=1, ops=[cond(2)]),
        d(KLAUS, "talk", "30187-06.htm", cond=2),
        d(KLAUS, "event", "30187-08.htm", event="30187-08", cond=2, ops=[cond(3), give(M_HUM)]),
        d(ALDER, "talk", "32092-01.htm", cond=3),
        d(ALDER, "event", "32092-05.htm", event="32092-05", cond=3, ops=[cond(4), give(M_DWARF)]),
        d(AKLAN, "talk", "31288-01.htm", cond=4),
        d(AKLAN, "event", "31288-04.htm", event="31288-04", cond=4, ops=[cond(5), give(M_ORC)]),
        d(OLTLIN, "talk", "30862-01.htm", cond=5),
        d(OLTLIN, "event", "30862-04.htm", event="30862-04", cond=5, ops=[cond(6), give(M_DELF)]),
        d(JURIS, "talk", "30113-01.htm", cond=6),
        d(JURIS, "event", "30113-04.htm", event="30113-04", cond=6, ops=[cond(7), give(M_ELF)]),
        d(RODEMAI, "talk", "30756-01.htm", cond=7),
        d(RODEMAI, "event", "30756-05.htm", event="kamaelstory", cond=7, ops=[cond(8)]),
        d(RODEMAI, "talk", "30756-04.htm", cond=8),
        d(RODEMAI, "event", "30756-05.htm", event="30756-05", cond=8, ops=[cond(9)]),
        d(DOMINIC, "talk", "31350-05.htm", cond=9),
        d(
            DOMINIC,
            "event",
            "31350-06.htm",
            event="31350-06",
            cond=9,
            hasItems=marks,
            ops=[take_all(*marks), adena(159100), DONE],
        ),
    ]
    return {
        "id": Q,
        "name": "Kamael: A Window to the Future",
        "repeatable": False,
        "abortable": True,
        "questItems": list(marks),
        "dialogs": dialogs,
    }


def q384() -> dict:
    Q = 384
    _, d = html_loader("Q00384_WarehouseKeepersPastime", Q)
    CLIFF, BAXT = 30182, 30685
    MEDAL = 5964
    REWARD = 852  # Moonstone Earring — Mobius bingo reward pool simplified
    accept = START + [cond(1)]
    turn = [take(MEDAL, 10), give(REWARD), DONE]
    dialogs = [
        d(CLIFF, "talk", "30182-01.htm", when="absent", minLevel=40),
        d(CLIFF, "talk", "30182-04.html", when="absent", maxLevel=39),
        d(CLIFF, "event", "30182-05.htm", when="absent", minLevel=40, event="QUEST_ACCEPTED", ops=accept),
        d(CLIFF, "talk", "30182-06.html", cond=1, below={str(MEDAL): 10}),
        d(CLIFF, "talk", "30182-07.html", cond=1, atLeast={str(MEDAL): 10}, ops=turn),
        d(BAXT, "talk", "30685-06.html", cond=1, below={str(MEDAL): 10}),
        d(BAXT, "talk", "30685-07.html", cond=1, atLeast={str(MEDAL): 10}, ops=turn),
    ]
    add_kills(dialogs, d, Q384_MOBS, MEDAL, 0.4)
    return {
        "id": Q,
        "name": "Warehouse Keeper's Pastime",
        "repeatable": True,
        "abortable": True,
        "questItems": [MEDAL],
        "dialogs": dialogs,
    }


def q386() -> dict:
    Q = 386
    _, d = html_loader("Q00386_StolenDignity", Q)
    ROMP = 30843
    ORE = 6363
    REWARD = 5529  # Dragon Slayer Edge — simplified bingo pool
    accept = START + [cond(1)]
    turn = [take(ORE, 100), give(REWARD), DONE]
    dialogs = [
        d(ROMP, "talk", "30843-01.htm", when="absent", minLevel=58),
        d(ROMP, "talk", "30843-04.html", when="absent", maxLevel=57),
        d(ROMP, "event", "30843-05.htm", when="absent", minLevel=58, event="QUEST_ACCEPTED", ops=accept),
        d(ROMP, "talk", "30843-06.html", cond=1, below={str(ORE): 100}),
        d(ROMP, "talk", "30843-07.html", cond=1, atLeast={str(ORE): 100}, ops=turn),
    ]
    add_kills(dialogs, d, Q386_MOBS, ORE, 0.35)
    return {
        "id": Q,
        "name": "Stolen Dignity",
        "repeatable": True,
        "abortable": True,
        "questItems": [ORE],
        "dialogs": dialogs,
    }


def q422() -> dict:
    Q = 422
    _, d = html_loader("Q00422_RepentYourSins", Q)
    JUDGE, KATARI = 30981, 30668
    SKULL, MANUAL = 4326, 4331
    dialogs = [
        d(JUDGE, "talk", "30981-02.htm", when="absent"),
        d(
            JUDGE,
            "event",
            "30981-03.htm",
            when="absent",
            event="Start",
            maxLevel=20,
            ops=START + [cond(2)],
        ),
        d(
            JUDGE,
            "event",
            "30981-04.htm",
            when="absent",
            event="Start",
            minLevel=21,
            maxLevel=30,
            ops=START + [cond(3)],
        ),
        d(
            JUDGE,
            "event",
            "30981-05.htm",
            when="absent",
            event="Start",
            minLevel=31,
            maxLevel=40,
            ops=START + [cond(4)],
        ),
        d(
            JUDGE,
            "event",
            "30981-06.htm",
            when="absent",
            event="Start",
            minLevel=41,
            ops=START + [cond(5)],
        ),
        d(KATARI, "talk", "30668-01.htm", cond=2, ops=[cond(6)]),
        d(KATARI, "talk", "30668-02.htm", cond=6, below={str(SKULL): 10}),
        d(
            KATARI,
            "talk",
            "30668-03.htm",
            cond=6,
            atLeast={str(SKULL): 10},
            ops=[take(SKULL, 10), cond(10)],
        ),
        d(JUDGE, "talk", "30981-08.htm", cond=10, ops=[cond(14), give(MANUAL)]),
    ]
    dialogs.append(d(20039, "kill", None, cond=6, below={str(SKULL): 10}, raw="", ops=[drop(SKULL, 1.0)]))
    return {
        "id": Q,
        "name": "Repent Your Sins",
        "repeatable": True,
        "abortable": True,
        "questItems": [SKULL, MANUAL, 4327, 4328, 4329, 4330, 4425, 4426],
        "dialogs": dialogs,
    }


def q648() -> dict:
    Q = 648
    _, d = html_loader("Q00648_AnIceMerchantsDream", Q)
    RAFFORTY = 32020
    HEMO, SILVER, CRAFTED = 8057, 8077, 1894
    dialogs = [
        d(RAFFORTY, "talk", "32020-01.htm", when="absent", maxLevel=52),
        d(RAFFORTY, "talk", "32020-02.htm", when="absent", minLevel=53),
        d(RAFFORTY, "event", "32020-04.htm", when="absent", minLevel=53, event="32020-04", ops=START),
        d(RAFFORTY, "event", "32020-05.htm", when="started", event="32020-05", ops=[cond(2)]),
        d(
            RAFFORTY,
            "event",
            "32020-08.htm",
            event="32020-17a",
            cond=2,
            atLeast={str(SILVER): 23},
            ops=[take(SILVER, 23), give(CRAFTED)],
        ),
    ]
    for mob in Q648_MOBS:
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(SILVER, 0.45)]))
        dialogs.append(d(mob, "kill", None, cond=2, raw="", ops=[drop(HEMO, 0.08)]))
    return {
        "id": Q,
        "name": "An Ice Merchant's Dream",
        "repeatable": True,
        "abortable": True,
        "questItems": [HEMO, SILVER, 8078],
        "dialogs": dialogs,
    }


def main() -> None:
    questio.ensure_meta(reference="Mobius CT_0 batch3 via scripts/gen_quests_stubs_batch3.py")
    for q in (q127(), q384(), q386(), q422(), q648()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")
    questio.ensure_meta(reference="Mobius CT_0 batch3 via scripts/gen_quests_stubs_batch3.py")


if __name__ == "__main__":
    main()