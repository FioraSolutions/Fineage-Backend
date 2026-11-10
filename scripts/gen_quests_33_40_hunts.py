#!/usr/bin/env python3
"""Hand-parity Mobius Q329 / Q338 (batch ~33–40 hunts).

  Q329 Curiosity of a Dwarf — Rolento, exclusive heartstone bands → independent
  Q338 Alligator Hunter — Enverun, always+19% leather, event sell

After: python3 scripts/gen_quests_33_40_hunts.py
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


def q329():
    Q, d = 329, html_loader("Q00329_CuriosityOfADwarf", 329)
    ROLENTO = 30437
    GOLEM, BROKEN = 1346, 1365
    # Exclusive: rnd < golem_thr → golem; elif rnd < broken_thr → broken
    # Independent band widths: golem=thr, broken=broken_thr-golem_thr
    mobs = {
        20083: (0.03, 0.51),  # Granitic Golem 3 / 54
        20085: (0.03, 0.55),  # Puncher 3 / 58
    }
    sell = {
        "op": "sell_items",
        "prices": [
            {"itemId": BROKEN, "adena": 50},
            {"itemId": GOLEM, "adena": 1000},
        ],
        "bonusMin": 10,
        "bonusAdena": 1183,
    }
    parts = [GOLEM, BROKEN]
    dialogs = [
        d(ROLENTO, "talk", "30437-01.htm", when="absent", maxLevel=32),
        d(ROLENTO, "talk", "30437-02.htm", when="absent", minLevel=33),
        d(
            ROLENTO,
            "event",
            "30437-03.htm",
            when="absent",
            event="30437-03",
            minLevel=33,
            ops=START + [cond(1)],
        ),
        d(ROLENTO, "event", "30437-06.html", when="started", event="30437-06", ops=[{"op": "complete"}]),
        d(ROLENTO, "event", "30437-07.html", when="started", event="30437-07"),
        d(ROLENTO, "talk", "30437-05.html", when="started", hasAny=parts, ops=[sell]),
        d(ROLENTO, "talk", "30437-04.html", when="started", lacksItems=parts),
    ]
    for mob, (golem_ch, broken_ch) in mobs.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(GOLEM, golem_ch), drop(BROKEN, broken_ch)],
            )
        )
    return {
        "id": Q,
        "name": "Curiosity Of A Dwarf",
        "repeatable": True,
        "abortable": True,
        "questItems": parts,
        "dialogs": dialogs,
    }


def q338():
    Q, d = 338, html_loader("Q00338_AlligatorHunter", 338)
    ENVERUN = 30892
    LEATHER = 4337
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": LEATHER, "adena": 60}],
        "bonusMin": 10,
        "bonusAdena": 3430,
    }
    dialogs = [
        d(ENVERUN, "talk", "30892-01.htm", when="absent", maxLevel=39),
        d(ENVERUN, "talk", "30892-02.htm", when="absent", minLevel=40),
        d(
            ENVERUN,
            "event",
            "30892-03.htm",
            when="absent",
            event="30892-03",
            minLevel=40,
            ops=START + [cond(1)],
        ),
        d(ENVERUN, "talk", "30892-04.html", when="started"),
        # Sell is an event (Mobius onAdvEvent 30892-06)
        d(
            ENVERUN,
            "event",
            "30892-06.html",
            when="started",
            event="30892-06",
            hasItem=LEATHER,
            ops=[sell],
        ),
        d(
            ENVERUN,
            "event",
            "30892-05.html",
            when="started",
            event="30892-06",
            lacksItems=[LEATHER],
        ),
        d(ENVERUN, "event", "30892-10.html", when="started", event="30892-10", ops=[{"op": "complete"}]),
        d(ENVERUN, "event", "30892-07.html", when="started", event="30892-07"),
        d(ENVERUN, "event", "30892-08.html", when="started", event="30892-08"),
        d(ENVERUN, "event", "30892-09.html", when="started", event="30892-09"),
        # Always 1 leather + 19% second
        d(
            20135,
            "kill",
            None,
            when="started",
            raw="",
            ops=[drop(LEATHER, 1.0), drop(LEATHER, 0.19)],
        ),
    ]
    return {
        "id": Q,
        "name": "Alligator Hunter",
        "repeatable": True,
        "abortable": True,
        "questItems": [LEATHER],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q329(), q338()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
