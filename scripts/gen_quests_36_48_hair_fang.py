#!/usr/bin/env python3
"""Hand-parity Mobius Q366 / Q368 (batch ~36–48 hair/fang sells).

  Q366 Silver Haired Shaman — Dieter, Sairon hair sell
  Q368 Trespassing into the Holy Ground — Restina, stakato fang sell tiers

After: python3 scripts/gen_quests_36_48_hair_fang.py
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


def q366():
    Q, d = 366, html_loader("Q00366_SilverHairedShaman", 366)
    DIETER = 30111
    HAIR = 5874
    mobs = {20986: 0.80, 20987: 0.73, 20988: 0.80}
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": HAIR, "adena": 500}],
        "bonusMin": 1,
        "bonusAdena": 29000,
    }
    dialogs = [
        d(DIETER, "talk", "30111-03.html", when="absent", maxLevel=47),
        d(DIETER, "talk", "30111-01.htm", when="absent", minLevel=48),
        d(
            DIETER,
            "event",
            "30111-02.htm",
            when="absent",
            event="30111-02",
            minLevel=48,
            ops=START + [cond(1)],
        ),
        d(DIETER, "event", "30111-05.html", when="started", event="30111-05", ops=[{"op": "complete"}]),
        d(DIETER, "event", "30111-06.html", when="started", event="30111-06"),
        d(DIETER, "talk", "30111-04.html", when="started", hasItem=HAIR, ops=[sell]),
        d(DIETER, "talk", "30111-07.html", when="started", lacksItems=[HAIR]),
    ]
    for mob, ch in mobs.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(HAIR, ch)])
        )
    return {
        "id": Q,
        "name": "Silver Haired Shaman",
        "repeatable": True,
        "abortable": True,
        "questItems": [HAIR],
        "dialogs": dialogs,
    }


def q368():
    Q, d = 368, html_loader("Q00368_TrespassingIntoTheHolyGround", 368)
    RESTINA = 30926
    FANG = 5881
    mobs = {20794: 0.60, 20795: 0.57, 20796: 0.61, 20797: 0.93}
    sell_hi = {
        "op": "sell_items",
        "prices": [{"itemId": FANG, "adena": 250}],
        "bonusMin": 10,
        "bonusAdena": 9450,
    }
    sell_lo = {
        "op": "sell_items",
        "prices": [{"itemId": FANG, "adena": 250}],
        "bonusMin": 1,
        "bonusAdena": 2000,
    }
    dialogs = [
        d(RESTINA, "talk", "30926-03.html", when="absent", maxLevel=35),
        d(RESTINA, "talk", "30926-01.htm", when="absent", minLevel=36),
        d(
            RESTINA,
            "event",
            "30926-02.htm",
            when="absent",
            event="30926-02",
            minLevel=36,
            ops=START + [cond(1)],
        ),
        d(RESTINA, "event", "30926-05.html", when="started", event="30926-05", ops=[{"op": "complete"}]),
        d(RESTINA, "event", "30926-06.html", when="started", event="30926-06"),
        d(
            RESTINA,
            "talk",
            "30926-04.html",
            when="started",
            atLeast={FANG: 10},
            ops=[sell_hi],
        ),
        d(
            RESTINA,
            "talk",
            "30926-04.html",
            when="started",
            hasItem=FANG,
            ops=[sell_lo],
        ),
        d(RESTINA, "talk", "30926-07.html", when="started", lacksItems=[FANG]),
    ]
    for mob, ch in mobs.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(FANG, ch)])
        )
    return {
        "id": Q,
        "name": "Trespassing into the Holy Ground",
        "repeatable": True,
        "abortable": True,
        "questItems": [FANG],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q366(), q368()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
