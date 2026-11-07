#!/usr/bin/env python3
"""Hand-parity Mobius Q158 / Q626 / Q629 (batch ~21–66 boss + swamp).

  Q158 Seed of Evil — Biotin, Nerkas tablet
  Q626 A Dark Twilight — Hierarch, blood×300 → Exp or Adena
  Q629 Clean Up the Swamp of Screams — Pierce, talons×100 → Ram coins

After: python3 scripts/gen_quests_21_66_boss_swamp.py
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


def q158():
    Q, d = 158, html_loader("Q00158_SeedOfEvil", 158)
    BIOTIN, NERKAS = 30031, 27016
    TABLET, SOE_D = 1025, 956
    dialogs = [
        d(BIOTIN, "talk", "30031-01.html", when="absent", maxLevel=20),
        d(BIOTIN, "talk", "30031-02.htm", when="absent", minLevel=21),
        d(
            BIOTIN,
            "event",
            "30031-03.htm",
            when="absent",
            event="30031-03",
            minLevel=21,
            ops=START + [cond(1)],
        ),
        d(BIOTIN, "talk", "30031-04.html", cond=1),
        d(
            BIOTIN,
            "talk",
            "30031-05.html",
            cond=2,
            hasItem=TABLET,
            ops=[give(SOE_D), adena(1495), exp_sp(17818, 927), {"op": "complete"}],
        ),
        d(
            NERKAS,
            "kill",
            None,
            cond=1,
            lacksItems=[TABLET],
            raw="",
            ops=[give(TABLET), cond(2)],
        ),
    ]
    return {
        "id": Q,
        "name": "Seed of Evil",
        "repeatable": False,
        "abortable": True,
        "questItems": [TABLET],
        "dialogs": dialogs,
    }


def q626():
    Q, d = 626, html_loader("Q00626_ADarkTwilight", 626)
    HIERARCH = 31517
    BLOOD = 7169
    # chance out of 1000
    mobs = {
        21520: 0.641, 21523: 0.648, 21524: 0.692, 21525: 0.710, 21526: 0.772,
        21529: 0.639, 21530: 0.683, 21531: 0.767, 21532: 0.795, 21535: 0.802,
        21536: 0.774, 21539: 0.848, 21540: 0.880, 21658: 0.790,
    }
    dialogs = [
        d(HIERARCH, "talk", "31517-00.htm", when="absent", maxLevel=59),
        d(HIERARCH, "talk", "31517-01.htm", when="absent", minLevel=60),
        d(
            HIERARCH,
            "event",
            "31517-02.htm",
            when="absent",
            event="31517-02",
            minLevel=60,
            ops=START + [cond(1)],
        ),
        d(HIERARCH, "event", "31517-05.html", when="started", event="31517-05"),
        d(HIERARCH, "talk", "31517-04.html", cond=2, atLeast={BLOOD: 300}),
        d(HIERARCH, "talk", "31517-03.html", when="started"),
        d(
            HIERARCH,
            "event",
            "31517-07.html",
            when="started",
            event="Exp",
            atLeast={BLOOD: 300},
            ops=[take(BLOOD, 300), exp_sp(162773, 12500), {"op": "complete"}],
        ),
        d(HIERARCH, "event", "31517-06.html", when="started", event="Exp"),
        d(
            HIERARCH,
            "event",
            "31517-07.html",
            when="started",
            event="Adena",
            atLeast={BLOOD: 300},
            ops=[take(BLOOD, 300), adena(100000), {"op": "complete"}],
        ),
        d(HIERARCH, "event", "31517-06.html", when="started", event="Adena"),
    ]
    for mob, ch in mobs.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(BLOOD, ch, limit=300, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "A Dark Twilight",
        "repeatable": True,
        "abortable": True,
        "questItems": [BLOOD],
        "dialogs": dialogs,
    }


def q629():
    Q, d = 629, html_loader("Q00629_CleanUpTheSwampOfScreams", 629)
    PIERCE = 31553
    TALON, COIN = 7250, 7251
    mobs = {
        21508: 0.599, 21509: 0.524, 21510: 0.640, 21511: 0.830, 21512: 0.970,
        21513: 0.682, 21514: 0.595, 21515: 0.727, 21516: 0.879, 21517: 0.999,
    }
    dialogs = [
        d(PIERCE, "talk", "31553-02.htm", when="absent", maxLevel=65),
        d(PIERCE, "talk", "31553-01.htm", when="absent", minLevel=66),
        d(
            PIERCE,
            "event",
            "31553-03.htm",
            when="absent",
            event="31553-03",
            minLevel=66,
            ops=START + [cond(1)],
        ),
        d(PIERCE, "event", "31553-04.html", when="started", event="31553-04"),
        d(PIERCE, "event", "31553-06.html", when="started", event="31553-06"),
        d(
            PIERCE,
            "event",
            "31553-07.html",
            when="started",
            event="31553-07",
            atLeast={TALON: 100},
            ops=[take(TALON, 100), give(COIN, 20)],
        ),
        d(PIERCE, "event", "31553-08.html", when="started", event="31553-07"),
        d(PIERCE, "event", "31553-09.html", when="started", event="31553-09", ops=[{"op": "complete"}]),
        d(PIERCE, "talk", "31553-04.html", when="started", atLeast={TALON: 100}),
        d(PIERCE, "talk", "31553-05.html", when="started"),
    ]
    for mob, ch in mobs.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(TALON, ch)])
        )
    return {
        "id": Q,
        "name": "Clean Up The Swamp Of Screams",
        "repeatable": True,
        "abortable": True,
        "questItems": [TALON, COIN],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q158(), q626(), q629()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
