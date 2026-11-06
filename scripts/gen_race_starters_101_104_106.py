#!/usr/bin/env python3
"""Hand-parity Mobius race starters Q101 / Q104 / Q106 (batch 0–10).

  Q101 Sword of Solidarity — Human (Roien/Altran), lvl 9+
  Q104 Spirit of Mirrors — Human (Gallint), lvl 10+
  Q106 Forgotten Truth — Dark Elf (Thifiell/Karta), lvl 10+

Simplifications:
  Q104: no equipped-wand gate (Fineage has no paperdoll quest gate); guard memo
        skipped — kills work from cond 1; talking to guards is HTML-only.
  Q104/Q101/Q106: echo/potion rewards always granted (Mobius ItemHolder loop).

After: python3 scripts/gen_race_starters_101_104_106.py
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


ECHO = [(1060, 100), (4412, 10), (4413, 10), (4414, 10), (4415, 10), (4416, 10)]


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


def reward_ops(weapon_id: int, exp: int, sp: int, aden: int) -> list:
    ops = [give(weapon_id)]
    for iid, c in ECHO:
        ops.append(give(iid, c))
    ops.extend([adena(aden), exp_sp(exp, sp), {"op": "complete"}])
    return ops


def q101():
    Q, d = 101, html_loader("Q00101_SwordOfSolidarity", 101)
    ROIEN, ALTRAN = 30008, 30283
    HANDLE, BOTTOM, TOP, NOTE, LETTER, DIR = 739, 740, 741, 742, 796, 937
    SWORD = 738
    MOBS = (20361, 20362)
    dialogs = [
        d(ROIEN, "talk", "30008-01.htm", when="absent", races=[1, 2, 3, 4]),
        d(ROIEN, "talk", "30008-08.htm", when="absent", races=[0], maxLevel=8),
        d(ROIEN, "talk", "30008-02.htm", when="absent", races=[0], minLevel=9),
        d(ROIEN, "event", "30008-03.html", when="absent", event="30008-03", races=[0], minLevel=9),
        d(ROIEN, "event", "30008-09.html", when="absent", event="30008-09", races=[0], minLevel=9),
        d(
            ROIEN,
            "event",
            "30008-04.htm",
            when="absent",
            event="30008-04",
            races=[0],
            minLevel=9,
            ops=START + [give(LETTER), cond(1)],
        ),
        d(ROIEN, "talk", "30008-05.html", cond=1, hasItem=LETTER),
        d(ROIEN, "talk", "30008-11.html", cond=2, hasAny=[BOTTOM, TOP]),
        d(ROIEN, "talk", "30008-10.html", cond=2, hasItem=DIR),
        d(ROIEN, "talk", "30008-12.html", cond=3, hasItems=[BOTTOM, TOP]),
        d(
            ROIEN,
            "talk",
            "30008-06.html",
            cond=4,
            hasItem=NOTE,
            ops=[take(NOTE), give(HANDLE), cond(5)],
        ),
        d(ROIEN, "talk", "30008-07.html", cond=5, hasItem=HANDLE),
        d(ALTRAN, "talk", "30283-01.html", cond=1, hasItem=LETTER),
        d(
            ALTRAN,
            "event",
            "30283-02.html",
            event="30283-02",
            cond=1,
            hasItem=LETTER,
            ops=[take(LETTER), give(DIR), cond(2)],
        ),
        d(ALTRAN, "talk", "30283-08.html", cond=2, hasAny=[BOTTOM, TOP]),
        d(ALTRAN, "talk", "30283-03.html", cond=2, hasItem=DIR),
        d(
            ALTRAN,
            "talk",
            "30283-04.html",
            cond=3,
            hasItems=[BOTTOM, TOP],
            ops=[take(DIR), take(TOP), take(BOTTOM), give(NOTE), cond(4)],
        ),
        d(ALTRAN, "talk", "30283-05.html", cond=4, hasItem=NOTE),
        d(ALTRAN, "talk", "30283-06.html", cond=5, hasItem=HANDLE),
        d(
            ALTRAN,
            "event",
            "30283-07.html",
            event="30283-07",
            cond=5,
            hasItem=HANDLE,
            ops=reward_ops(SWORD, 25747, 2171, 10981),
        ),
    ]
    # 20% blade piece; prefer missing half; both → cond 3
    for mob in MOBS:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=2,
                hasItem=BOTTOM,
                lacksItems=[TOP],
                raw="",
                ops=[drop(TOP, 0.2, limit=1, at=3)],
            )
        )
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=2,
                hasItem=TOP,
                lacksItems=[BOTTOM],
                raw="",
                ops=[drop(BOTTOM, 0.2, limit=1, at=3)],
            )
        )
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=2,
                lacksItems=[TOP, BOTTOM],
                raw="",
                ops=[drop(TOP, 0.2, limit=1)],
            )
        )
    return {
        "id": Q,
        "name": "Sword of Solidarity",
        "repeatable": False,
        "abortable": True,
        "questItems": [HANDLE, BOTTOM, TOP, NOTE, LETTER, DIR],
        "dialogs": dialogs,
    }


def q104():
    Q, d = 104, html_loader("Q00104_SpiritOfMirrors", 104)
    GALLINT, ARNOLD, JOHNSTONE, KENYOS = 30017, 30041, 30043, 30045
    OAK, W1, W2, W3 = 748, 1135, 1136, 1137
    ADEPT = 747
    MONSTERS = {27003: W1, 27004: W2, 27005: W3}
    dialogs = [
        d(GALLINT, "talk", "30017-01.htm", when="absent", races=[1, 2, 3, 4]),
        d(GALLINT, "talk", "30017-02.htm", when="absent", races=[0], maxLevel=9),
        d(GALLINT, "talk", "30017-03.htm", when="absent", races=[0], minLevel=10),
        d(
            GALLINT,
            "event",
            "30017-04.htm",
            when="absent",
            event="30017-04",
            races=[0],
            minLevel=10,
            ops=START + [give(OAK, 3), cond(1)],
        ),
        d(GALLINT, "talk", "30017-05.html", cond=1),
        d(GALLINT, "talk", "30017-05.html", cond=2),
        d(
            GALLINT,
            "talk",
            "30017-06.html",
            cond=3,
            hasItems=[W1, W2, W3],
            ops=reward_ops(ADEPT, 39750, 3407, 16866),
        ),
        # Guards: HTML only (Mobius memo → cond 2; kills already work on cond 1).
        d(ARNOLD, "talk", "30041-01.html", when="started"),
        d(JOHNSTONE, "talk", "30043-01.html", when="started"),
        d(KENYOS, "talk", "30045-01.html", when="started"),
    ]
    for mob, wand in MONSTERS.items():
        others = [w for w in (W1, W2, W3) if w != wand]
        # Completing kill: already hold the other two spiritbounds
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                hasItem=OAK,
                hasItems=others,
                lacksItems=[wand],
                raw="",
                ops=[take(OAK), give(wand), cond(3)],
            )
        )
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                hasItem=OAK,
                lacksItems=[wand],
                raw="",
                ops=[take(OAK), give(wand)],
            )
        )
    return {
        "id": Q,
        "name": "Spirit of Mirrors",
        "repeatable": False,
        "abortable": True,
        "questItems": [OAK, W1, W2, W3],
        "dialogs": dialogs,
    }


def q106():
    Q, d = 106, html_loader("Q00106_ForgottenTruth", 106)
    THIFIELL, KARTA = 30358, 30133
    T1, T2, SCROLL, TABLET, TRANS = 984, 985, 986, 987, 988
    DAGGER, STAFF = 989, 2373
    MOB = 27070
    finish = lambda weapon: [
        give(weapon),
        *[give(i, c) for i, c in ECHO],
        adena(10266),
        exp_sp(24195, 2074),
        {"op": "complete"},
    ]
    dialogs = [
        d(THIFIELL, "talk", "30358-01.htm", when="absent", races=[0, 1, 3, 4]),
        d(THIFIELL, "talk", "30358-02.htm", when="absent", races=[2], maxLevel=9),
        d(THIFIELL, "talk", "30358-03.htm", when="absent", races=[2], minLevel=10),
        d(THIFIELL, "event", "30358-04.htm", when="absent", event="30358-04", races=[2], minLevel=10),
        d(
            THIFIELL,
            "event",
            "30358-05.htm",
            when="absent",
            event="30358-05",
            races=[2],
            minLevel=10,
            ops=START + [give(T1), cond(1)],
        ),
        d(THIFIELL, "talk", "30358-06.html", when="started", hasAny=[T1, T2], lacksItems=[TRANS]),
        d(
            THIFIELL,
            "talk",
            "30358-07.html",
            cond=4,
            hasItem=TRANS,
            isMage=True,
            ops=finish(STAFF),
        ),
        d(
            THIFIELL,
            "talk",
            "30358-07.html",
            cond=4,
            hasItem=TRANS,
            isMage=False,
            ops=finish(DAGGER),
        ),
        d(
            KARTA,
            "talk",
            "30133-01.html",
            cond=1,
            hasItem=T1,
            ops=[take(T1), give(T2), cond(2)],
        ),
        d(KARTA, "talk", "30133-02.html", cond=2, hasItem=T2),
        d(
            KARTA,
            "talk",
            "30133-03.html",
            cond=3,
            hasItems=[SCROLL, TABLET],
            ops=[take(SCROLL), take(TABLET), take(T2), give(TRANS), cond(4)],
        ),
        d(KARTA, "talk", "30133-04.html", cond=4, hasItem=TRANS),
        # 20% drop: scroll first, then tablet → cond 3 (Mobius getRandom(100)<20)
        d(
            MOB,
            "kill",
            None,
            cond=2,
            hasItem=T2,
            lacksItems=[SCROLL],
            raw="",
            ops=[drop(SCROLL, 0.2, limit=1)],
        ),
        d(
            MOB,
            "kill",
            None,
            cond=2,
            hasItems=[T2, SCROLL],
            lacksItems=[TABLET],
            raw="",
            ops=[drop(TABLET, 0.2, limit=1, at=3)],
        ),
    ]
    return {
        "id": Q,
        "name": "Forgotten Truth",
        "repeatable": False,
        "abortable": True,
        "questItems": [T1, T2, SCROLL, TABLET, TRANS],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q101(), q104(), q106()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
