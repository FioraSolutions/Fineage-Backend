#!/usr/bin/env python3
"""Batch-port early Elven Village hunt quests into data/quests/ pack (questio.upsert):
  Q105 Skirmish with Orcs (linearized to orders 1→5; Mobius random order pick is a follow-up)
  Q159 Protect the Water Source
  Q162 Curse of the Underground Fortress
  Q163 Legacy of the Poet

HTML from Mobius CT0 under ref/.../scripts/quests/. Run gen_drop_items.py + gen_quest_item_flags.py after.
"""
from __future__ import annotations

import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import questio
QROOT = ROOT / "ref/mobius-ct0-interlude/dist/game/data/scripts/quests"
if not QROOT.is_dir():
    QROOT = ROOT / "ref/mobius-ct0-interlude/data/scripts/quests"

give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
drop = lambda i, chance, limit=0, at=None, count=1: {
    "op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit,
    **({"condAtLimit": at} if at is not None else {}),
}
START = [{"op": "create"}, {"op": "start"}]


def html_loader(script: str, qid: int):
    src = QROOT / script

    def html(name: str) -> str:
        for ext in ("", ".htm", ".html"):
            p = src / (name + ext)
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


def q105():
    """Skirmish with Orcs — linear 1st-order then 5th-order path (no random order roll)."""
    Q, d = 105, html_loader("Q00105_SkirmishWithOrcs", 105)
    K = 30218
    O1, O5 = 1836, 1840
    T1, T2 = 1844, 1845
    dialogs = [
        d(K, "talk", "30218-03.htm", when="absent", races=[1], minLevel=10),
        d(K, "talk", "30218-02.htm", when="absent", races=[1]),
        d(K, "talk", "30218-01.htm", when="absent"),
        d(K, "event", "30218-04.html", when="absent", event="30218-04", races=[1], minLevel=10,
          ops=START + [give(O1), cond(1)]),
        d(K, "event", "30218-05.html", when="any", event="30218-05"),
        d(K, "talk", "30218-06.html", cond=1, hasAny=[O1, 1837, 1838, 1839]),
        # Linear path: only 1st then 5th order (Mobius rolls random among 1-4 / 5-8).
        d(K, "talk", "30218-07.html", cond=2, hasItem=T1,
          ops=[take(O1), take(T1), give(O5), cond(3)]),
        d(K, "talk", "30218-08.html", cond=3, hasItem=O5),
        d(K, "talk", "30218-09.html", cond=4, hasItem=T2, ops=[
            {"op": "give_adena", "count": 17599},
            {"op": "give_exp_sp", "exp": 41478, "sp": 3555},
            {"op": "complete"},
        ]),
        # Kaboo chiefs: torque only while holding the matching order
        d(27059, "kill", None, cond=1, hasItem=O1, raw="", ops=[give(T1), cond(2)]),
        d(27064, "kill", None, cond=3, hasItem=O5, raw="", ops=[give(T2), cond(4)]),
    ]
    return {
        "id": Q, "name": "Skirmish with Orcs", "repeatable": False, "abortable": True,
        "questItems": [O1, 1837, 1838, 1839, O5, 1841, 1842, 1843, T1, T2],
        "dialogs": dialogs,
    }


def q159():
    Q, d = 159, html_loader("Q00159_ProtectTheWaterSource", 159)
    A, Z = 30154, 27017
    DUST, C1, C2 = 1035, 1071, 1072
    dialogs = [
        d(A, "talk", "30154-03.htm", when="absent", races=[1], minLevel=12),
        d(A, "talk", "30154-02.htm", when="absent", races=[1]),
        d(A, "talk", "30154-01.htm", when="absent"),
        d(A, "event", "30154-04.htm", when="absent", event="30154-04", races=[1], minLevel=12,
          ops=START + [give(C1), cond(1)]),
        d(A, "talk", "30154-05.html", cond=1, hasItem=C1, lacksItems=[DUST]),
        d(A, "talk", "30154-06.html", cond=2, hasItems=[C1, DUST],
          ops=[take(C1), take(DUST), give(C2), cond(3)]),
        d(A, "talk", "30154-07.html", cond=3, hasItem=C2),
        d(A, "talk", "30154-08.html", cond=4, hasItem=C2, atLeast={DUST: 5}, ops=[
            {"op": "give_adena", "count": 18250},
            {"op": "complete"},
        ]),
        # First dust (40%) while charm1; second phase up to 5 with charm2
        d(Z, "kill", None, cond=1, hasItem=C1, lacksItems=[DUST], raw="",
          ops=[drop(DUST, 0.4, limit=1, at=2)]),
        d(Z, "kill", None, cond=3, hasItem=C2, raw="",
          ops=[drop(DUST, 0.4, limit=5, at=4)]),
    ]
    return {
        "id": Q, "name": "Protect the Water Source", "repeatable": False, "abortable": True,
        "questItems": [DUST, C1, C2], "dialogs": dialogs,
    }


def q162():
    Q, d = 162, html_loader("Q00162_CurseOfTheUndergroundFortress", 162)
    U = 30147
    SHIELD, BONE, SKULL = 625, 1158, 1159
    skulls = {20033: 0.25, 20345: 0.26, 20371: 0.23}
    bones = {20463: 0.25, 20464: 0.23, 20504: 0.26}
    dialogs = [
        d(U, "talk", "30147-02.htm", when="absent", races=[0, 1, 3, 4], minLevel=12),  # not dark elf
        d(U, "talk", "30147-01.htm", when="absent", races=[0, 1, 3, 4]),
        d(U, "talk", "30147-00.htm", when="absent", races=[2]),
        d(U, "event", "30147-03.htm", when="any", event="30147-03"),
        d(U, "event", "30147-04.htm", when="absent", event="30147-04",
          races=[0, 1, 3, 4], minLevel=12, ops=START + [cond(1)]),
        # Done when 10 bones + 3 skulls (Mobius REQUIRED_COUNT 13)
        d(U, "talk", "30147-06.html", cond=1, atLeast={BONE: 10, SKULL: 3}, ops=[
            give(SHIELD),
            {"op": "give_exp_sp", "exp": 22652, "sp": 1004},
            {"op": "give_adena", "count": 24000},
            {"op": "complete"},
        ]),
        d(U, "talk", "30147-05.html", cond=1),
    ]
    for mob, ch in skulls.items():
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(SKULL, ch, limit=3)]))
    for mob, ch in bones.items():
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(BONE, ch, limit=10)]))
    return {
        "id": Q, "name": "Curse of the Underground Fortress", "repeatable": False, "abortable": True,
        "questItems": [BONE, SKULL], "dialogs": dialogs,
    }


def q163():
    Q, d = 163, html_loader("Q00163_LegacyOfThePoet", 163)
    S = 30220
    P1, P2, P3, P4 = 1038, 1039, 1040, 1041
    poems = [P1, P2, P3, P4]
    # Mobius chances: p1 10%, p2/p3 20%, p4 40% — only if not already held (limit 1)
    chances = {P1: 0.1, P2: 0.2, P3: 0.2, P4: 0.4}
    dialogs = [
        d(S, "talk", "30220-02.htm", when="absent", races=[0, 1, 3, 4], minLevel=11),
        d(S, "talk", "30220-01.htm", when="absent", races=[0, 1, 3, 4]),
        d(S, "talk", "30220-00.htm", when="absent", races=[2]),
        d(S, "event", "30220-03.html", when="any", event="30220-03"),
        d(S, "event", "30220-04.html", when="any", event="30220-04"),
        d(S, "event", "30220-05.htm", when="absent", event="30220-05",
          races=[0, 1, 3, 4], minLevel=11, ops=START + [cond(1)]),
        d(S, "talk", "30220-07.html", cond=1, hasItems=poems, ops=[
            {"op": "give_exp_sp", "exp": 21643, "sp": 943},
            {"op": "give_adena", "count": 13890},
            {"op": "complete"},
        ]),
        d(S, "talk", "30220-06.html", cond=1),
    ]
    # One kill dialog per mob: OnKill matches a single dialog; Mobius rolls all four independently.
    poem_ops = [drop(poem, ch, limit=1) for poem, ch in chances.items()]
    for mob in (20372, 20373):
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=poem_ops))
    return {
        "id": Q, "name": "Legacy of the Poet", "repeatable": False, "abortable": True,
        "questItems": poems, "dialogs": dialogs,
    }


new = [q105(), q159(), q162(), q163()]
questio.upsert_quests(new)
for q in new:
    print(f"quest {q['id']}: {len(q['dialogs'])} dialogs")
