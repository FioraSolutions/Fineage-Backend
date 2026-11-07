#!/usr/bin/env python3
"""Hand-parity Mobius Q326 / Q328 (batch ~21 Ol Mahum + eye hunts).

  Q326 Vanquish Remnants — Leopold, cross badges sell
  Q328 Sense for Business — Sarien, eye/gizzard sell

Simplifications:
  Q326: skip Black Lion Mark (needs sum≥100 gate Fineage lacks).
  Q328: exclusive eye carcass/lens bands → independent rolls at band widths.

After: python3 scripts/gen_quests_21_ol_mahum_business.py
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


def q326():
    Q, d = 326, html_loader("Q00326_VanquishRemnants", 326)
    LEOPOLD = 30435
    RED, BLUE, BLACK = 1359, 1360, 1361
    # chance%, item
    drops = {
        20053: (0.61, RED),
        20058: (0.61, RED),
        20061: (0.57, BLUE),
        20063: (0.63, BLUE),
        20066: (0.59, BLACK),
        20436: (0.55, BLUE),
        20437: (0.59, RED),
        20438: (0.60, BLACK),
        20439: (0.62, BLUE),
    }
    sell = {
        "op": "sell_items",
        "prices": [
            {"itemId": RED, "adena": 46},
            {"itemId": BLUE, "adena": 52},
            {"itemId": BLACK, "adena": 58},
        ],
        "bonusMin": 10,
        "bonusAdena": 4320,
    }
    parts = [RED, BLUE, BLACK]
    dialogs = [
        d(LEOPOLD, "talk", "30435-01.htm", when="absent", maxLevel=20),
        d(LEOPOLD, "talk", "30435-02.htm", when="absent", minLevel=21),
        d(
            LEOPOLD,
            "event",
            "30435-03.htm",
            when="absent",
            event="30435-03",
            minLevel=21,
            ops=START + [cond(1)],
        ),
        d(LEOPOLD, "event", "30435-07.html", when="started", event="30435-07", ops=[{"op": "complete"}]),
        d(LEOPOLD, "event", "30435-08.html", when="started", event="30435-08"),
        d(LEOPOLD, "talk", "30435-05.html", when="started", hasAny=parts, ops=[sell]),
        d(LEOPOLD, "talk", "30435-04.html", when="started", lacksItems=parts),
    ]
    for mob, (ch, item) in drops.items():
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(item, ch)]))
    return {
        "id": Q,
        "name": "Vanquish Remnants",
        "repeatable": True,
        "abortable": True,
        "questItems": parts,
        "dialogs": dialogs,
    }


def q328():
    Q, d = 328, html_loader("Q00328_SenseForBusiness", 328)
    SARIEN = 30436
    CARCASS, LENS, GIZZARD = 1347, 1366, 1348
    # Exclusive bands → independent band widths
    eyes = {
        20055: (0.61, 0.01),
        20059: (0.61, 0.01),
        20067: (0.72, 0.02),
        20068: (0.78, 0.01),
    }
    basilisks = {20070: 0.60, 20072: 0.63}
    sell = {
        "op": "sell_items",
        "prices": [
            {"itemId": CARCASS, "adena": 25},
            {"itemId": LENS, "adena": 1000},
            {"itemId": GIZZARD, "adena": 60},
        ],
        "bonusMin": 10,
        "bonusAdena": 618,
    }
    parts = [CARCASS, LENS, GIZZARD]
    dialogs = [
        d(SARIEN, "talk", "30436-01.htm", when="absent", maxLevel=20),
        d(SARIEN, "talk", "30436-02.htm", when="absent", minLevel=21),
        d(
            SARIEN,
            "event",
            "30436-03.htm",
            when="absent",
            event="30436-03",
            minLevel=21,
            ops=START + [cond(1)],
        ),
        d(SARIEN, "event", "30436-06.html", when="started", event="30436-06", ops=[{"op": "complete"}]),
        d(SARIEN, "talk", "30436-05.html", when="started", hasAny=parts, ops=[sell]),
        d(SARIEN, "talk", "30436-04.html", when="started", lacksItems=parts),
    ]
    for mob, (carc, lens) in eyes.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                raw="",
                ops=[drop(CARCASS, carc), drop(LENS, lens)],
            )
        )
    for mob, ch in basilisks.items():
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(GIZZARD, ch)]))
    return {
        "id": Q,
        "name": "Sense for Business",
        "repeatable": True,
        "abortable": True,
        "questItems": parts,
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q326(), q328()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
