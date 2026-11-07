#!/usr/bin/env python3
"""Hand-parity Mobius Q353 / Q631 / Q643 (darkness stones + meat + dino bones).

  Q353 Power of Darkness — Malruk stones sell (230 + 2500)
  Q631 Delicious Top Choice Meat — meat×120 → material pick (Asofe→adena)
  Q643 Rise and Fall of the Elroki Tribe — bones sell / 300→adena (pieces missing)

After: python3 scripts/gen_quests_353_643_mix.py
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


def q353():
    Q, d = 353, html_loader("Q00353_PowerOfDarkness", 353)
    GALMAN = 31044
    STONE = 5862
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": STONE, "adena": 230}],
        "bonusMin": 1,
        "bonusAdena": 2500,
    }
    dialogs = [
        d(GALMAN, "talk", "31044-01.htm", when="absent", maxLevel=54),
        d(GALMAN, "talk", "31044-02.htm", when="absent", minLevel=55),
        d(GALMAN, "event", "31044-03.htm", when="absent", event="31044-03", minLevel=55),
        d(
            GALMAN,
            "event",
            "31044-04.htm",
            when="absent",
            event="31044-04",
            minLevel=55,
            ops=START + [cond(1)],
        ),
        d(GALMAN, "talk", "31044-06.htm", cond=1, hasItem=STONE, ops=[sell]),
        d(GALMAN, "talk", "31044-05.htm", cond=1, lacksItems=[STONE]),
        d(GALMAN, "event", "31044-07.htm", cond=1, event="31044-07"),
        d(
            GALMAN,
            "event",
            "31044-08.htm",
            when="started",
            event="31044-08",
            ops=[{"op": "complete"}],
        ),
    ]
    for mob, ch in ((20244, 0.48), (20283, 0.48), (20245, 0.50), (20284, 0.50)):
        dialogs.append(
            d(mob, "kill", None, cond=1, raw="", ops=[drop(STONE, ch)])
        )
    return {
        "id": Q,
        "name": "Power of Darkness",
        "repeatable": True,
        "abortable": True,
        "questItems": [STONE],
        "dialogs": dialogs,
    }


def q631():
    Q, d = 631, html_loader("Q00631_DeliciousTopChoiceMeat", 631)
    TUNA = 31537
    MEAT = 7546
    chances = {
        21460: 0.600, 21461: 0.480, 21462: 0.447, 21463: 0.808,
        21464: 0.447, 21465: 0.808, 21466: 0.447, 21467: 0.808,
        21479: 0.477, 21480: 0.863, 21481: 0.477, 21482: 0.863,
        21483: 0.477, 21484: 0.863, 21485: 0.477, 21486: 0.863,
        21498: 0.509, 21499: 0.920, 21500: 0.509, 21501: 0.920,
        21502: 0.509, 21503: 0.920, 21504: 0.509, 21505: 0.920,
    }
    # index → (item, count); 1 Asofe(4043) missing → adena
    rewards = {
        "0": (4039, 15),
        "1": None,
        "2": (4044, 15),
        "3": (4040, 10),
        "4": (4042, 10),
        "5": (4041, 5),
    }
    dialogs = [
        d(TUNA, "talk", "31537-02.htm", when="absent", maxLevel=64),
        d(TUNA, "talk", "31537-01.htm", when="absent", minLevel=65),
        d(
            TUNA,
            "event",
            "31537-03.htm",
            when="absent",
            event="31537-03",
            minLevel=65,
            ops=START + [cond(1)],
        ),
        d(TUNA, "talk", "31537-04.htm", cond=2, atLeast={MEAT: 120}),
        d(TUNA, "talk", "31537-03a.htm", when="started"),
        d(
            TUNA,
            "event",
            "31537-05.htm",
            event="31537-05",
            cond=2,
            atLeast={MEAT: 120},
        ),
    ]
    for ev, row in rewards.items():
        if row is None:
            dialogs.append(
                d(
                    TUNA,
                    "event",
                    "31537-06.htm",
                    event=ev,
                    cond=2,
                    atLeast={MEAT: 120},
                    ops=[take(MEAT, 120), adena(50000), {"op": "complete"}],
                )
            )
        else:
            item, count = row
            dialogs.append(
                d(
                    TUNA,
                    "event",
                    "31537-06.htm",
                    event=ev,
                    cond=2,
                    atLeast={MEAT: 120},
                    ops=[take(MEAT, 120), give(item, count), {"op": "complete"}],
                )
            )
        dialogs.append(
            d(TUNA, "event", "31537-07.htm", event=ev, when="started")
        )
    for mob, ch in chances.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(MEAT, ch, limit=120, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Delicious Top Choice Meat",
        "repeatable": True,
        "abortable": True,
        "questItems": [MEAT],
        "dialogs": dialogs,
    }


def q643():
    Q, d = 643, html_loader("Q00643_RiseAndFallOfTheElrokiTribe", 643)
    SING, KARA = 32106, 32117
    BONES = 8776
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": BONES, "adena": 1374}],
    }
    mobs = [
        22200, 22201, 22202, 22204, 22205, 22208, 22209, 22210, 22211, 22212, 22213,
        22219, 22220, 22221, 22222, 22224, 22225, 22226, 22227,
    ]
    dialogs = [
        d(SING, "talk", "32106-06.html", when="absent", maxLevel=74),
        d(SING, "talk", "32106-01.htm", when="absent", minLevel=75),
        d(SING, "event", "32106-02.htm", when="absent", event="32106-02", minLevel=75),
        d(SING, "event", "32106-04.htm", when="absent", event="32106-04"),
        d(
            SING,
            "event",
            "32106-03.html",
            when="absent",
            event="quest_accept",
            minLevel=75,
            ops=START + [cond(1)],
        ),
        d(
            SING,
            "event",
            "32106-07.html",
            when="absent",
            event="quest_accept",
            maxLevel=74,
        ),
        d(SING, "event", "32106-05.html", when="started", event="32106-05"),
        d(SING, "talk", "32106-08.html", when="started", hasItem=BONES),
        d(SING, "talk", "32106-14.html", when="started", lacksItems=[BONES]),
        d(
            SING,
            "event",
            "32106-09.html",
            when="started",
            event="32106-09",
            hasItem=BONES,
            ops=[sell],
        ),
        d(
            SING,
            "event",
            "32106-12.html",
            when="started",
            event="exit",
            hasItem=BONES,
            ops=[sell, {"op": "complete"}],
        ),
        d(
            SING,
            "event",
            "32106-11.html",
            when="started",
            event="exit",
            lacksItems=[BONES],
            ops=[{"op": "complete"}],
        ),
        d(SING, "event", "32106-10.html", when="started", event="32106-10"),
        d(SING, "event", "32106-13.html", when="started", event="32106-13"),
        # Karakawei: first-talk flag omitted → always show long intro then exchange
        d(KARA, "talk", "32117-01.html", when="started"),
        d(KARA, "event", "32117-02.html", when="started", event="32117-02"),
        d(KARA, "event", "32117-07.html", when="started", event="32117-07"),
        d(KARA, "event", "32117-06.html", when="started", event="32117-06"),
        # PIECE recipes 8712–8722 missing → adena stand-in for 300 bones
        d(
            KARA,
            "event",
            "32117-05.html",
            when="started",
            event="exchange",
            atLeast={BONES: 300},
            ops=[take(BONES, 300), adena(300 * 1374)],
        ),
        d(
            KARA,
            "event",
            "32117-04.html",
            when="started",
            event="exchange",
        ),
    ]
    for mob in mobs:
        # Mobius always rewards 8 (both chance branches) for plains mobs
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(BONES, 1.0, count=8)],
            )
        )
    dialogs.append(
        d(
            22203,
            "kill",
            None,
            when="started",
            party=True,
            raw="",
            ops=[drop(BONES, 0.558, count=8)],
        )
    )
    return {
        "id": Q,
        "name": "Rise and Fall of the Elroki Tribe",
        "repeatable": True,
        "abortable": True,
        "questItems": [BONES],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q353(), q631(), q643()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
