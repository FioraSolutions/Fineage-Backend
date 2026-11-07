#!/usr/bin/env python3
"""Hand-parity Mobius Q632 / Q633 / Q601 / Q602 (batch ~63–71).

  Q632 Necromancer's Request — hearts×200 sell loop; brains accumulate
  Q633 In the Forgotten Village — ribs×200 turn-in loop; livers accumulate
  Q601 Watching Eyes — proofs×100; reward → no-item branch (adena 230k)
  Q602 Shadow of Light — eyes×100; reward → no-item branch (adena+exp)

After: python3 scripts/gen_quests_63_71_necro_argos.py
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
exp_sp = lambda e, s: {"op": "give_exp_sp", "exp": e, "sp": s}


def drop(i, chance, limit=0, at=None, count=1):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


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


def q632():
    Q, d = 632, html_loader("Q00632_NecromancersRequest", 632)
    WIZ = 31522
    HEART, BRAIN = 7542, 7543
    brains = {
        21547: 0.565, 21548: 0.484, 21549: 0.585, 21550: 0.597, 21551: 0.673,
        21552: 0.637, 21555: 0.575, 21556: 0.560, 21562: 0.631, 21571: 0.758,
        21576: 0.647, 21577: 0.625, 21579: 0.766,
    }
    hearts = {
        21568: 0.452, 21569: 0.484, 21573: 0.499, 21582: 0.522, 21585: 0.413,
        21586: 0.496, 21587: 0.519, 21588: 0.428, 21589: 0.439, 21590: 0.428,
        21591: 0.502, 21592: 0.370, 21593: 0.592, 21594: 0.554, 21595: 0.392,
    }
    dialogs = [
        d(WIZ, "talk", "31522-103.htm", when="absent", maxLevel=62),
        d(WIZ, "talk", "31522-101.htm", when="absent", minLevel=63),
        d(
            WIZ,
            "event",
            "31522-104.htm",
            when="absent",
            event="31522-104",
            minLevel=63,
            ops=START + [cond(1), setv("memo", 11)],
        ),
        d(WIZ, "event", "31522-201.html", when="started", event="31522-201"),
        d(
            WIZ,
            "event",
            "31522-202.html",
            when="started",
            event="31522-202",
            atLeast={HEART: 200},
            ops=[take(HEART, 200), adena(120000), setv("memo", 11), cond(1)],
        ),
        d(WIZ, "event", "31522-203.html", when="started", event="31522-202"),
        d(
            WIZ,
            "event",
            "31522-204.html",
            when="started",
            event="31522-204",
            ops=[{"op": "complete"}],
        ),
        d(
            WIZ,
            "talk",
            "31522-105.html",
            when="started",
            atLeast={HEART: 200},
            ops=[setv("memo", 12), cond(2)],
        ),
        d(WIZ, "talk", "31522-106.html", when="started"),
    ]
    for mob, ch in brains.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(BRAIN, ch)])
        )
    for mob, ch in hearts.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                raw="",
                ops=[drop(HEART, ch, limit=200, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Necromancer's Request",
        "repeatable": True,
        "abortable": True,
        "questItems": [HEART, BRAIN],
        "dialogs": dialogs,
    }


def q633():
    Q, d = 633, html_loader("Q00633_InTheForgottenVillage", 633)
    MINA = 31388
    RIB, LIVER = 7544, 7545
    drops = {
        21553: (LIVER, 0.417), 21554: (LIVER, 0.417),
        21557: (RIB, 0.394), 21558: (RIB, 0.394), 21559: (RIB, 0.436), 21560: (RIB, 0.430),
        21561: (LIVER, 0.538),
        21563: (RIB, 0.436), 21564: (RIB, 0.414), 21565: (RIB, 0.420), 21566: (RIB, 0.460),
        21567: (RIB, 0.549),
        21570: (LIVER, 0.508),
        21572: (RIB, 0.465), 21574: (RIB, 0.586), 21575: (RIB, 0.329),
        21578: (LIVER, 0.649),
        21580: (RIB, 0.462), 21581: (RIB, 0.505), 21583: (RIB, 0.475), 21584: (RIB, 0.475),
        21596: (RIB, 0.543),
        21597: (LIVER, 0.510), 21598: (LIVER, 0.572),
        21599: (RIB, 0.580),
        21600: (LIVER, 0.561),
        21601: (RIB, 0.677),
    }
    dialogs = [
        d(MINA, "talk", "31388-02.htm", when="absent", maxLevel=64),
        d(MINA, "talk", "31388-01.htm", when="absent", minLevel=65),
        d(
            MINA,
            "event",
            "31388-03.htm",
            when="absent",
            event="31388-03",
            minLevel=65,
            ops=START + [cond(1)],
        ),
        d(MINA, "event", "31388-04.html", when="started", event="31388-04"),
        d(MINA, "event", "31388-05.html", when="started", event="31388-05"),
        d(MINA, "event", "31388-06.html", when="started", event="31388-06"),
        d(
            MINA,
            "event",
            "31388-07.html",
            when="started",
            event="31388-07",
            cond=2,
            atLeast={RIB: 200},
            ops=[take(RIB, 200), adena(25000), exp_sp(305235, 0), cond(1)],
        ),
        d(MINA, "event", "31388-08.html", when="started", event="31388-07"),
        d(MINA, "event", "31388-09.html", when="started", event="31388-09", ops=[{"op": "complete"}]),
        d(MINA, "talk", "31388-04.html", when="started", atLeast={RIB: 200}),
        d(MINA, "talk", "31388-05.html", when="started"),
    ]
    for mob, (item, ch) in drops.items():
        if item == RIB:
            ops = [drop(RIB, ch, limit=200, at=2)]
            gates = dict(cond=1)
        else:
            ops = [drop(LIVER, ch)]
            gates = {}
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=ops, **gates)
        )
    return {
        "id": Q,
        "name": "In The Forgotten Village",
        "repeatable": True,
        "abortable": True,
        "questItems": [RIB, LIVER],
        "dialogs": dialogs,
    }


def q601():
    Q, d = 601, html_loader("Q00601_WatchingEyes", 601)
    EYE = 31683
    PROOF = 7188
    # chance out of 1000
    mobs = {21308: 0.790, 21309: 0.820, 21306: 0.850, 21310: 0.680, 21311: 0.630}
    # Random reward → no-item branch (i=3): 230000 adena (jewelry ids missing in Fineage)
    finish = [take(PROOF, 100), adena(230000), {"op": "complete"}]
    dialogs = [
        d(EYE, "talk", "31683-00.htm", when="absent", maxLevel=70),
        d(EYE, "talk", "31683-01.htm", when="absent", minLevel=71),
        d(
            EYE,
            "event",
            "31683-02.htm",
            when="absent",
            event="31683-02",
            minLevel=71,
            ops=START + [cond(1)],
        ),
        d(EYE, "talk", "31683-03.html", cond=1),
        d(EYE, "talk", "31683-04.html", cond=2),
        d(
            EYE,
            "event",
            "31683-05.html",
            event="31683-05",
            cond=2,
            atLeast={PROOF: 100},
            ops=finish,
        ),
        d(EYE, "event", "31683-06.html", event="31683-05", when="started"),
    ]
    for mob, ch in mobs.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                raw="",
                ops=[drop(PROOF, ch, limit=100, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Watching Eyes",
        "repeatable": True,
        "abortable": True,
        "questItems": [PROOF],
        "dialogs": dialogs,
    }


def q602():
    Q, d = 602, html_loader("Q00602_ShadowOfLight", 602)
    EYE = 31683
    DARK = 7189
    # no-item branch: adena 100000 + exp/sp
    finish = [take(DARK, 100), adena(100000), exp_sp(140000, 11250), {"op": "complete"}]
    dialogs = [
        d(EYE, "talk", "31683-00.htm", when="absent", maxLevel=67),
        d(EYE, "talk", "31683-01.htm", when="absent", minLevel=68),
        d(
            EYE,
            "event",
            "31683-02.htm",
            when="absent",
            event="31683-02",
            minLevel=68,
            ops=START + [cond(1)],
        ),
        d(EYE, "talk", "31683-03.html", cond=1),
        d(EYE, "talk", "31683-04.html", cond=2),
        d(
            EYE,
            "event",
            "31683-05.html",
            event="31683-05",
            cond=2,
            atLeast={DARK: 100},
            ops=finish,
        ),
        d(EYE, "event", "31683-06.html", event="31683-05", when="started"),
        d(21299, "kill", None, cond=1, raw="", ops=[drop(DARK, 0.560, limit=100, at=2)]),
        d(21304, "kill", None, cond=1, raw="", ops=[drop(DARK, 0.800, limit=100, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Shadow Of Light",
        "repeatable": True,
        "abortable": True,
        "questItems": [DARK],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q632(), q633(), q601(), q602()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
