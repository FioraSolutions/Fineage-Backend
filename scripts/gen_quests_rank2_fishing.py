#!/usr/bin/env python3
"""Mobius gap audit rank-2 batch 2: fishing bait Q50–53 and chest chain Q27–30.

  Q50–Q53 — special bait collects (party kill drop, 100× essence → lure reward)
  Q27–Q30 — treasure-chest hand-ins after bait quests (prerequisite Q50→Q51→Q52→Q53 not enforced in JSON)

Skip: Q255 (Fineage Q998), Q634/Q635 (dimensional rift).

After: python3 scripts/gen_quests_rank2_fishing.py
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = questio.mobius_quest_root()

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


def html_loader(script: str, qid: int):
    src = QROOT / script

    def _ev(raw: str) -> str:
        return re.sub(r"\.html?$", "", raw, flags=re.I)

    def rewrite(text: str) -> str:
        text = re.sub(
            rf"bypass -h Quest {re.escape(script)} ([A-Za-z0-9_\-]+)(?:\.html?)?",
            lambda m: f"quest_event:{qid}:{_ev(m.group(1))}",
            text,
        )
        text = re.sub(
            rf"bypass(?: -h)? Script {re.escape(script)} ([A-Za-z0-9_\-\.]+)(?:\.html?)?",
            lambda m: f"quest_event:{qid}:{_ev(m.group(1))}",
            text,
        )
        return text

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


BAIT_SPECS = [
  # qid, script, fisher, item, lure, mob, min_lvl
    (50, "Q00050_LanoscosSpecialBait", 31570, 7621, 7610, 21026, 27),
    (51, "Q00051_OFullesSpecialBait", 31572, 7622, 7611, 20552, 36),
    (52, "Q00052_WilliesSpecialBait", 31574, 7623, 7612, 20573, 48),
    (53, "Q00053_LinnaeusSpecialBait", 31577, 7624, 7613, 20670, 60),
]

CHEST_SPECS = [
    # qid, script, fisher, turn_in, chest, letter, reward, min_lvl
    (27, "Q00027_ChestCaughtWithABaitOfWind", 31570, 31434, 6500, 7625, 880, 27),
    (28, "Q00028_ChestCaughtWithABaitOfIcyAir", 31572, 31442, 6503, 7626, 881, 36),
    (29, "Q00029_ChestCaughtWithABaitOfEarth", 31574, 30909, 6507, 7627, 2455, 48),
    (30, "Q00030_ChestCaughtWithABaitOfFire", 31577, 30629, 6511, 7628, 916, 60),
]


def bait_quest(qid: int, script: str, fisher: int, item: int, lure: int, mob: int, min_lvl: int) -> dict:
    _, d = html_loader(script, qid)
    ev = str(fisher)
    dialogs = [
        d(fisher, "talk", f"{ev}-01.htm", when="absent", minLevel=min_lvl),
        d(fisher, "talk", f"{ev}-02.htm", when="absent", maxLevel=min_lvl - 1),
        d(
            fisher,
            "event",
            f"{ev}-03.htm",
            when="absent",
            event=f"{ev}-03",
            minLevel=min_lvl,
            ops=START + [cond(1)],
        ),
        d(fisher, "talk", f"{ev}-05.htm", when="started", cond=1),
        d(fisher, "talk", f"{ev}-04.htm", when="started", cond=2, atLeast={str(item): 100}),
        d(
            fisher,
            "event",
            f"{ev}-06.htm",
            when="started",
            event=f"{ev}-07",
            cond=2,
            atLeast={str(item): 100},
            ops=[take(item, 100), give(lure, 4), {"op": "complete"}],
        ),
        d(mob, "kill", None, when="started", cond=1, party=True, raw="", ops=[drop(item, 0.33, limit=100, at=2)]),
    ]
    return {
        "id": qid,
        "name": script.split("_", 1)[1],
        "repeatable": False,
        "abortable": True,
        "questItems": [item],
        "dialogs": dialogs,
    }


def chest_quest(qid: int, script: str, fisher: int, turn_in: int, chest: int, letter: int, reward: int, min_lvl: int) -> dict:
    _, d = html_loader(script, qid)
    fev = str(fisher)
    tev = str(turn_in)
    dialogs = [
        d(fisher, "talk", f"{fev}-01.htm", when="absent", minLevel=min_lvl),
        d(fisher, "talk", f"{fev}-02.htm", when="absent", maxLevel=min_lvl - 1),
        d(
            fisher,
            "event",
            f"{fev}-04.htm",
            when="absent",
            event=f"{fev}-04",
            minLevel=min_lvl,
            ops=START + [cond(1)],
        ),
        d(fisher, "talk", f"{fev}-06.htm", when="started", cond=1, lacksItems=[chest]),
        d(fisher, "talk", f"{fev}-05.htm", when="started", cond=1, hasItem=chest),
        d(
            fisher,
            "event",
            f"{fev}-07.htm",
            when="started",
            event=f"{fev}-07",
            cond=1,
            hasItem=chest,
            ops=[take(chest, 1), give(letter, 1), cond(2)],
        ),
        d(turn_in, "talk", f"{tev}-01.htm", when="started", cond=2, hasItem=letter),
        d(
            turn_in,
            "event",
            f"{tev}-02.htm",
            when="started",
            event=f"{tev}-02",
            cond=2,
            hasItem=letter,
            ops=[take(letter, 1), give(reward, 1), {"op": "complete"}],
        ),
    ]
    return {
        "id": qid,
        "name": script.split("_", 1)[1],
        "repeatable": False,
        "abortable": True,
        "questItems": [letter],
        "dialogs": dialogs,
    }


def main() -> None:
    questio.ensure_meta(max_active=25, talk_range=250)
    for spec in BAIT_SPECS:
        q = bait_quest(*spec)
        questio.upsert_quest(q)
        print(f"upserted {q['id']} dialogs={len(q['dialogs'])}")
    for spec in CHEST_SPECS:
        q = chest_quest(*spec)
        questio.upsert_quest(q)
        print(f"upserted {q['id']} dialogs={len(q['dialogs'])}")
    questio.ensure_meta(max_active=25, talk_range=250)


if __name__ == "__main__":
    main()