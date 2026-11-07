#!/usr/bin/env python3
"""Hand-parity Mobius Q360 / Q369 (batch ~52–60 plunder + jewels).

  Q360 Plunder Their Supplies — Coleman; piece→recipe convert on talk (≥5)
  Q369 Collector of Jewels — Nell; memo phases 50×2 then 200×2

After: python3 scripts/gen_quests_52_60_plunder_jewels.py
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
setv = lambda k, v: {"op": "set", "key": k, "value": str(v)}


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


def q360():
    Q, d = 360, html_loader("Q00360_PlunderTheirSupplies", 360)
    COLEMAN = 30873
    RECIPE, PIECE, SUPPLY = 5870, 5871, 5872
    mobs = {20666: 0.50, 20669: 0.75}
    sell_both = {
        "op": "sell_items",
        "prices": [
            {"itemId": SUPPLY, "adena": 100},
            {"itemId": RECIPE, "adena": 6000},
        ],
        "bonusMin": 1,
        "bonusAdena": 6000,
    }
    sell_supply = {
        "op": "sell_items",
        "prices": [{"itemId": SUPPLY, "adena": 100}],
        "bonusMin": 1,
        "bonusAdena": 6000,
    }
    sell_recipe = {
        "op": "sell_items",
        "prices": [{"itemId": RECIPE, "adena": 6000}],
        "bonusMin": 0,
        "bonusAdena": 0,
    }
    dialogs = [
        d(COLEMAN, "talk", "30873-01.html", when="absent", maxLevel=51),
        d(COLEMAN, "talk", "30873-02.htm", when="absent", minLevel=52),
        d(COLEMAN, "event", "30873-03.htm", when="absent", event="30873-03", minLevel=52),
        d(
            COLEMAN,
            "event",
            "30873-04.htm",
            when="absent",
            event="30873-04",
            minLevel=52,
            ops=START + [cond(1)],
        ),
        d(COLEMAN, "event", "30873-09.html", when="started", event="30873-09"),
        # Mobius exitQuest(false) — one-shot
        d(COLEMAN, "event", "30873-10.html", when="started", event="30873-10", ops=[{"op": "complete"}]),
        # Sell paths; ≥5 pieces → recipe first (Mobius converts on the 5th kill)
        d(
            COLEMAN,
            "talk",
            "30873-07.html",
            when="started",
            hasItem=SUPPLY,
            hasAny=[RECIPE],
            ops=[sell_both],
        ),
        d(
            COLEMAN,
            "talk",
            "30873-07.html",
            when="started",
            hasItem=SUPPLY,
            atLeast={PIECE: 5},
            ops=[take(PIECE, 5), give(RECIPE), sell_both],
        ),
        d(
            COLEMAN,
            "talk",
            "30873-06.html",
            when="started",
            hasItem=SUPPLY,
            ops=[sell_supply],
        ),
        d(
            COLEMAN,
            "talk",
            "30873-08.html",
            when="started",
            hasItem=RECIPE,
            ops=[sell_recipe],
        ),
        d(
            COLEMAN,
            "talk",
            "30873-08.html",
            when="started",
            atLeast={PIECE: 5},
            ops=[take(PIECE, 5), give(RECIPE), sell_recipe],
        ),
        d(COLEMAN, "talk", "30873-05.html", when="started"),
    ]
    for mob, ch in mobs.items():
        # Mobius: killer in range (not getRandomPartyMember). Avoid party+piece limit
        # (PartyKillApplies skips when any capped drop is full).
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                raw="",
                ops=[drop(SUPPLY, ch), drop(PIECE, 0.1, limit=5)],
            )
        )
    return {
        "id": Q,
        "name": "Plunder Supplies",
        "repeatable": False,
        "abortable": True,
        "questItems": [SUPPLY, PIECE, RECIPE],
        "dialogs": dialogs,
    }


def q369():
    Q, d = 369, html_loader("Q00369_CollectorOfJewels", 369)
    NELL = 30376
    FLARE, FREEZE = 5882, 5883
    # (item, chance, count)
    mobs = {
        20609: (FLARE, 0.75, 1),
        20612: (FLARE, 0.91, 1),
        20749: (FLARE, 1.0, 2),
        20616: (FREEZE, 0.81, 1),
        20619: (FREEZE, 0.87, 1),
        20747: (FREEZE, 1.0, 2),
    }
    m = lambda v: dict(whenVar="memo", whenValue=str(v))
    dialogs = [
        d(NELL, "talk", "30376-03.html", when="absent", maxLevel=24),
        d(NELL, "talk", "30376-01.htm", when="absent", minLevel=25),
        d(
            NELL,
            "event",
            "30376-02.htm",
            when="absent",
            event="30376-02",
            minLevel=25,
            ops=START + [cond(1), setv("memo", 1)],
        ),
        d(NELL, "event", "30376-05.html", when="started", event="30376-05"),
        d(NELL, "event", "30376-07.html", when="started", event="30376-07", ops=[{"op": "complete"}]),
        # Phase 1 complete: 50+50
        d(
            NELL,
            "talk",
            "30376-04.html",
            when="started",
            atLeast={FLARE: 50, FREEZE: 50},
            ops=[take(FLARE, 50), take(FREEZE, 50), adena(31810), setv("memo", 2), cond(2)],
            **m(1),
        ),
        d(NELL, "talk", "30376-08.html", when="started", **m(1)),
        d(NELL, "talk", "30376-09.html", when="started", **m(2)),
        d(
            NELL,
            "event",
            "30376-06.html",
            when="started",
            event="30376-06",
            ops=[setv("memo", 3), cond(3)],
            **m(2),
        ),
        # Phase 2 complete: 200+200
        d(
            NELL,
            "talk",
            "30376-10.html",
            when="started",
            atLeast={FLARE: 200, FREEZE: 200},
            ops=[take(FLARE, 200), take(FREEZE, 200), adena(84415), {"op": "complete"}],
            **m(3),
        ),
        d(NELL, "talk", "30376-11.html", when="started", **m(3)),
    ]
    for mob, (item, ch, cnt) in mobs.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(item, ch, limit=50, count=cnt)],
                **m(1),
            )
        )
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(item, ch, limit=200, count=cnt)],
                **m(3),
            )
        )
    return {
        "id": Q,
        "name": "Collector of Jewels",
        "repeatable": True,
        "abortable": True,
        "questItems": [FLARE, FREEZE],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q360(), q369()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
