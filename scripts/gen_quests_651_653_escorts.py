#!/usr/bin/env python3
"""Hand-parity Mobius Q651 / Q652 / Q653 (SOE/soulshot escorts; deleteMe omitted).

  Q651 Runaway Youth — SOE → Batidae 2883 adena
  Q652 An Aged Ex Adventurer — 100 SS-C → Sara (adena or scroll+adena; most-common adena)
  Q653 Wild Maiden — SOE → Galibredo 2553 adena

After: python3 scripts/gen_quests_651_653_escorts.py
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


def q651():
    Q, d = 651, html_loader("Q00651_RunawayYouth", 651)
    BATIDAE, IVAN = 31989, 32014
    SOE = 736
    dialogs = [
        d(IVAN, "talk", "32014-01a.htm", when="absent", maxLevel=25),
        d(IVAN, "talk", "32014-01.htm", when="absent", minLevel=26),
        d(IVAN, "event", "32014-03.html", when="absent", event="32014-03", minLevel=26),
        d(
            IVAN,
            "event",
            "32014-04.htm",
            when="absent",
            event="32014-04",
            minLevel=26,
            hasItem=SOE,
            ops=START + [cond(1), take(SOE)],
        ),
        d(
            IVAN,
            "event",
            "32014-05.htm",
            when="absent",
            event="32014-04",
            minLevel=26,
            lacksItems=[SOE],
        ),
        d(IVAN, "talk", "32014-02.html", when="started"),
        d(
            BATIDAE,
            "talk",
            "31989-01.html",
            when="started",
            ops=[adena(2883), {"op": "complete"}],
        ),
    ]
    return {
        "id": Q,
        "name": "Runaway Youth",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def q652():
    Q, d = 652, html_loader("Q00652_AnAgedExAdventurer", 652)
    TANTAN, SARA = 32012, 30180
    SS_C, SCROLL = 1464, 956
    dialogs = [
        d(TANTAN, "talk", "32012-01a.htm", when="absent", maxLevel=45),
        d(TANTAN, "talk", "32012-01.htm", when="absent", minLevel=46),
        d(TANTAN, "event", "32012-03.html", when="absent", event="32012-03", minLevel=46),
        d(
            TANTAN,
            "event",
            "32012-04.htm",
            when="absent",
            event="32012-04",
            minLevel=46,
            atLeast={SS_C: 100},
            ops=START + [cond(1), take(SS_C, 100)],
        ),
        d(
            TANTAN,
            "event",
            "32012-05.htm",
            when="absent",
            event="32012-04",
            minLevel=46,
        ),
        d(TANTAN, "talk", "32012-02.html", when="started"),
        # Mobius 50% scroll+5026 vs 10000 — most-common path is 10000 (60%)
        d(
            SARA,
            "talk",
            "30180-02.html",
            when="started",
            ops=[adena(10000), {"op": "complete"}],
        ),
    ]
    return {
        "id": Q,
        "name": "An Aged Ex Adventurer",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def q653():
    Q, d = 653, html_loader("Q00653_WildMaiden", 653)
    GALIBREDO, SUKI = 30181, 32013
    SOE = 736
    dialogs = [
        d(SUKI, "talk", "32013-01a.htm", when="absent", maxLevel=35),
        d(SUKI, "talk", "32013-01.htm", when="absent", minLevel=36),
        d(SUKI, "event", "32013-03.html", when="absent", event="32013-03", minLevel=36),
        d(
            SUKI,
            "event",
            "32013-04.htm",
            when="absent",
            event="32013-04",
            minLevel=36,
            hasItem=SOE,
            ops=START + [cond(1), take(SOE)],
        ),
        d(
            SUKI,
            "event",
            "32013-05.htm",
            when="absent",
            event="32013-04",
            minLevel=36,
            lacksItems=[SOE],
        ),
        d(SUKI, "talk", "32013-02.htm", when="started"),
        d(
            GALIBREDO,
            "talk",
            "30181-01.html",
            when="started",
            ops=[adena(2553), {"op": "complete"}],
        ),
    ]
    return {
        "id": Q,
        "name": "Wild Maiden",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q651(), q652(), q653()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
