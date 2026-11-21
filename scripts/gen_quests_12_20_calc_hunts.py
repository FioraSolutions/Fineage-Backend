#!/usr/bin/env python3
"""Hand-parity Mobius Q169 / Q316 / Q341 / Q347 (batch 12–20).

  Q169 Offspring of Nightmares — Vlasty DE, skulls
  Q316 Destroy Plague Carriers — Ellenia Elf, wererat fangs sell
  Q341 Hunting for Wild Beasts — Pano, bear skins ×20
  Q347 Go Get the Calculator — Brunon guild chain + gemstone hunt

Simplifications:
  Q316: drop chance ≈ always (Mobius 10/N); keep party:true for getRandomPartyMemberState.
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

QROOT = questio.mobius_quest_root()

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
        return questio.mobius_quest_html(QROOT, script, qid, name)

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
        d(VLASTY, "talk", "30145-02.htm", when="absent", races=[2], maxLevel=14),
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
        d(VLASTY, "talk", "30145-06.htm", cond=2, hasItem=PERFECT),
        d(
            VLASTY,
            "event",
            "30145-07.htm",
            event="30145-07",
            cond=2,
            hasItem=PERFECT,
            hasAny=[CRACKED],
            ops=finish_base + [sell_cracked, adena(17030), {"op": "complete"}],
        ),
        d(
            VLASTY,
            "event",
            "30145-07.htm",
            event="30145-07",
            cond=2,
            hasItem=PERFECT,
            lacksItems=[CRACKED],
            ops=finish_base + [adena(17030), {"op": "complete"}],
        ),
        d(VLASTY, "talk", "30145-05.htm", when="started", hasItem=CRACKED, lacksItems=[PERFECT]),
        d(VLASTY, "talk", "30145-04.htm", when="started", lacksItems=[CRACKED, PERFECT]),
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
        d(ELLENIA, "event", "30155-08.htm", when="started", event="30155-08", ops=[{"op": "complete"}]),
        d(ELLENIA, "event", "30155-09.htm", when="started", event="30155-09"),
        d(ELLENIA, "talk", "30155-07.htm", when="started", hasAny=[RAT, VAROOL], ops=[sell]),
        d(ELLENIA, "talk", "30155-05.htm", when="started", lacksItems=[RAT, VAROOL]),
        d(20040, "kill", None, when="started", party=True, raw="", ops=[drop(RAT, 1.0)]),
        d(20047, "kill", None, when="started", party=True, raw="", ops=[drop(RAT, 1.0)]),
        d(27020, "kill", None, when="started", party=True, raw="", ops=[drop(VAROOL, 1.0, limit=1)]),
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
        d(PANO, "talk", "30078-01.htm", when="absent", minLevel=20),
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
            "30078-04.htm",
            cond=2,
            atLeast={SKIN: 20},
            ops=[adena(3710), {"op": "complete"}],
        ),
        d(PANO, "talk", "30078-04.htm", when="started"),
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
        d(BRUNON, "talk", "30526-02.htm", when="absent", maxLevel=11),
        d(BRUNON, "talk", "30526-01.htm", when="absent", minLevel=12),
        d(BRUNON, "event", "30526-03.htm", when="absent", event="30526-03"),
        d(BRUNON, "event", "30526-03.htm", when="absent", event="30526-03"),
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
        d(BRUNON, "talk", "30526-03.htm", cond=1),
        d(BRUNON, "talk", "30526-03.htm", cond=2),
        d(BRUNON, "talk", "30526-09.htm", cond=3),
        d(BRUNON, "talk", "30526-09.htm", cond=4),
        d(BRUNON, "talk", "30526-08.htm", cond=5),
        d(BRUNON, "talk", "30526-09.htm", cond=6),
        d(
            BRUNON,
            "event",
            "30526-09.htm",
            event="30526-09",
            cond=6,
            hasItem=STOLEN,
            ops=[take(STOLEN), give(CALC), {"op": "complete"}],
        ),
        d(BRUNON, "event", "30526-09.htm", event="30526-09", when="started"),
        d(
            BRUNON,
            "event",
            "30526-09.htm",
            event="30526-09",
            cond=6,
            hasItem=STOLEN,
            ops=[take(STOLEN), adena(1500), {"op": "complete"}],
        ),
        d(SPIRON, "talk", "30532-01.htm", cond=1),
        d(SPIRON, "talk", "30532-05.htm", when="started"),
        d(SPIRON, "event", "30532-02.htm", event="30532-02", cond=1, ops=[cond(2)]),
        d(SPIRON, "event", "30532-03.htm", event="30532-03", when="started"),
        d(SPIRON, "event", "30532-04.htm", event="30532-04", when="started"),
        d(BALANKI, "talk", "30533-01.htm", cond=2),
        d(BALANKI, "talk", "30533-04.htm", cond=3),
        d(BALANKI, "talk", "30533-04.htm", cond=4),
        d(BALANKI, "talk", "30533-04.htm", cond=5),
        d(BALANKI, "talk", "30533-04.htm", cond=6),
        d(BALANKI, "talk", "30533-03.htm", when="started"),
        d(
            BALANKI,
            "event",
            "30533-02.htm",
            event="30533-02",
            cond=2,
            atLeast={ADENA: 101},
            ops=[take(ADENA, 100), cond(3)],
        ),
        d(SILVERA, "talk", "30527-01.htm", cond=1),
        d(SILVERA, "talk", "30527-01.htm", cond=2),
        d(SILVERA, "talk", "30527-02.htm", cond=3, ops=[cond(4)]),
        d(SILVERA, "talk", "30527-04.htm", cond=4),
        d(
            SILVERA,
            "talk",
            "30527-03.htm",
            cond=5,
            atLeast={GEM: 10},
            ops=[take(GEM, 10), give(STOLEN), cond(6)],
        ),
        d(SILVERA, "talk", "30527-04.htm", cond=6),
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
