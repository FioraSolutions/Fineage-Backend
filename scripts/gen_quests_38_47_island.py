#!/usr/bin/env python3
"""Hand-parity Mobius Q354 / Q357 (batch ~38–47 island / warehouse).

  Q354 Conquest of Alligator Island — Kluck, teeth sell tiers + map pieces
  Q357 Warehouse Keeper's Ambition — Silva, jade crystal sell tiers

After: python3 scripts/gen_quests_38_47_island.py
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


def q354():
    Q, d = 354, html_loader("Q00354_ConquestOfAlligatorIsland", 354)
    KLUCK = 30895
    TOOTH, PIECE, MAP = 5863, 5864, 5915
    mob1 = {20804: 0.84, 20805: 0.91, 20806: 0.88, 20807: 0.92}
    mob2 = {20808: 0.14, 20991: 0.69}  # always 1 + chance for 2nd
    sell_hi = {
        "op": "sell_items",
        "prices": [{"itemId": TOOTH, "adena": 220}],
        "bonusMin": 100,
        "bonusAdena": 10700,
    }
    sell_lo = {
        "op": "sell_items",
        "prices": [{"itemId": TOOTH, "adena": 220}],
        "bonusMin": 1,
        "bonusAdena": 3100,
    }
    dialogs = [
        d(KLUCK, "talk", "30895-03.html", when="absent", maxLevel=37),
        d(KLUCK, "talk", "30895-01.htm", when="absent", minLevel=38),
        d(
            KLUCK,
            "event",
            "30895-02.html",
            when="absent",
            event="30895-02",
            minLevel=38,
            ops=START + [cond(1)],
        ),
        d(KLUCK, "event", "30895-04.html", when="started", event="30895-04"),
        d(KLUCK, "event", "30895-05.html", when="started", event="30895-05"),
        d(KLUCK, "event", "30895-09.html", when="started", event="30895-09"),
        d(KLUCK, "talk", "30895-11.html", when="started", hasItem=PIECE),
        d(KLUCK, "talk", "30895-04.html", when="started", lacksItems=[PIECE]),
        # ADENA sell tiers (first-match: ≥100 then any)
        d(
            KLUCK,
            "event",
            "30895-06.html",
            when="started",
            event="ADENA",
            atLeast={TOOTH: 100},
            ops=[sell_hi],
        ),
        d(
            KLUCK,
            "event",
            "30895-07.html",
            when="started",
            event="ADENA",
            hasItem=TOOTH,
            ops=[sell_lo],
        ),
        d(
            KLUCK,
            "event",
            "30895-08.html",
            when="started",
            event="ADENA",
            lacksItems=[TOOTH],
        ),
        # Map piece exchange
        d(
            KLUCK,
            "event",
            "30895-13.html",
            when="started",
            event="REWARD",
            atLeast={PIECE: 10},
            ops=[
                {"op": "take_item", "itemId": PIECE, "count": 10},
                {"op": "give_item", "itemId": MAP, "count": 1},
            ],
        ),
        d(
            KLUCK,
            "event",
            "30895-12.html",
            when="started",
            event="REWARD",
            hasItem=PIECE,
        ),
        d(KLUCK, "event", "30895-10.html", when="started", event="30895-10", ops=[{"op": "complete"}]),
    ]
    for mob, ch in mob1.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(TOOTH, ch), drop(PIECE, 0.1)],
            )
        )
    for mob, ch2 in mob2.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(TOOTH, 1.0), drop(TOOTH, ch2), drop(PIECE, 0.1)],
            )
        )
    return {
        "id": Q,
        "name": "Conquest of Alligator Island",
        "repeatable": True,
        "abortable": True,
        "questItems": [TOOTH, PIECE],
        "dialogs": dialogs,
    }


def q357():
    Q, d = 357, html_loader("Q00357_WarehouseKeepersAmbition", 357)
    SILVA = 30686
    JADE = 5867
    drops = {20594: 0.577, 20595: 0.6, 20596: 0.638, 20597: 0.062}
    sell_hi = {
        "op": "sell_items",
        "prices": [{"itemId": JADE, "adena": 425}],
        "bonusMin": 100,
        "bonusAdena": 40500,
    }
    sell_lo = {
        "op": "sell_items",
        "prices": [{"itemId": JADE, "adena": 425}],
        "bonusMin": 1,
        "bonusAdena": 13500,
    }
    sell_quit_hi = sell_hi
    sell_quit_lo = {
        "op": "sell_items",
        "prices": [{"itemId": JADE, "adena": 425}],
        "bonusMin": 0,
        "bonusAdena": 0,
    }
    dialogs = [
        d(SILVA, "talk", "30686-01.html", when="absent", maxLevel=46),
        d(SILVA, "talk", "30686-02.htm", when="absent", minLevel=47),
        # Info event "30686-01.htm" on disk is 30686-01.html (Mobius naming quirk).
        d(SILVA, "event", "30686-01.html", when="absent", event="30686-01"),
        d(SILVA, "event", "30686-03.htm", when="absent", event="30686-03", minLevel=47),
        d(SILVA, "event", "30686-04.htm", when="absent", event="30686-04", minLevel=47),
        d(
            SILVA,
            "event",
            "30686-05.htm",
            when="absent",
            event="30686-05",
            minLevel=47,
            ops=START + [cond(1)],
        ),
        d(SILVA, "talk", "30686-07.html", when="started", hasItem=JADE),
        d(SILVA, "talk", "30686-06.html", when="started", lacksItems=[JADE]),
        d(SILVA, "event", "30686-10.html", when="started", event="30686-10"),
        # Sell stay
        d(
            SILVA,
            "event",
            "30686-09.html",
            when="started",
            event="30686-09",
            atLeast={JADE: 100},
            ops=[sell_hi],
        ),
        d(
            SILVA,
            "event",
            "30686-08.html",
            when="started",
            event="30686-09",
            hasItem=JADE,
            ops=[sell_lo],
        ),
        # Quit: sell (hi bonus only if ≥100) then complete
        d(
            SILVA,
            "event",
            "30686-11.html",
            when="started",
            event="30686-11",
            atLeast={JADE: 100},
            ops=[sell_quit_hi, {"op": "complete"}],
        ),
        d(
            SILVA,
            "event",
            "30686-11.html",
            when="started",
            event="30686-11",
            hasItem=JADE,
            ops=[sell_quit_lo, {"op": "complete"}],
        ),
        d(
            SILVA,
            "event",
            "30686-11.html",
            when="started",
            event="30686-11",
            lacksItems=[JADE],
            ops=[{"op": "complete"}],
        ),
    ]
    for mob, ch in drops.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(JADE, ch)])
        )
    return {
        "id": Q,
        "name": "Warehouse Keeper's Ambition",
        "repeatable": True,
        "abortable": True,
        "questItems": [JADE],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q354(), q357()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
