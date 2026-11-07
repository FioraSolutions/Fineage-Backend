#!/usr/bin/env python3
"""Hand-parity Mobius Q154 / Q155 / Q642 / Q644 (batch mix early + late).

  Q154 Sacrifice to the Sea — fox fur → yarn → doll → earring
  Q155 Find Sir Windawood — letter delivery
  Q642 A Powerful Primeval Creature — dino tissue sell
  Q644 Grave Robber Annihilation — orc goods×120 → material pick

After: python3 scripts/gen_quests_154_644_mix.py
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


def q154():
    Q, d = 154, html_loader("Q00154_SacrificeToTheSea", 154)
    ROCK, CRISTEL, ROLLFNAN = 30312, 30051, 30055
    FUR, YARN, DOLL, EAR = 1032, 1033, 1034, 113
    dialogs = [
        d(ROCK, "talk", "30312-02.htm", when="absent", maxLevel=1),
        d(ROCK, "talk", "30312-01.htm", when="absent", minLevel=2),
        d(
            ROCK,
            "event",
            "30312-03.htm",
            when="absent",
            event="30312-03",
            minLevel=2,
            ops=START + [cond(1)],
        ),
        d(ROCK, "talk", "30312-04.html", cond=1),
        d(ROCK, "talk", "30312-07.html", cond=2),
        d(ROCK, "talk", "30312-05.html", cond=3),
        d(
            ROCK,
            "talk",
            "30312-06.html",
            cond=4,
            hasItem=DOLL,
            ops=[take(DOLL), give(EAR), exp_sp(0, 1000), {"op": "complete"}],
        ),
        d(CRISTEL, "talk", "30051-02.html", cond=1),
        d(
            CRISTEL,
            "talk",
            "30051-01.html",
            cond=2,
            atLeast={FUR: 10},
            ops=[take(FUR, 10), give(YARN), cond(3)],
        ),
        d(CRISTEL, "talk", "30051-03.html", cond=3),
        d(CRISTEL, "talk", "30051-04.html", cond=4),
        d(ROLLFNAN, "talk", "30055-03.html", cond=1),
        d(ROLLFNAN, "talk", "30055-03.html", cond=2),
        d(
            ROLLFNAN,
            "talk",
            "30055-01.html",
            cond=3,
            hasItem=YARN,
            ops=[take(YARN), give(DOLL), cond(4)],
        ),
        d(ROLLFNAN, "talk", "30055-02.html", cond=4),
    ]
    for mob in (20544, 20545, 20481):
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(FUR, 0.3, limit=10, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Sacrifice to the Sea",
        "repeatable": False,
        "abortable": True,
        "questItems": [FUR, YARN, DOLL],
        "dialogs": dialogs,
    }


def q155():
    Q, d = 155, html_loader("Q00155_FindSirWindawood", 155)
    ABELLOS, WINDAWOOD = 30042, 30311
    LETTER, HASTE = 1019, 734
    dialogs = [
        d(ABELLOS, "talk", "30042-01.htm", when="absent", maxLevel=2),
        d(ABELLOS, "talk", "30042-02.htm", when="absent", minLevel=3),
        d(
            ABELLOS,
            "event",
            "30042-03.htm",
            when="absent",
            event="30042-03",
            minLevel=3,
            ops=START + [cond(1), give(LETTER)],
        ),
        d(ABELLOS, "talk", "30042-04.html", when="started"),
        d(
            WINDAWOOD,
            "talk",
            "30311-01.html",
            when="started",
            hasItem=LETTER,
            ops=[give(HASTE), {"op": "complete"}],
        ),
    ]
    return {
        "id": Q,
        "name": "Find Sir Windawood",
        "repeatable": False,
        "abortable": True,
        "questItems": [LETTER],
        "dialogs": dialogs,
    }


def q642():
    Q, d = 642, html_loader("Q00642_APowerfulPrimevalCreature", 642)
    DINN = 32105
    TISSUE, EGG = 8774, 8775
    tissues = {
        22196: 0.309, 22197: 0.309, 22198: 0.309, 22199: 0.309,
        22215: 0.988, 22216: 0.988, 22217: 0.988,
        22218: 0.309, 22223: 0.309,
    }
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": TISSUE, "adena": 5000}],
        "bonusMin": 0,
        "bonusAdena": 0,
    }
    dialogs = [
        d(DINN, "talk", "32105-01.htm", when="absent", maxLevel=74),
        d(DINN, "talk", "32105-02.htm", when="absent", minLevel=75),
        d(DINN, "event", "32105-03.htm", when="absent", event="32105-03", minLevel=75),
        d(DINN, "event", "32105-04.htm", when="absent", event="32105-04", minLevel=75),
        d(
            DINN,
            "event",
            "32105-05.html",
            when="absent",
            event="32105-05",
            minLevel=75,
            ops=START + [cond(1)],
        ),
        d(DINN, "event", "32105-06.htm", when="absent", event="32105-06"),
        d(DINN, "talk", "32105-08.html", when="started", hasAny=[TISSUE, EGG]),
        d(DINN, "talk", "32105-07.html", when="started", lacksItems=[TISSUE, EGG]),
        d(
            DINN,
            "event",
            "32105-09.html",
            when="started",
            event="32105-09",
            hasItem=TISSUE,
            ops=[sell],
        ),
        d(DINN, "event", "32105-14.html", when="started", event="32105-09", lacksItems=[TISSUE]),
        d(
            DINN,
            "event",
            "32105-12.html",
            when="started",
            event="exit",
            hasItem=TISSUE,
            ops=[sell, {"op": "complete"}],
        ),
        d(
            DINN,
            "event",
            "32105-13.html",
            when="started",
            event="exit",
            lacksItems=[TISSUE],
            ops=[{"op": "complete"}],
        ),
    ]
    for mob, ch in tissues.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(TISSUE, ch)])
        )
    dialogs.append(
        d(18344, "kill", None, when="started", party=True, raw="", ops=[drop(EGG, 1.0)])
    )
    return {
        "id": Q,
        "name": "A Powerful Primeval Creature",
        "repeatable": True,
        "abortable": True,
        "questItems": [TISSUE, EGG],
        "dialogs": dialogs,
    }


def q644():
    Q, d = 644, html_loader("Q00644_GraveRobberAnnihilation", 644)
    KARUDA = 32017
    GOODS = 8088
    mobs = {22003: 0.714, 22004: 0.841, 22005: 0.778, 22006: 0.746, 22008: 0.810}
    rewards = {
        "varnish": (1865, 30),
        "animalskin": (1867, 40),
        "animalbone": (1872, 40),
        "charcoal": (1871, 30),
        "coal": (1870, 30),
        "ironore": (1869, 30),
    }
    dialogs = [
        d(KARUDA, "talk", "32017-02.htm", when="absent", maxLevel=19),
        d(KARUDA, "talk", "32017-01.htm", when="absent", minLevel=20),
        d(
            KARUDA,
            "event",
            "32017-03.htm",
            when="absent",
            event="32017-03",
            minLevel=20,
            ops=START + [cond(1)],
        ),
        d(KARUDA, "talk", "32017-04.html", cond=2, atLeast={GOODS: 120}),
        d(KARUDA, "talk", "32017-05.html", when="started"),
        d(
            KARUDA,
            "event",
            "32017-06.html",
            event="32017-06",
            cond=2,
            atLeast={GOODS: 120},
        ),
    ]
    for name, (item, count) in rewards.items():
        dialogs.append(
            d(
                KARUDA,
                "event",
                "32017-07.html",
                event=name,
                cond=2,
                atLeast={GOODS: 120},
                ops=[give(item, count), {"op": "complete"}],
            )
        )
    for mob, ch in mobs.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(GOODS, ch, limit=120, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Grave Robber Annihilation",
        "repeatable": True,
        "abortable": True,
        "questItems": [GOODS],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q154(), q155(), q642(), q644()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
