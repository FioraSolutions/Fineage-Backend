#!/usr/bin/env python3
"""Hand-parity port of Mobius Galladucci travel-home quests 45–49.

Mobius: Giran Galladucci (30097) → Gentler/Sandra/Dustin errands → SoE to race village.
Requires Mark of Traveler (7570) from the matching Step Into / Trip Begins chain.
Fineage has no "prior quest completed" gate — Mark item is the gate (same as Mobius item check).
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

GALLADUCCI, GENTLER, SANDRA, DUSTIN = 30097, 30094, 30090, 30116
MARK = 7570
ORDER1, ORDER2, ORDER3 = 7563, 7564, 7565
HILT, POWDER, NECK = 7568, 7567, 7566
MIN_LEVEL = 3

# id, folder, name, SoE scroll
VARIANTS = [
    (45, "Q00045_ToTalkingIsland", "To Talking Island", 7554),
    (46, "Q00046_OnceMoreInTheArmsOfTheMotherTree", "Once More In The Arms Of The Mother Tree", 7555),
    (47, "Q00047_IntoTheDarkElvenForest", "Into The Dark Elven Forest", 7556),
    (48, "Q00048_ToTheImmortalPlateau", "To The Immortal Plateau", 7557),
    (49, "Q00049_TheRoadHome", "The Road Home", 7558),
]

give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
START = [{"op": "create"}, {"op": "start"}]


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

    def d(npc, hook, page, when="started", ops=None, event=None, **gates):
        row = {"npcId": npc, "hook": hook, "when": when}
        if event:
            row["event"] = event
        row.update(gates)
        row["html"] = html(page)
        if ops:
            row["ops"] = ops
        return row

    return d


def build(qid: int, script: str, name: str, soe: int) -> dict:
    d = html_loader(script, qid)
    # event tokens without extension (Fineage event names)
    dialogs = [
        # Offer / refuse
        d(GALLADUCCI, "talk", "30097-03.html", when="absent", maxLevel=MIN_LEVEL - 1),
        d(GALLADUCCI, "talk", "30097-02.html", when="absent", minLevel=MIN_LEVEL, lacksItems=[MARK]),
        d(GALLADUCCI, "talk", "30097-01.htm", when="absent", minLevel=MIN_LEVEL, hasItem=MARK),
        d(
            GALLADUCCI,
            "event",
            "30097-04.htm",
            when="absent",
            event="30097-04",
            minLevel=MIN_LEVEL,
            hasItem=MARK,
            ops=START + [give(ORDER1), cond(1)],
        ),
        # Cond 1: Galladucci waiting / Gentler turn-in
        d(GALLADUCCI, "talk", "30097-05.html", cond=1, hasItem=ORDER1),
        d(GENTLER, "talk", "30094-01.html", cond=1, hasItem=ORDER1),
        d(
            GENTLER,
            "event",
            "30094-02.html",
            event="30094-02",
            cond=1,
            hasItem=ORDER1,
            ops=[take(ORDER1), give(HILT), cond(2)],
        ),
        d(GENTLER, "talk", "30094-03.html", cond=1, lacksItems=[ORDER1]),
        # Cond 2
        d(GENTLER, "talk", "30094-04.html", cond=2),
        d(GALLADUCCI, "talk", "30097-06.html", cond=2, hasItem=HILT),
        d(
            GALLADUCCI,
            "event",
            "30097-07.html",
            event="30097-07",
            cond=2,
            hasItem=HILT,
            ops=[take(HILT), give(ORDER2), cond(3)],
        ),
        d(GALLADUCCI, "talk", "30097-08.html", cond=2, lacksItems=[HILT]),
        # Cond 3
        d(GALLADUCCI, "talk", "30097-09.html", cond=3, hasItem=ORDER2),
        d(SANDRA, "talk", "30090-01.html", cond=3, hasItem=ORDER2),
        d(
            SANDRA,
            "event",
            "30090-02.html",
            event="30090-02",
            cond=3,
            hasItem=ORDER2,
            ops=[take(ORDER2), give(POWDER), cond(4)],
        ),
        d(SANDRA, "talk", "30090-03.html", cond=3, lacksItems=[ORDER2]),
        # Cond 4
        d(SANDRA, "talk", "30090-04.html", cond=4),
        d(GALLADUCCI, "talk", "30097-10.html", cond=4, hasItem=POWDER),
        d(
            GALLADUCCI,
            "event",
            "30097-11.html",
            event="30097-11",
            cond=4,
            hasItem=POWDER,
            ops=[take(POWDER), give(ORDER3), cond(5)],
        ),
        d(GALLADUCCI, "talk", "30097-12.html", cond=4, lacksItems=[POWDER]),
        # Cond 5
        d(GALLADUCCI, "talk", "30097-13.html", cond=5, hasItem=ORDER3),
        d(DUSTIN, "talk", "30116-01.html", cond=5, hasItem=ORDER3),
        d(
            DUSTIN,
            "event",
            "30116-02.html",
            event="30116-02",
            cond=5,
            hasItem=ORDER3,
            ops=[take(ORDER3), give(NECK), cond(6)],
        ),
        d(DUSTIN, "talk", "30116-03.html", cond=5, lacksItems=[ORDER3]),
        # Cond 6 finish
        d(DUSTIN, "talk", "30116-04.html", cond=6),
        d(GALLADUCCI, "talk", "30097-14.html", cond=6, hasItem=NECK),
        d(
            GALLADUCCI,
            "event",
            "30097-15.html",
            event="30097-15",
            cond=6,
            hasItem=NECK,
            ops=[take(NECK), give(soe), {"op": "complete"}],
        ),
        d(GALLADUCCI, "talk", "30097-16.html", cond=6, lacksItems=[NECK]),
    ]
    return {
        "id": qid,
        "name": name[:40],
        "repeatable": False,
        "abortable": True,
        "questItems": [ORDER1, ORDER2, ORDER3, HILT, POWDER, NECK],
        "dialogs": dialogs,
    }


def main() -> None:
    for qid, script, name, soe in VARIANTS:
        questio.upsert_quest(build(qid, script, name, soe))
        print(f"upserted {qid} {name}")


if __name__ == "__main__":
    main()
