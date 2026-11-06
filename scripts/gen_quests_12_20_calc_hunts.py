#!/usr/bin/env python3
"""Hand-parity Mobius Q169 / Q316 / Q341 / Q347 (batch 12–20).

  Q169 Offspring of Nightmares — Vlasty DE, skulls
  Q316 Destroy Plague Carriers — Ellenia Elf, wererat fangs sell
  Q341 Hunting for Wild Beasts — Pano, bear skins ×20
  Q347 Go Get the Calculator — Brunon guild chain + gemstone hunt

Simplifications:
  Q316: killer-only drops (skip party); chance ≈ always (Mobius 10/N).
  Q347: skip Brunon auto-cond-6 when already holding calculator item.

After: python3 scripts/gen_quests_12_20_calc_hunts.py
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
adena = lambda n: {"op": "give_adena", "count": n}
exp_sp = lambda e, s: {"op": "give_exp_sp", "exp": e, "sp": s}
START = [{"op": "create"}, {"op": "start"}]
ADENA = 57


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


def q169():
    Q, d = 169, html_loader("Q00169_OffspringOfNightmares", 169)
    VLASTY = 30145
    GAITERS, CRACKED, PERFECT = 31, 1030, 1031
    MOBS = (20025, 20105)
    sell_cracked = {"op": "sell_items", "prices": [{"itemId": CRACKED, "adena": 10}]}
    finish_base = [give(GAITERS), exp_sp(17475, 818)]
    dialogs = [
        d(VLASTY, "talk", "30145-00.htm", when="absent", races=[0, 1, 3, 4]),
        d(VLASTY, "talk", "30145-01.htm", when="absent", races=[2], maxLevel=14),
        d(VLASTY, "talk", "30145-02.htm", when="absent", races=[2], minLevel=15),
        d(
            VLASTY,
            "event",
            "30145-03.htm",
            when="absent",
            event="30145-03",
            races=[2],
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(VLASTY, "talk", "30145-06.html", cond=2, hasItem=PERFECT),
        d(
            VLASTY,
            "event",
            "30145-07.html",
            event="30145-07",
            cond=2,
            hasItem=PERFECT,
            hasAny=[CRACKED],
            ops=finish_base + [sell_cracked, adena(17030), {"op": "complete"}],
        ),
        d(
            VLASTY,
            "event",
            "30145-07.html",
            event="30145-07",
            cond=2,
            hasItem=PERFECT,
            lacksItems=[CRACKED],
            ops=finish_base + [adena(17030), {"op": "complete"}],
        ),
        d(VLASTY, "talk", "30145-05.html", when="started", hasItem=CRACKED, lacksItems=[PERFECT]),
        d(VLASTY, "talk", "30145-04.html", when="started", lacksItems=[CRACKED, PERFECT]),
    ]
    for mob in MOBS:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                raw="",
                ops=[drop(PERFECT, 0.2, limit=1, at=2), drop(CRACKED, 0.5)],
            )
        )
    return {
        "id": Q,
        "name": "Offspring of Nightmares",
        "repeatable": False,
        "abortable": True,
        "questItems": [CRACKED, PERFECT],
        "dialogs": dialogs,
    }


def q316():
    Q, d = 316, html_loader("Q00316_DestroyPlagueCarriers", 316)
    ELLENIA = 30155
    RAT, VAROOL = 1042, 1043
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": RAT, "adena": 30}, {"itemId": VAROOL, "adena": 10000}],
        "bonusMin": 10,
        "bonusAdena": 5000,
    }
    dialogs = [
        d(ELLENIA, "talk", "30155-00.htm", when="absent", races=[0, 2, 3, 4]),
        d(ELLENIA, "talk", "30155-02.htm", when="absent", races=[1], maxLevel=17),
        d(ELLENIA, "talk", "30155-03.htm", when="absent", races=[1], minLevel=18),
        d(
            ELLENIA,
            "event",
            "30155-04.htm",
            when="absent",
            event="30155-04",
            races=[1],
            minLevel=18,
            ops=START + [cond(1)],
        ),
        d(ELLENIA, "event", "30155-08.html", when="started", event="30155-08", ops=[{"op": "complete"}]),
        d(ELLENIA, "event", "30155-09.html", when="started", event="30155-09"),
        d(ELLENIA, "talk", "30155-07.html", when="started", hasAny=[RAT, VAROOL], ops=[sell]),
        d(ELLENIA, "talk", "30155-05.html", when="started", lacksItems=[RAT, VAROOL]),
        d(20040, "kill", None, when="started", raw="", ops=[drop(RAT, 1.0)]),
        d(20047, "kill", None, when="started", raw="", ops=[drop(RAT, 1.0)]),
        d(27020, "kill", None, when="started", raw="", ops=[drop(VAROOL, 1.0, limit=1)]),
    ]
    return {
        "id": Q,
        "name": "Destroy Plague Carriers",
        "repeatable": True,
        "abortable": True,
        "questItems": [RAT, VAROOL],
        "dialogs": dialogs,
    }


def q341():
    Q, d = 341, html_loader("Q00341_HuntingForWildBeasts", 341)
    PANO = 30078
    SKIN = 4259
    chances = {20203: 0.99, 20310: 0.87, 20021: 0.83, 20335: 0.87}
    dialogs = [
        d(PANO, "talk", "30078-02.htm", when="absent", maxLevel=19),
        d(PANO, "talk", "30078-01.html", when="absent", minLevel=20),
        d(PANO, "event", "30078-03.htm", when="absent", event="30078-03", minLevel=20),
        d(
            PANO,
            "event",
            "30078-04.htm",
            when="absent",
            event="30078-04",
            minLevel=20,
            ops=START + [cond(1)],
        ),
        d(
            PANO,
            "talk",
            "30078-05.html",
            cond=2,
            atLeast={SKIN: 20},
            ops=[adena(3710), {"op": "complete"}],
        ),
        d(PANO, "talk", "30078-06.html", when="started"),
    ]
    for mob, ch in chances.items():
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(SKIN, ch, limit=20, at=2)]))
    return {
        "id": Q,
        "name": "Hunting for Wild Beasts",
        "repeatable": True,
        "abortable": True,
        "questItems": [SKIN],
        "dialogs": dialogs,
    }


def q347():
    Q, d = 347, html_loader("Q00347_GoGetTheCalculator", 347)
    BRUNON, SILVERA, SPIRON, BALANKI = 30526, 30527, 30532, 30533
    STOLEN, GEM, CALC = 4285, 4286, 4393
    BEAST = 20540
    dialogs = [
        d(BRUNON, "talk", "30526-02.html", when="absent", maxLevel=11),
        d(BRUNON, "talk", "30526-01.htm", when="absent", minLevel=12),
        d(BRUNON, "event", "30526-03.htm", when="absent", event="30526-03"),
        d(BRUNON, "event", "30526-04.htm", when="absent", event="30526-04"),
        d(BRUNON, "event", "30526-05.htm", when="absent", event="30526-05"),
        d(BRUNON, "event", "30526-06.htm", when="absent", event="30526-06"),
        d(BRUNON, "event", "30526-07.htm", when="absent", event="30526-07"),
        d(
            BRUNON,
            "event",
            "30526-08.htm",
            when="absent",
            event="30526-08",
            minLevel=12,
            ops=START + [cond(1)],
        ),
        d(BRUNON, "talk", "30526-13.html", cond=1),
        d(BRUNON, "talk", "30526-13.html", cond=2),
        d(BRUNON, "talk", "30526-14.html", cond=3),
        d(BRUNON, "talk", "30526-14.html", cond=4),
        d(BRUNON, "talk", "30526-15.html", cond=5),
        d(BRUNON, "talk", "30526-09.html", cond=6),
        d(
            BRUNON,
            "event",
            "30526-10.html",
            event="30526-10",
            cond=6,
            hasItem=STOLEN,
            ops=[take(STOLEN), give(CALC), {"op": "complete"}],
        ),
        d(BRUNON, "event", "30526-09.html", event="30526-10", when="started"),
        d(
            BRUNON,
            "event",
            "30526-11.html",
            event="30526-11",
            cond=6,
            hasItem=STOLEN,
            ops=[take(STOLEN), adena(1500), {"op": "complete"}],
        ),
        d(SPIRON, "talk", "30532-01.html", cond=1),
        d(SPIRON, "talk", "30532-05.html", when="started"),
        d(SPIRON, "event", "30532-02.html", event="30532-02", cond=1, ops=[cond(2)]),
        d(SPIRON, "event", "30532-03.html", event="30532-03", when="started"),
        d(SPIRON, "event", "30532-04.html", event="30532-04", when="started"),
        d(BALANKI, "talk", "30533-01.html", cond=2),
        d(BALANKI, "talk", "30533-04.html", cond=3),
        d(BALANKI, "talk", "30533-04.html", cond=4),
        d(BALANKI, "talk", "30533-04.html", cond=5),
        d(BALANKI, "talk", "30533-04.html", cond=6),
        d(BALANKI, "talk", "30533-03.html", when="started"),
        d(
            BALANKI,
            "event",
            "30533-02.html",
            event="30533-02",
            cond=2,
            atLeast={ADENA: 101},
            ops=[take(ADENA, 100), cond(3)],
        ),
        d(SILVERA, "talk", "30527-01.html", cond=1),
        d(SILVERA, "talk", "30527-01.html", cond=2),
        d(SILVERA, "talk", "30527-02.html", cond=3, ops=[cond(4)]),
        d(SILVERA, "talk", "30527-04.html", cond=4),
        d(
            SILVERA,
            "talk",
            "30527-03.html",
            cond=5,
            atLeast={GEM: 10},
            ops=[take(GEM, 10), give(STOLEN), cond(6)],
        ),
        d(SILVERA, "talk", "30527-05.html", cond=6),
        d(BEAST, "kill", None, cond=4, raw="", ops=[drop(GEM, 0.4, limit=10, at=5)]),
    ]
    return {
        "id": Q,
        "name": "Go Get the Calculator",
        "repeatable": True,
        "abortable": True,
        "questItems": [STOLEN, GEM],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q169(), q316(), q341(), q347()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
