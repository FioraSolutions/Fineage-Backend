#!/usr/bin/env python3
"""Hand-parity Mobius Q298 / Q300 (batch ~25–34 lizard hunts).

  Q298 Lizardmen's Conspiracy — Praga → Rohmer, dual gem collect
  Q300 Hunting Leto Lizardman — Rath, 60 bracelets; reward → most common (adena 50%)

After: python3 scripts/gen_quests_25_34_lizards.py
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


def q298():
    Q, d = 298, html_loader("Q00298_LizardmensConspiracy", 298)
    PRAGA, ROHMER = 30333, 30344
    REPORT, GEM, RED = 7182, 7183, 7184
    gems = {
        20922: (GEM, 0.49),
        20923: (GEM, 0.70),
        20924: (GEM, 0.75),
        20926: (RED, 0.54),
        20927: (RED, 0.64),
    }
    dialogs = [
        d(PRAGA, "talk", "30333-02.htm", when="absent", maxLevel=24),
        d(PRAGA, "talk", "30333-01.htm", when="absent", minLevel=25),
        d(
            PRAGA,
            "event",
            "30333-03.htm",
            when="absent",
            event="30333-03",
            minLevel=25,
            ops=START + [cond(1), give(REPORT)],
        ),
        d(PRAGA, "talk", "30333-04.html", when="started", hasItem=REPORT),
        d(ROHMER, "talk", "30344-01.html", cond=1),
        d(
            ROHMER,
            "event",
            "30344-04.html",
            event="30344-04",
            cond=1,
            hasItem=REPORT,
            ops=[take(REPORT, 1), cond(2)],
        ),
        # Dual collect ready → bump cond 3 on talk
        d(
            ROHMER,
            "talk",
            "30344-03.html",
            cond=2,
            atLeast={GEM: 50, RED: 50},
            ops=[cond(3)],
        ),
        d(ROHMER, "talk", "30344-02.html", cond=2),
        d(ROHMER, "talk", "30344-03.html", cond=3),
        d(
            ROHMER,
            "event",
            "30344-06.html",
            event="30344-06",
            cond=3,
            ops=[exp_sp(0, 42000), {"op": "complete"}],
        ),
        d(
            ROHMER,
            "event",
            "30344-06.html",
            event="30344-06",
            cond=2,
            atLeast={GEM: 50, RED: 50},
            ops=[cond(3), exp_sp(0, 42000), {"op": "complete"}],
        ),
        d(ROHMER, "event", "30344-07.html", event="30344-06", when="started"),
    ]
    for mob, (item, ch) in gems.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=2,
                party=True,
                raw="",
                ops=[drop(item, ch, limit=50)],
            )
        )
    return {
        "id": Q,
        "name": "Lizardmen's Conspiracy",
        "repeatable": True,
        "abortable": True,
        "questItems": [REPORT, GEM, RED],
        "dialogs": dialogs,
    }


def q300():
    Q, d = 300, html_loader("Q00300_HuntingLetoLizardman", 300)
    RATH = 30126
    BRACELET = 7139
    # chance out of 1000
    mobs = {20577: 0.36, 20578: 0.39, 20579: 0.41, 20580: 0.79, 20582: 0.89}
    # Random reward → most common Mobius branch (adena 50%)
    finish = [
        take(BRACELET, 60),
        {"op": "give_adena", "count": 30000},
        {"op": "complete"},
    ]
    dialogs = [
        d(RATH, "talk", "30126-02.htm", when="absent", maxLevel=33),
        d(RATH, "talk", "30126-01.htm", when="absent", minLevel=34),
        d(
            RATH,
            "event",
            "30126-03.htm",
            when="absent",
            event="30126-03",
            minLevel=34,
            ops=START + [cond(1)],
        ),
        d(RATH, "talk", "30126-04.html", cond=1),
        d(
            RATH,
            "talk",
            "30126-05.html",
            cond=2,
            atLeast={BRACELET: 60},
        ),
        d(
            RATH,
            "event",
            "30126-06.html",
            event="30126-06",
            cond=2,
            atLeast={BRACELET: 60},
            ops=finish,
        ),
        d(RATH, "event", "30126-07.html", event="30126-06", when="started"),
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
                ops=[drop(BRACELET, ch, limit=60, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Hunting Leto Lizardman",
        "repeatable": True,
        "abortable": True,
        "questItems": [BRACELET],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q298(), q300()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
