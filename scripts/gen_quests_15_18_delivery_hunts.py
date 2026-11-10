#!/usr/bin/env python3
"""Hand-parity Mobius Q156 / Q167 / Q306 / Q317 (batch 15–18 delivery + shard hunts).

  Q156 Millennium Love — Lilith ↔ Baenedes letter/diary
  Q167 Dwarven Kinship — Carlon → Haprock → Norman (early-exit branches)
  Q306 Crystals of Fire and Ice — Katerina, flame/ice shard sell
  Q317 Catch the Wind — Rizraell, wind shard sell

Simplifications:
  Q306: drop chance ≈ always (Mobius 1000/N with N≈900–950).
  Q317: killer-only drop (skip getRandomPartyMemberState).

After: python3 scripts/gen_quests_15_18_delivery_hunts.py
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


def q156():
    Q, d = 156, html_loader("Q00156_MillenniumLove", 156)
    LILITH, BAENEDES = 30368, 30369
    LETTER, DIARY, SOUL = 1022, 1023, 5250
    dialogs = [
        d(LILITH, "talk", "30368-01.htm", when="absent"),
        d(LILITH, "event", "30368-02.html", when="absent", event="30368-02"),
        d(LILITH, "event", "30368-03.html", when="absent", event="30368-03"),
        d(LILITH, "event", "30368-04.htm", when="absent", event="30368-05", maxLevel=14),
        d(
            LILITH,
            "event",
            "30368-05.htm",
            when="absent",
            event="30368-05",
            minLevel=15,
            ops=START + [give(LETTER), cond(1)],
        ),
        d(LILITH, "talk", "30368-06.html", cond=1, hasItem=LETTER),
        d(
            LILITH,
            "talk",
            "30368-07.html",
            cond=2,
            hasItem=DIARY,
            ops=[give(SOUL), exp_sp(3000, 0), {"op": "complete"}],
        ),
        d(BAENEDES, "talk", "30369-01.html", cond=1, hasItem=LETTER),
        d(
            BAENEDES,
            "event",
            "30369-02.html",
            event="30369-02",
            cond=1,
            hasItem=LETTER,
            ops=[take(LETTER), give(DIARY), cond(2)],
        ),
        d(
            BAENEDES,
            "event",
            "30369-03.html",
            event="30369-03",
            cond=1,
            hasItem=LETTER,
            ops=[exp_sp(3000, 0), {"op": "complete"}],
        ),
        d(BAENEDES, "talk", "30369-04.html", cond=2, hasItem=DIARY),
    ]
    return {
        "id": Q,
        "name": "Millennium Love",
        "repeatable": False,
        "abortable": True,
        "questItems": [LETTER, DIARY],
        "dialogs": dialogs,
    }


def q167():
    Q, d = 167, html_loader("Q00167_DwarvenKinship", 167)
    NORMAN, HAPROCK, CARLON = 30210, 30255, 30350
    C_LETTER, N_LETTER = 1076, 1106
    dialogs = [
        d(CARLON, "talk", "30350-01.htm", when="absent", maxLevel=14),
        d(CARLON, "talk", "30350-02.htm", when="absent", minLevel=15),
        d(
            CARLON,
            "event",
            "30350-03.htm",
            when="absent",
            event="30350-03",
            minLevel=15,
            ops=START + [give(C_LETTER), cond(1)],
        ),
        d(CARLON, "talk", "30350-04.html", cond=1, hasItem=C_LETTER),
        d(HAPROCK, "talk", "30255-01.html", cond=1, hasItem=C_LETTER),
        d(HAPROCK, "event", "30255-02.html", event="30255-02", cond=1, hasItem=C_LETTER),
        d(
            HAPROCK,
            "event",
            "30255-03.html",
            event="30255-03",
            cond=1,
            hasItem=C_LETTER,
            ops=[take(C_LETTER), give(N_LETTER), adena(2000), cond(2)],
        ),
        d(
            HAPROCK,
            "event",
            "30255-04.html",
            event="30255-04",
            cond=1,
            hasItem=C_LETTER,
            ops=[adena(15000), {"op": "complete"}],
        ),
        d(HAPROCK, "talk", "30255-05.html", cond=2, hasItem=N_LETTER),
        d(NORMAN, "talk", "30210-01.html", cond=2, hasItem=N_LETTER),
        d(
            NORMAN,
            "event",
            "30210-02.html",
            event="30210-02",
            cond=2,
            hasItem=N_LETTER,
            ops=[adena(20000), {"op": "complete"}],
        ),
    ]
    return {
        "id": Q,
        "name": "Dwarven Kinship",
        "repeatable": False,
        "abortable": True,
        "questItems": [C_LETTER, N_LETTER],
        "dialogs": dialogs,
    }


def q306():
    Q, d = 306, html_loader("Q00306_CrystalOfFireAndIce", 306)
    KATERINA = 30004
    FLAME, ICE = 1020, 1021
    # Mobius ItemHolder count ≈ chance denom; 1000/N ≈ always.
    drops = {
        20109: FLAME,
        20110: ICE,
        20112: FLAME,
        20113: ICE,
        20114: FLAME,
        20115: ICE,
    }
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": FLAME, "adena": 40}, {"itemId": ICE, "adena": 40}],
        "bonusMin": 10,
        "bonusAdena": 5000,
    }
    dialogs = [
        d(KATERINA, "talk", "30004-02.htm", when="absent", maxLevel=16),
        d(KATERINA, "talk", "30004-03.htm", when="absent", minLevel=17),
        d(
            KATERINA,
            "event",
            "30004-04.htm",
            when="absent",
            event="30004-04",
            minLevel=17,
            ops=START + [cond(1)],
        ),
        d(KATERINA, "event", "30004-08.html", when="started", event="30004-08", ops=[{"op": "complete"}]),
        d(KATERINA, "event", "30004-09.html", when="started", event="30004-09"),
        d(KATERINA, "talk", "30004-07.html", when="started", hasAny=[FLAME, ICE], ops=[sell]),
        d(KATERINA, "talk", "30004-05.html", when="started", lacksItems=[FLAME, ICE]),
    ]
    for mob, item in drops.items():
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(item, 1.0)]))
    return {
        "id": Q,
        "name": "Crystals of Fire and Ice",
        "repeatable": True,
        "abortable": True,
        "questItems": [FLAME, ICE],
        "dialogs": dialogs,
    }


def q317():
    Q, d = 317, html_loader("Q00317_CatchTheWind", 317)
    RIZRAELL = 30361
    SHARD = 1078
    MOBS = (20036, 20044)
    sell = {
        "op": "sell_items",
        "prices": [{"itemId": SHARD, "adena": 40}],
        "bonusMin": 10,
        "bonusAdena": 2988,
    }
    dialogs = [
        d(RIZRAELL, "talk", "30361-02.htm", when="absent", maxLevel=17),
        d(RIZRAELL, "talk", "30361-03.htm", when="absent", minLevel=18),
        d(
            RIZRAELL,
            "event",
            "30361-04.htm",
            when="absent",
            event="30361-04",
            minLevel=18,
            ops=START + [cond(1)],
        ),
        d(RIZRAELL, "talk", "30361-07.html", when="started", hasItem=SHARD),
        d(RIZRAELL, "talk", "30361-05.html", when="started", lacksItems=[SHARD]),
        # 08 = sell + quit; 09 = sell + continue
        d(
            RIZRAELL,
            "event",
            "30361-08.html",
            when="started",
            event="30361-08",
            hasItem=SHARD,
            ops=[sell, {"op": "complete"}],
        ),
        d(
            RIZRAELL,
            "event",
            "30361-08.html",
            when="started",
            event="30361-08",
            lacksItems=[SHARD],
            ops=[{"op": "complete"}],
        ),
        d(
            RIZRAELL,
            "event",
            "30361-09.html",
            when="started",
            event="30361-09",
            hasItem=SHARD,
            ops=[sell],
        ),
        d(RIZRAELL, "event", "30361-09.html", when="started", event="30361-09", lacksItems=[SHARD]),
    ]
    for mob in MOBS:
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(SHARD, 0.5)]))
    return {
        "id": Q,
        "name": "Catch The Wind",
        "repeatable": True,
        "abortable": True,
        "questItems": [SHARD],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q156(), q167(), q306(), q317()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
