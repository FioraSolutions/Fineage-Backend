#!/usr/bin/env python3
"""Hand-parity Mobius Q19 / Q113 / Q3 / Q640 (delivery + DE collect + stakato sell).

  Q19  Go to the Pastureland — meat delivery → 30k adena
  Q113 Status of the Beacon Tower — fire box delivery → 21578 adena
  Q3   Will the Seal Be Broken — DE race; eye/stone/blood → scroll B
  Q640 The Zero Hour — stakato fangs → material pick (Asofe 4043 missing → adena)

After: python3 scripts/gen_quests_19_640_mix.py
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = ROOT / "ref/mobius-ct0-interlude/dist/game/data/scripts/quests"
if not QROOT.is_dir():
    QROOT = ROOT / "ref/mobius-ct0-interlude/data/scripts/quests"

cond = lambda c: {"op": "set_cond", "cond": c}
START = [{"op": "create"}, {"op": "start"}]
exp_sp = lambda e, s: {"op": "give_exp_sp", "exp": e, "sp": s}


def drop(i, chance, limit=0, at=None, count=1):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


def give(i, count=1):
    return {"op": "give_item", "itemId": i, "count": count}


def take(i, count=1):
    return {"op": "take_item", "itemId": i, "count": count}


def adena(n):
    return {"op": "give_adena", "count": n}


def html_loader(script: str, qid: int):
    src = QROOT / script

    def html(name: str) -> str:
        candidates = [src / name] if re.search(r"\.html?$", name, re.I) else [
            src / f"{name}{ext}" for ext in ("", ".htm", ".html")
        ]
        for p in candidates:
            if p.exists():
                text = p.read_text(encoding="utf-8").strip()
                return re.sub(
                    rf"bypass -h Quest {re.escape(script)} ([A-Za-z0-9_\-]+)(?:\.html?)?",
                    lambda m: f"quest_event:{qid}:{m.group(1)}",
                    text,
                )
        raise SystemExit(f"{script}: missing html {name}")

    def d(npc, hook, page, when="started", ops=None, event=None, raw=None, **gates):
        row = {"npcId": npc, "hook": hook, "when": when}
        if event:
            row["event"] = event
        row.update(gates)
        row["html"] = raw if raw is not None else html(page)
        if ops:
            row["ops"] = ops
        return row

    return d


def q19():
    Q, d = 19, html_loader("Q00019_GoToThePastureland", 19)
    VLAD, TUNA = 31302, 31537
    MEAT = 7547
    dialogs = [
        d(VLAD, "talk", "31302-03.htm", when="absent", maxLevel=62),
        d(VLAD, "talk", "31302-00.htm", when="absent", minLevel=63),
        d(
            VLAD,
            "event",
            "31302-01.htm",
            when="absent",
            event="31302-01",
            minLevel=63,
            ops=START + [cond(1), give(MEAT)],
        ),
        d(VLAD, "talk", "31302-02.htm", when="started"),
        d(TUNA, "talk", "31537-00.htm", when="started"),
        d(
            TUNA,
            "event",
            "31537-01.htm",
            when="started",
            event="019_finish",
            hasItem=MEAT,
            ops=[take(MEAT), adena(30000), {"op": "complete"}],
        ),
        d(
            TUNA,
            "event",
            "31537-02.htm",
            when="started",
            event="019_finish",
            lacksItems=[MEAT],
        ),
    ]
    return {
        "id": Q,
        "name": "Go to the Pastureland",
        "repeatable": False,
        "abortable": True,
        "questItems": [MEAT],
        "dialogs": dialogs,
    }


def q113():
    Q, d = 113, html_loader("Q00113_StatusOfTheBeaconTower", 113)
    MOIRA, TORRANT = 31979, 32016
    BOX = 8086
    dialogs = [
        d(MOIRA, "talk", "31979-00.htm", when="absent", maxLevel=39),
        d(MOIRA, "talk", "31979-01.htm", when="absent", minLevel=40),
        d(
            MOIRA,
            "event",
            "31979-02.htm",
            when="absent",
            event="31979-02",
            minLevel=40,
            ops=START + [cond(1), give(BOX)],
        ),
        d(MOIRA, "talk", "31979-03.htm", when="started"),
        d(TORRANT, "talk", "32016-01.htm", when="started", hasItem=BOX),
        d(
            TORRANT,
            "event",
            "32016-02.htm",
            when="started",
            event="32016-02",
            hasItem=BOX,
            ops=[take(BOX), adena(21578), {"op": "complete"}],
        ),
    ]
    return {
        "id": Q,
        "name": "Status of the Beacon Tower",
        "repeatable": False,
        "abortable": True,
        "questItems": [BOX],
        "dialogs": dialogs,
    }


def q3():
    Q, d = 3, html_loader("Q00003_WillTheSealBeBroken", 3)
    TALLOTH = 30141
    EYE, STONE, BLOOD, SCROLL = 1081, 1082, 1083, 956
    # Race: Dark Elf = 2
    dialogs = [
        d(TALLOTH, "talk", "30141-00.htm", when="absent", races=[0, 1, 3, 4]),
        d(TALLOTH, "talk", "30141-01.html", when="absent", races=[2], maxLevel=15),
        d(TALLOTH, "talk", "30141-02.htm", when="absent", races=[2], minLevel=16),
        d(
            TALLOTH,
            "event",
            "30141-03.htm",
            when="absent",
            event="30141-03",
            races=[2],
            minLevel=16,
            ops=START + [cond(1)],
        ),
        d(
            TALLOTH,
            "talk",
            "30141-06.html",
            cond=1,
            atLeast={EYE: 1, STONE: 1, BLOOD: 1},
            ops=[take(EYE), take(STONE), take(BLOOD), give(SCROLL), {"op": "complete"}],
        ),
        d(TALLOTH, "talk", "30141-04.html", cond=1),
        d(TALLOTH, "event", "30141-05.html", cond=1, event="30141-05"),
    ]
    # Unique one-shots (Mobius: give if missing; cond 2 when all three — we finish via atLeast).
    for mob in (20031,):
        dialogs.append(
            d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(EYE, 1.0, limit=1)])
        )
    for mob in (20041, 20046):
        dialogs.append(
            d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(STONE, 1.0, limit=1)])
        )
    for mob in (20048, 20052, 20057):
        dialogs.append(
            d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(BLOOD, 1.0, limit=1)])
        )
    return {
        "id": Q,
        "name": "Will the Seal Be Broken",
        "repeatable": False,
        "abortable": True,
        "questItems": [EYE, STONE, BLOOD],
        "dialogs": dialogs,
    }


def q640():
    Q, d = 640, html_loader("Q00640_TheZeroHour", 640)
    KAHMAN = 31554
    FANG = 8085
    # Mobius REWARDS: {cost, itemId, count}; index 1 Asofe(4043) missing → adena stand-in
    rewards = [
        (12, 4042, 1),   # 0 Enria
        None,            # 1 Asofe missing
        (6, 4044, 1),    # 2 Thons
        (81, 1887, 10),  # 3 Varnish of Purity
        (33, 1888, 5),   # 4 Synthetic Cokes
        (30, 1889, 10),  # 5 Compound Braid
        (150, 5550, 10), # 6 Durable Metal Plate
        (131, 1890, 10), # 7 Mithril Alloy
        (123, 1893, 5),  # 8 Oriharukon
    ]
    mobs = [
        22105, 22106, 22107, 22108, 22109, 22110, 22111, 22113, 22114,
        22115, 22116, 22117, 22118, 22119, 22121,
    ]
    dialogs = [
        d(KAHMAN, "talk", "31554-00.htm", when="absent", maxLevel=65),
        # Q109 prerequisite omitted (no completed-quest gate yet)
        d(KAHMAN, "talk", "31554-01.htm", when="absent", minLevel=66),
        d(
            KAHMAN,
            "event",
            "31554-02.htm",
            when="absent",
            event="31554-02",
            minLevel=66,
            ops=START + [cond(1)],
        ),
        d(KAHMAN, "talk", "31554-04.htm", when="started", hasItem=FANG),
        d(KAHMAN, "talk", "31554-03.htm", when="started", lacksItems=[FANG]),
        d(
            KAHMAN,
            "event",
            "31554-05.htm",
            when="started",
            event="31554-05",
            hasItem=FANG,
        ),
        d(
            KAHMAN,
            "event",
            "31554-06.htm",
            when="started",
            event="31554-05",
            lacksItems=[FANG],
        ),
        d(
            KAHMAN,
            "event",
            "31554-08.htm",
            when="started",
            event="31554-08",
            ops=[{"op": "complete"}],
        ),
    ]
    for idx, row in enumerate(rewards):
        ev = str(idx)
        if row is None:
            # Asofe (4043) absent from items.json — adena stand-in for 6 fangs
            dialogs.append(
                d(
                    KAHMAN,
                    "event",
                    "31554-09.htm",
                    when="started",
                    event=ev,
                    atLeast={FANG: 6},
                    ops=[take(FANG, 6), adena(30000)],
                )
            )
            dialogs.append(
                d(
                    KAHMAN,
                    "event",
                    "31554-06.htm",
                    when="started",
                    event=ev,
                    # not enough fangs
                )
            )
            continue
        cost, item, count = row
        dialogs.append(
            d(
                KAHMAN,
                "event",
                "31554-09.htm",
                when="started",
                event=ev,
                atLeast={FANG: cost},
                ops=[take(FANG, cost), give(item, count)],
            )
        )
        dialogs.append(
            d(
                KAHMAN,
                "event",
                "31554-06.htm",
                when="started",
                event=ev,
            )
        )
    # Mobius CT0 gives 10 fangs per kill (commented single-drop left in source)
    for mob in mobs:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(FANG, 1.0, count=10)],
            )
        )
    return {
        "id": Q,
        "name": "The Zero Hour",
        "repeatable": True,
        "abortable": True,
        "questItems": [FANG],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q19(), q113(), q3(), q640()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
