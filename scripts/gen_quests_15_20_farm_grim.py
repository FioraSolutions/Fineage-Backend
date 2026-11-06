#!/usr/bin/env python3
"""Hand-parity Mobius Q151 / Q259 / Q325 (batch 15–20 deliveries + farm/grim).

  Q151 Cure for Fever — Elias → Yohanes medicine (fix dialog order)
  Q259 Request from the Farm Owner — Edmond sell + Marius exchanges
  Q325 Grim Collector — Samed parts sell + Varsak assemble

Simplifications:
  Q325: exclusive Mobius drop bands → independent rolls at band widths.
  Q325 bulk sell: omit flat +543 when any complete skeleton present
        (341/ea still paid; dedicated 30434-09 keeps +543).

After: python3 scripts/gen_quests_15_20_farm_grim.py
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

give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
exp_sp = lambda e, s: {"op": "give_exp_sp", "exp": e, "sp": s}
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


def q151():
    Q, d = 151, html_loader("Q00151_CureForFever", 151)
    ELLIAS, YOHANES = 30050, 30032
    SHIELD, SAC, MED = 102, 703, 704
    MOBS = (20103, 20106, 20108)
    dialogs = [
        d(ELLIAS, "talk", "30050-01.htm", when="absent", maxLevel=14),
        d(ELLIAS, "talk", "30050-02.htm", when="absent", minLevel=15),
        d(
            ELLIAS,
            "event",
            "30050-03.htm",
            when="absent",
            event="30050-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(
            ELLIAS,
            "talk",
            "30050-06.html",
            cond=3,
            hasItem=MED,
            ops=[give(SHIELD), exp_sp(13106, 613), {"op": "complete"}],
        ),
        d(ELLIAS, "talk", "30050-05.html", cond=2, hasItem=SAC),
        d(ELLIAS, "talk", "30050-04.html", cond=1),
        d(
            YOHANES,
            "talk",
            "30032-01.html",
            cond=2,
            hasItem=SAC,
            ops=[take(SAC), give(MED), cond(3)],
        ),
        d(YOHANES, "talk", "30032-02.html", cond=3, hasItem=MED),
    ]
    for mob in MOBS:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                lacksItems=[SAC],
                raw="",
                ops=[drop(SAC, 0.2, limit=1, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Cure for Fever",
        "repeatable": False,
        "abortable": True,
        "questItems": [SAC, MED],
        "dialogs": dialogs,
    }


def q259():
    Q, d = 259, html_loader("Q00259_RequestFromTheFarmOwner", 259)
    EDMOND, MARIUS = 30497, 30405
    SKIN = 1495
    MOBS = (20103, 20106, 20108)
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": SKIN, "adena": 25}],
        "bonusMin": 10,
        "bonusAdena": 250,
    }
    # Java ItemHolder counts (HTML text says "fifty" arrows — Java gives 250).
    swaps = {
        "30405-04": (1061, 2),
        "30405-05": (17, 250),
        "30405-05a": (1835, 60),
        "30405-05c": (2509, 30),
    }
    dialogs = [
        d(EDMOND, "talk", "30497-01.html", when="absent", maxLevel=14),
        d(EDMOND, "talk", "30497-02.htm", when="absent", minLevel=15),
        d(
            EDMOND,
            "event",
            "30497-03.html",
            when="absent",
            event="30497-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(EDMOND, "talk", "30497-05.html", when="started", hasItem=SKIN, ops=[sell]),
        d(EDMOND, "talk", "30497-04.html", when="started", lacksItems=[SKIN]),
        d(EDMOND, "event", "30497-06.html", when="started", event="30497-06", ops=[{"op": "complete"}]),
        d(EDMOND, "event", "30497-07.html", when="started", event="30497-07"),
        d(MARIUS, "talk", "30405-02.html", when="started", atLeast={SKIN: 10}),
        d(MARIUS, "talk", "30405-01.html", when="started"),
        d(MARIUS, "event", "30405-03.html", when="started", event="30405-03"),
        d(MARIUS, "event", "30405-05b.html", when="started", event="30405-05b"),
        d(MARIUS, "event", "30405-05d.html", when="started", event="30405-05d"),
        d(MARIUS, "event", "30405-06.html", when="started", event="30405-06", atLeast={SKIN: 10}),
        d(MARIUS, "event", "30405-07.html", when="started", event="30405-06"),
    ]
    for ev, (iid, cnt) in swaps.items():
        dialogs.append(
            d(
                MARIUS,
                "event",
                f"{ev}.html",
                when="started",
                event=ev,
                atLeast={SKIN: 10},
                ops=[take(SKIN, 10), give(iid, cnt)],
            )
        )
    for mob in MOBS:
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(SKIN, 1.0)]))
    return {
        "id": Q,
        "name": "Request from the Farm Owner",
        "repeatable": True,
        "abortable": True,
        "questItems": [SKIN],
        "dialogs": dialogs,
    }


def q325():
    Q, d = 325, html_loader("Q00325_GrimCollector", 325)
    CURTIZ, VARSAK, SAMED = 30336, 30342, 30434
    DIAG = 1349
    HEAD, HEART, LIVER = 1350, 1351, 1352
    SKULL, RIB, SPINE, ARM, THIGH = 1353, 1354, 1355, 1356, 1357
    COMPLETE = 1358
    PARTS = [HEAD, HEART, LIVER, SKULL, RIB, SPINE, ARM, THIGH, COMPLETE]
    BONES = [SPINE, ARM, SKULL, RIB, THIGH]
    # Mobius exclusive bands → independent band-width chances.
    drops = {
        20026: [(HEAD, 0.30), (HEART, 0.20), (LIVER, 0.25)],
        20029: [(HEAD, 0.30), (HEART, 0.22), (LIVER, 0.25)],
        20035: [(SKULL, 0.05), (RIB, 0.10), (SPINE, 0.14), (THIGH, 0.50)],
        20042: [(SKULL, 0.06), (RIB, 0.13), (ARM, 0.50), (THIGH, 0.17)],
        20045: [(SKULL, 0.09), (SPINE, 0.50), (ARM, 0.18), (THIGH, 0.20)],
        20051: [(SKULL, 0.09), (RIB, 0.50), (SPINE, 0.20), (ARM, 0.21)],
        20457: [(HEAD, 0.40), (HEART, 0.20), (LIVER, 0.20)],
        20458: [(HEAD, 0.40), (HEART, 0.30), (LIVER, 0.30)],
        20514: [(SKULL, 0.06), (RIB, 0.15), (SPINE, 0.09), (ARM, 0.01), (THIGH, 0.33)],
        20515: [(SKULL, 0.05), (RIB, 0.15), (SPINE, 0.11), (ARM, 0.02), (THIGH, 0.36)],
    }
    sell = {
        "op": "sell_items",
        "prices": [
            {"itemId": HEAD, "adena": 30},
            {"itemId": HEART, "adena": 20},
            {"itemId": LIVER, "adena": 20},
            {"itemId": SKULL, "adena": 100},
            {"itemId": RIB, "adena": 40},
            {"itemId": SPINE, "adena": 14},
            {"itemId": ARM, "adena": 14},
            {"itemId": THIGH, "adena": 14},
            {"itemId": COMPLETE, "adena": 341},
        ],
        "bonusMin": 10,
        "bonusAdena": 1629,
    }
    sell_complete = {
        "op": "sell_items",
        "prices": [{"itemId": COMPLETE, "adena": 341}],
        "bonusMin": 1,
        "bonusAdena": 543,
    }
    dialogs = [
        d(CURTIZ, "talk", "30336-01.htm", when="absent", maxLevel=14),
        d(CURTIZ, "talk", "30336-02.htm", when="absent", minLevel=15),
        d(
            CURTIZ,
            "event",
            "30336-03.htm",
            when="absent",
            event="30336-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(CURTIZ, "talk", "30336-05.html", when="started", hasItem=DIAG),
        d(CURTIZ, "talk", "30336-04.html", when="started", lacksItems=[DIAG]),
        d(VARSAK, "talk", "30342-01.html", when="started", hasItem=DIAG),
        d(
            VARSAK,
            "event",
            "30342-03.html",
            when="started",
            event="assembleSkeleton",
            hasItem=DIAG,
            hasItems=BONES,
            ops=[take(SPINE), take(ARM), take(SKULL), take(RIB), take(THIGH), drop(COMPLETE, 0.8)],
        ),
        d(
            VARSAK,
            "event",
            "30342-02.html",
            when="started",
            event="assembleSkeleton",
            hasItem=DIAG,
        ),
        d(SAMED, "talk", "30434-01.html", when="started", lacksItems=[DIAG]),
        d(SAMED, "talk", "30434-08.html", when="started", hasItem=DIAG, hasItems=[COMPLETE]),
        d(SAMED, "talk", "30434-05.html", when="started", hasItem=DIAG, hasAny=PARTS),
        d(SAMED, "talk", "30434-04.html", when="started", hasItem=DIAG),
        d(SAMED, "event", "30434-02.htm", when="started", event="30434-02", lacksItems=[DIAG]),
        d(
            SAMED,
            "event",
            "30434-03.html",
            when="started",
            event="30434-03",
            lacksItems=[DIAG],
            ops=[give(DIAG)],
        ),
        d(
            SAMED,
            "event",
            "30434-07.html",
            when="started",
            event="30434-07",
            hasAny=PARTS,
            ops=[sell],
        ),
        d(SAMED, "event", "30434-07.html", when="started", event="30434-07", lacksItems=PARTS),
        d(
            SAMED,
            "event",
            "30434-06.html",
            when="started",
            event="30434-06",
            hasAny=PARTS,
            ops=[sell, {"op": "complete"}],
        ),
        d(
            SAMED,
            "event",
            "30434-06.html",
            when="started",
            event="30434-06",
            lacksItems=PARTS,
            ops=[{"op": "complete"}],
        ),
        d(
            SAMED,
            "event",
            "30434-09.html",
            when="started",
            event="30434-09",
            hasItem=COMPLETE,
            ops=[sell_complete],
        ),
        d(SAMED, "event", "30434-09.html", when="started", event="30434-09", lacksItems=[COMPLETE]),
    ]
    for mob, table in drops.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                hasItem=DIAG,
                raw="",
                ops=[drop(i, ch) for i, ch in table],
            )
        )
    return {
        "id": Q,
        "name": "Grim Collector",
        "repeatable": True,
        "abortable": True,
        "questItems": [DIAG] + PARTS,
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q151(), q259(), q325()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
