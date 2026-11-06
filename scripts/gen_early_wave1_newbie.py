#!/usr/bin/env python3
"""Port Mobius CT0 early newbie/delivery quests into data/quests/ pack (questio.upsert).

Quest IDs: 1, 2, 4, 5, 6, 7, 8, 9, 10, 153, 155, 166, 168

Source: ref/mobius-ct0-interlude/dist/game/data/scripts/quests/Q00xxx_* (Java + HTML).
Alternate path: ref/mobius-ct0-interlude/data/scripts/quests when dist is absent.

After: python3 scripts/gen_early_wave1_newbie.py
Then: python3 scripts/gen_drop_items.py && python3 scripts/gen_quest_item_flags.py
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

IDS = (1, 2, 4, 5, 6, 7, 8, 9, 10, 153, 155, 166, 168)

give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
# QuestCatalog requires take_item count >= 1 (Mobius takeItems(_, -1) = one stack here).
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
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


def j(x):
    """JSON object keys as strings (atLeast keys in catalog)."""
    if isinstance(x, dict):
        return {str(k): v for k, v in x.items()}
    return x


def q001():
    Q, d = 1, html_loader("Q00001_LettersOfLove", 1)
    DARIN, ROXXY, BAULRO = 30048, 30006, 30033
    LETTER, KERCHIEF, RECEIPT, POTION = 687, 688, 1079, 1080
    NECK = 906
    dialogs = [
        d(DARIN, "talk", "30048-02.html", when="absent", minLevel=2),
        d(DARIN, "talk", "30048-01.html", when="absent"),
        d(DARIN, "event", "30048-03.html", when="absent", event="30048-03"),
        d(DARIN, "event", "30048-04.html", when="absent", event="30048-04"),
        d(DARIN, "event", "30048-05.html", when="absent", event="30048-05"),
        d(DARIN, "event", "30048-06.htm", when="absent", event="30048-06", minLevel=2,
          ops=START + [give(LETTER), cond(1)]),
        d(DARIN, "talk", "30048-07.html", cond=1, hasItem=LETTER),
        d(ROXXY, "talk", "30006-01.html", cond=1, hasItem=LETTER, lacksItems=[KERCHIEF],
          ops=[take(LETTER), give(KERCHIEF), cond(2)]),
        d(DARIN, "talk", "30048-08.html", cond=2, hasItem=KERCHIEF,
          ops=[take(KERCHIEF), give(RECEIPT), cond(3)]),
        d(ROXXY, "talk", "30006-02.html", cond=2, hasItem=KERCHIEF),
        d(DARIN, "talk", "30048-09.html", cond=3, hasItem=RECEIPT),
        d(DARIN, "talk", "30048-09.html", cond=3, lacksItems=[RECEIPT, POTION]),
        d(ROXXY, "talk", "30006-03.html", cond=3, hasAny=[RECEIPT, POTION]),
        d(BAULRO, "talk", "30033-01.html", cond=3, hasItem=RECEIPT, lacksItems=[POTION],
          ops=[take(RECEIPT), give(POTION), cond(4)]),
        d(BAULRO, "talk", "30033-02.html", cond=3, hasItem=POTION, lacksItems=[RECEIPT]),
        d(BAULRO, "talk", "30033-02.html", cond=4, hasItem=POTION),
        d(ROXXY, "talk", "30006-03.html", cond=4, hasItem=POTION),
        d(DARIN, "talk", "30048-10.html", cond=4, hasItem=POTION,
          ops=[take(POTION), give(NECK), {"op": "give_exp_sp", "exp": 5672, "sp": 446},
               {"op": "give_adena", "count": 2466}, {"op": "complete"}]),
    ]
    return {
        "id": Q,
        "name": "Letters of Love",
        "repeatable": False,
        "abortable": True,
        "questItems": [LETTER, KERCHIEF, RECEIPT, POTION],
        "dialogs": dialogs,
    }


def q002():
    Q, d = 2, html_loader("Q00002_WhatWomenWant", 2)
    ARUJIEN, MIRABEL, HERBIEL, GREENIS = 30223, 30146, 30150, 30157
    L1, L2, L3 = 1092, 1093, 1094
    POETRY, GREEN_LETTER, EARRING = 689, 693, 113
    dialogs = [
        d(ARUJIEN, "talk", "30223-02.htm", when="absent", races=[0, 1], minLevel=2),
        d(ARUJIEN, "talk", "30223-01.html", when="absent", races=[0, 1]),
        d(ARUJIEN, "talk", "30223-00.htm", when="absent"),
        d(ARUJIEN, "event", "30223-04.htm", when="absent", event="30223-04",
          races=[0, 1], minLevel=2, ops=START + [give(L1), cond(1)]),
        d(ARUJIEN, "event", "30223-08.html", when="started", event="30223-08", cond=3,
          hasItem=L3, ops=[take(L3), give(POETRY), cond(4)]),
        d(ARUJIEN, "event", "30223-09.html", when="started", event="30223-09", cond=4, hasItem=POETRY,
          ops=[{"op": "give_adena", "count": 450}, {"op": "complete"},
               {"op": "give_exp_sp", "exp": 4254, "sp": 335}, {"op": "give_adena", "count": 1850}]),
        d(ARUJIEN, "event", "30223-03.html", when="started", event="30223-03"),
        d(ARUJIEN, "talk", "30223-05.html", cond=1),
        d(ARUJIEN, "talk", "30223-06.html", cond=2),
        d(ARUJIEN, "talk", "30223-07.html", cond=3),
        d(ARUJIEN, "talk", "30223-10.html", cond=4),
        d(ARUJIEN, "talk", "30223-11.html", cond=5, hasItem=GREEN_LETTER,
          ops=[give(EARRING), {"op": "give_exp_sp", "exp": 4254, "sp": 335},
               {"op": "give_adena", "count": 1850}, {"op": "complete"}]),
        d(MIRABEL, "talk", "30146-01.html", cond=1, hasItem=L1,
          ops=[take(L1), give(L2), cond(2)]),
        d(MIRABEL, "talk", "30146-02.html", cond=1),
        d(MIRABEL, "talk", "30146-02.html", cond=2),
        d(MIRABEL, "talk", "30146-02.html", cond=3),
        d(MIRABEL, "talk", "30146-02.html", cond=4),
        d(MIRABEL, "talk", "30146-02.html", cond=5),
        d(HERBIEL, "talk", "30150-01.html", cond=2, hasItem=L2,
          ops=[take(L2), give(L3), cond(3)]),
        d(HERBIEL, "talk", "30150-02.html", cond=2),
        d(HERBIEL, "talk", "30150-02.html", cond=3),
        d(HERBIEL, "talk", "30150-02.html", cond=4),
        d(HERBIEL, "talk", "30150-02.html", cond=5),
        d(GREENIS, "talk", "30157-02.html", cond=4, hasItem=POETRY,
          ops=[take(POETRY), give(GREEN_LETTER), cond(5)]),
        d(GREENIS, "talk", "30157-03.html", cond=5),
        d(GREENIS, "talk", "30157-01.html", cond=1),
        d(GREENIS, "talk", "30157-01.html", cond=2),
        d(GREENIS, "talk", "30157-01.html", cond=3),
    ]
    return {
        "id": Q,
        "name": "What Women Want",
        "repeatable": False,
        "abortable": True,
        "questItems": [L1, L2, L3, POETRY, GREEN_LETTER],
        "dialogs": dialogs,
    }


def _q4_collection_dialogs(d, npc_id: int, item_id: int, others: list[int]):
    """Mobius giveItem: already have -> -02; else give; if all registered items held -> cond 2."""
    return [
        d(npc_id, "talk", f"{npc_id}-02.html", cond=1, hasItem=item_id),
        d(npc_id, "talk", f"{npc_id}-01.html", cond=1, lacksItems=[item_id], hasItems=others,
          ops=[give(item_id), cond(2)]),
        d(npc_id, "talk", f"{npc_id}-01.html", cond=1, lacksItems=[item_id], ops=[give(item_id)]),
    ]


def q004():
    Q, d = 4, html_loader("Q00004_LongLiveThePaagrioLord", 4)
    KUNAI, USKA, GROOKIN = 30559, 30560, 30562
    VARKEES, NAKUSIN, HESTUI, URUTU = 30566, 30578, 30585, 30587
    CLUB = 4
    HONEY, CLOAK, AXE, SKULL, DUST, ORB = 1541, 1542, 1543, 1544, 1545, 1546
    all_six = [HONEY, CLOAK, AXE, SKULL, DUST, ORB]

    def others(except_id: int) -> list[int]:
        return [i for i in all_six if i != except_id]

    dialogs = [
        d(NAKUSIN, "talk", "30578-02.htm", when="absent", races=[3], minLevel=2),
        d(NAKUSIN, "talk", "30578-01.htm", when="absent", races=[3]),
        d(NAKUSIN, "talk", "30578-00.htm", when="absent"),
        d(NAKUSIN, "event", "30578-03.htm", when="absent", event="30578-03", races=[3], minLevel=2, ops=START + [cond(1)]),
        d(NAKUSIN, "event", "30578-05.html", when="started", event="30578-05"),
        d(NAKUSIN, "talk", "30578-04.html", cond=1),
        d(NAKUSIN, "talk", "30578-06.html", cond=2, atLeast=j({i: 1 for i in all_six}),
          ops=[give(CLUB), {"op": "give_exp_sp", "exp": 4254, "sp": 335},
               {"op": "give_adena", "count": 1850}, {"op": "complete"}]),
    ]
    for npc_id, item_id in (
        (VARKEES, HONEY),
        (URUTU, ORB),
        (HESTUI, CLOAK),
        (KUNAI, DUST),
        (USKA, SKULL),
        (GROOKIN, AXE),
    ):
        dialogs.extend(_q4_collection_dialogs(d, npc_id, item_id, others(item_id)))
    return {
        "id": Q,
        "name": "Long Live the Pa'agrio Lord",
        "repeatable": False,
        "abortable": True,
        "questItems": all_six,
        "dialogs": dialogs,
    }


def _q5_npc_take_item(d, npc_id: int, item_id: int, rest: list[int]):
    """Miner chain: give one item; if LIST + rest now all held -> cond 2."""
    need_for_cond2 = [1547] + rest  # BOLTERS_LIST + the four others (not item_id before give)
    # After this NPC's item is given, player holds LIST + item_id + rest subset.
    return [
        d(npc_id, "talk", f"{npc_id}-02.html", cond=1, hasItem=item_id),
        d(npc_id, "talk", f"{npc_id}-01.html", cond=1, lacksItems=[item_id], hasItems=need_for_cond2,
          ops=[give(item_id), cond(2)]),
        d(npc_id, "talk", f"{npc_id}-01.html", cond=1, lacksItems=[item_id], ops=[give(item_id)]),
    ]


def q005():
    Q, d = 5, html_loader("Q00005_MinersFavor", 5)
    BOLTER, SHARI, GARITA, REED, BRUNON = 30554, 30517, 30518, 30520, 30526
    LIST, BOOTS, PICK, POWDER, BEER, SOCKS = 1547, 1548, 1549, 1550, 1551, 1552
    NECK = 906

    dialogs = [
        d(BOLTER, "talk", "30554-02.htm", when="absent", minLevel=2),
        d(BOLTER, "talk", "30554-01.html", when="absent"),
        d(BOLTER, "event", "30554-03.htm", when="absent", event="30554-03", minLevel=2,
          ops=START + [give(LIST), give(SOCKS), cond(1)]),
        d(BOLTER, "event", "30554-05.html", when="started", event="30554-05"),
        d(BOLTER, "talk", "30554-04.html", cond=1),
        d(BOLTER, "talk", "30554-06.html", cond=2,
          hasItems=[LIST, BOOTS, PICK, POWDER, BEER],
          ops=[take(LIST), take(BOOTS), take(PICK), take(POWDER), take(BEER),
               {"op": "give_adena", "count": 2466}, {"op": "give_exp_sp", "exp": 5672, "sp": 446},
               give(NECK), {"op": "complete"}]),
        d(BRUNON, "talk", "30526-03.html", cond=1, hasItem=PICK),
        d(BRUNON, "talk", "30526-01.html", cond=1, lacksItems=[PICK], hasItems=[LIST, SOCKS]),
        d(BRUNON, "event", "30526-02.html", when="started", event="30526-02", cond=1, hasItem=SOCKS,
          lacksItems=[PICK], hasItems=[LIST, BOOTS, POWDER, BEER],
          ops=[take(SOCKS), give(PICK), cond(2)]),
        d(BRUNON, "event", "30526-02.html", when="started", event="30526-02", cond=1, hasItem=SOCKS,
          lacksItems=[PICK], ops=[take(SOCKS), give(PICK)]),
        d(BRUNON, "event", "30526-04.html", when="started", event="30526-02", cond=1, lacksItems=[SOCKS]),
    ]
    dialogs.extend(_q5_npc_take_item(d, REED, BEER, [BOOTS, PICK, POWDER]))
    dialogs.extend(_q5_npc_take_item(d, SHARI, POWDER, [BOOTS, PICK, BEER]))
    dialogs.extend(_q5_npc_take_item(d, GARITA, BOOTS, [PICK, POWDER, BEER]))
    return {
        "id": Q,
        "name": "Miner's Favor",
        "repeatable": False,
        "abortable": True,
        "questItems": [LIST, BOOTS, PICK, POWDER, BEER, SOCKS],
        "dialogs": dialogs,
    }


def q006():
    Q, d = 6, html_loader("Q00006_StepIntoTheFuture", 6)
    ROXXY, BAULRO, COLLIN = 30006, 30033, 30311
    LETTER, SOE, MARK = 7571, 7559, 7570
    dialogs = [
        d(ROXXY, "talk", "30006-02.htm", when="absent", races=[0], minLevel=3),
        d(ROXXY, "talk", "30006-01.html", when="absent", races=[0]),
        d(ROXXY, "event", "30006-03.htm", when="absent", event="30006-03", races=[0], minLevel=3, ops=START + [cond(1)]),
        d(ROXXY, "event", "30006-06.html", when="started", event="30006-06", cond=3,
          ops=[give(SOE), give(MARK), {"op": "complete"}]),
        d(BAULRO, "event", "30033-02.html", when="started", event="30033-02", cond=1,
          ops=[cond(2), give(LETTER)]),
        d(COLLIN, "event", "30311-02.html", when="started", event="30311-02", cond=2, hasItem=LETTER,
          ops=[take(LETTER), cond(3)]),
        d(ROXXY, "talk", "30006-04.html", cond=1),
        d(ROXXY, "talk", "30006-05.html", cond=3),
        d(BAULRO, "talk", "30033-01.html", cond=1),
        d(BAULRO, "talk", "30033-03.html", cond=2),
        d(COLLIN, "talk", "30311-01.html", cond=2),
        d(COLLIN, "talk", "30311-04.html", cond=3),
        d(COLLIN, "event", "30311-03.html", when="started", event="30311-02", cond=2, lacksItems=[LETTER]),
    ]
    return {
        "id": Q,
        "name": "Step Into the Future",
        "repeatable": False,
        "abortable": True,
        "questItems": [LETTER],
        "dialogs": dialogs,
    }


def q007():
    Q, d = 7, html_loader("Q00007_ATripBegins", 7)
    MIRABEL, ARIEL, ASTERIOS = 30146, 30148, 30154
    REC, SOE, MARK = 7572, 7559, 7570
    dialogs = [
        d(MIRABEL, "talk", "30146-01.htm", when="absent", races=[1], minLevel=3),
        d(MIRABEL, "talk", "30146-02.html", when="absent", races=[1]),
        d(MIRABEL, "event", "30146-03.htm", when="absent", event="30146-03", races=[1], minLevel=3, ops=START + [cond(1)]),
        d(MIRABEL, "event", "30146-06.html", when="started", event="30146-06", cond=3,
          ops=[give(SOE), give(MARK), {"op": "complete"}]),
        d(ARIEL, "event", "30148-02.html", when="started", event="30148-02", cond=1,
          ops=[cond(2), give(REC)]),
        d(ASTERIOS, "event", "30154-02.html", when="started", event="30154-02", cond=2, hasItem=REC,
          ops=[take(REC), cond(3)]),
        d(MIRABEL, "talk", "30146-04.html", cond=1),
        d(MIRABEL, "talk", "30146-05.html", cond=3),
        d(ARIEL, "talk", "30148-01.html", cond=1),
        d(ARIEL, "talk", "30148-03.html", cond=2),
        d(ASTERIOS, "talk", "30154-01.html", cond=2),
        d(ASTERIOS, "talk", "30154-04.html", cond=3),
        d(ASTERIOS, "event", "30154-03.html", when="started", event="30154-02", cond=2, lacksItems=[REC]),
    ]
    return {
        "id": Q,
        "name": "A Trip Begins",
        "repeatable": False,
        "abortable": True,
        "questItems": [REC],
        "dialogs": dialogs,
    }


def q008():
    Q, d = 8, html_loader("Q00008_AnAdventureBegins", 8)
    JASMINE, ROSELYN, HARNE = 30134, 30355, 30144
    NOTE, SOE, MARK = 7573, 7559, 7570
    dialogs = [
        d(JASMINE, "talk", "30134-02.htm", when="absent", races=[2], minLevel=3),
        d(JASMINE, "talk", "30134-01.html", when="absent", races=[2]),
        d(JASMINE, "event", "30134-03.htm", when="absent", event="30134-03", races=[2], minLevel=3, ops=START + [cond(1)]),
        d(JASMINE, "event", "30134-06.html", when="started", event="30134-06", cond=3,
          ops=[give(SOE), give(MARK), {"op": "complete"}]),
        d(ROSELYN, "event", "30355-02.html", when="started", event="30355-02", cond=1,
          ops=[cond(2), give(NOTE)]),
        d(HARNE, "event", "30144-02.html", when="started", event="30144-02", cond=2, hasItem=NOTE,
          ops=[take(NOTE), cond(3)]),
        d(JASMINE, "talk", "30134-04.html", cond=1),
        d(JASMINE, "talk", "30134-05.html", cond=3),
        d(ROSELYN, "talk", "30355-01.html", cond=1),
        d(ROSELYN, "talk", "30355-03.html", cond=2),
        d(HARNE, "talk", "30144-01.html", cond=2),
        d(HARNE, "talk", "30144-04.html", cond=3),
        d(HARNE, "event", "30144-03.html", when="started", event="30144-02", cond=2, lacksItems=[NOTE]),
    ]
    return {
        "id": Q,
        "name": "An Adventure Begins",
        "repeatable": False,
        "abortable": True,
        "questItems": [NOTE],
        "dialogs": dialogs,
    }


def q009():
    Q, d = 9, html_loader("Q00009_IntoTheCityOfHumans", 9)
    PETUKAI, TANAPI, TAMIL = 30583, 30571, 30576
    SOE, MARK = 7559, 7570
    dialogs = [
        d(PETUKAI, "talk", "30583-01.htm", when="absent", races=[3], minLevel=3),
        d(PETUKAI, "talk", "30583-02.html", when="absent", races=[0, 1, 2, 4], minLevel=3),
        d(PETUKAI, "talk", "30583-03.html", when="absent", races=[0, 1, 2, 4]),
        d(PETUKAI, "event", "30583-04.htm", when="absent", event="30583-04", races=[3], minLevel=3, ops=START + [cond(1)]),
        d(PETUKAI, "talk", "30583-05.html", cond=1),
        d(TANAPI, "event", "30571-02.html", when="started", event="30571-02", cond=1, ops=[cond(2)]),
        d(TAMIL, "event", "30576-02.html", when="started", event="30576-02", cond=2,
          ops=[give(MARK), give(SOE), {"op": "complete"}]),
        d(TANAPI, "talk", "30571-01.html", cond=1),
        d(TANAPI, "talk", "30571-03.html", cond=2),
        d(TAMIL, "talk", "30576-01.html", cond=2),
    ]
    return {
        "id": Q,
        "name": "Into the City of Humans",
        "repeatable": False,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def q010():
    Q, d = 10, html_loader("Q00010_IntoTheWorld", 10)
    REED, BALANKI, GERALD = 30520, 30533, 30650
    NECK, SOE, MARK = 7574, 7559, 7570
    dialogs = [
        d(BALANKI, "talk", "30533-01.htm", when="absent", races=[4], minLevel=3),
        d(BALANKI, "talk", "30533-02.html", when="absent"),
        d(BALANKI, "event", "30533-03.htm", when="absent", event="30533-03", races=[4], minLevel=3, ops=START + [cond(1)]),
        d(BALANKI, "event", "30533-06.html", when="started", event="30533-06", cond=4,
          ops=[give(MARK), give(SOE), {"op": "complete"}]),
        d(REED, "event", "30520-02.html", when="started", event="30520-02", cond=1,
          ops=[give(NECK), cond(2)]),
        d(REED, "event", "30520-05.html", when="started", event="30520-05", cond=3, ops=[cond(4)]),
        d(GERALD, "event", "30650-02.html", when="started", event="30650-02", cond=2, hasItem=NECK,
          ops=[take(NECK), cond(3)]),
        d(BALANKI, "talk", "30533-04.html", cond=1),
        d(BALANKI, "talk", "30533-05.html", cond=4),
        d(REED, "talk", "30520-01.html", cond=1),
        d(REED, "talk", "30520-03.html", cond=2),
        d(REED, "talk", "30520-04.html", cond=3),
        d(REED, "talk", "30520-06.html", cond=4),
        d(GERALD, "talk", "30650-01.html", cond=2),
        d(GERALD, "talk", "30650-04.html", cond=3),
        d(GERALD, "event", "30650-03.html", when="started", event="30650-02", cond=2, lacksItems=[NECK]),
    ]
    return {
        "id": Q,
        "name": "Into the World",
        "repeatable": False,
        "abortable": True,
        "questItems": [NECK],
        "dialogs": dialogs,
    }


def _q153_deliver(d, npc_id: int, take_id: int, give_id: int, html_have: str, html_done: str, ss_extra: list | None = None):
    """Mobius: each hand-in takes the parcel and gives one receipt; cond 2 when all three receipts held."""
    RJ, RS, RR = 1016, 1017, 1018
    ss = ss_extra or []
    a, b = [x for x in (RJ, RS, RR) if x != give_id]
    base = [take(take_id), give(give_id)] + ss
    complete = base + [cond(2)]
    return [
        d(npc_id, "talk", html_have, cond=1, hasItem=take_id, hasItems=[a, b], ops=complete),
        d(npc_id, "talk", html_have, cond=1, hasItem=take_id, hasItems=[a], lacksItems=[b], ops=base),
        d(npc_id, "talk", html_have, cond=1, hasItem=take_id, hasItems=[b], lacksItems=[a], ops=base),
        d(npc_id, "talk", html_have, cond=1, hasItem=take_id, lacksItems=[a, b], ops=base),
        d(npc_id, "talk", html_done, cond=1, lacksItems=[take_id]),
    ]


def q153():
    Q, d = 153, html_loader("Q00153_DeliverGoods", 153)
    JACKSON, SILVIA, ARNOLD, RANT = 30002, 30003, 30041, 30054
    DL, BOX, CLOTH, POT = 1012, 1013, 1014, 1015
    RJ, RS, RR = 1016, 1017, 1018
    RING, SS = 875, 1835
    dialogs = [
        d(ARNOLD, "talk", "30041-01.htm", when="absent", minLevel=2),
        d(ARNOLD, "talk", "30041-00.htm", when="absent"),
        d(ARNOLD, "event", "30041-02.html", when="absent", event="30041-02", minLevel=2,
          ops=START + [give(DL), give(BOX), give(CLOTH), give(POT), cond(1)]),
        d(ARNOLD, "talk", "30041-03.html", cond=1),
        d(ARNOLD, "talk", "30041-04.html", cond=2, hasItems=[DL, RJ, RS, RR],
          ops=[take(DL), take(RJ), take(RS), take(RR), give(RING, 2), give(SS, 3),
               {"op": "give_exp_sp", "exp": 600, "sp": 0}, {"op": "complete"}]),
    ]
    dialogs.extend(_q153_deliver(d, JACKSON, BOX, RJ, "30002-01.html", "30002-02.html"))
    dialogs.extend(_q153_deliver(d, SILVIA, CLOTH, RS, "30003-01.html", "30003-02.html", ss_extra=[give(SS, 3)]))
    dialogs.extend(_q153_deliver(d, RANT, POT, RR, "30054-01.html", "30054-02.html"))
    return {
        "id": Q,
        "name": "Deliver Goods",
        "repeatable": False,
        "abortable": True,
        "questItems": [DL, BOX, CLOTH, POT, RJ, RS, RR],
        "dialogs": dialogs,
    }


def q155():
    Q, d = 155, html_loader("Q00155_FindSirWindawood", 155)
    ABELLOS, SIR = 30042, 30311
    LETTER, HASTE = 1019, 734
    dialogs = [
        d(ABELLOS, "talk", "30042-02.htm", when="absent", minLevel=3),
        d(ABELLOS, "talk", "30042-01.htm", when="absent"),
        d(ABELLOS, "event", "30042-03.htm", when="absent", event="30042-03", minLevel=3, ops=START + [give(LETTER), cond(1)]),
        d(ABELLOS, "talk", "30042-04.html", when="started"),
        d(SIR, "talk", "30311-01.html", when="started", hasItem=LETTER,
          ops=[take(LETTER), give(HASTE), {"op": "complete"}]),
    ]
    return {
        "id": Q,
        "name": "Find Sir Windawood",
        "repeatable": False,
        "abortable": True,
        "questItems": [LETTER],
        "dialogs": dialogs,
    }


def _q166_artifact(d, npc_id: int, item_id: int, others: list[int], finish_cond: bool):
    """finish_cond: Mobius sets cond 2 only when all three artifact types are held (last pickup)."""
    finish = [cond(2)] if finish_cond else []
    return [
        d(npc_id, "talk", f"{npc_id}-02.html", cond=1, hasItem=item_id),
        d(npc_id, "talk", f"{npc_id}-01.html", cond=1, lacksItems=[item_id], hasItems=others,
          ops=[give(item_id)] + finish),
        d(npc_id, "talk", f"{npc_id}-01.html", cond=1, lacksItems=[item_id], ops=[give(item_id)]),
    ]


def q166():
    Q, d = 166, html_loader("Q00166_MassOfDarkness", 166)
    UNDRIAS, IRIA, DORANKUS, TRUDY = 30130, 30135, 30139, 30143
    ULETTER, DAGGER, WINE, SCRIPT = 1088, 1089, 1090, 1091
    arts = [DAGGER, WINE, SCRIPT]

    def other_two(except_id: int):
        return [i for i in arts if i != except_id]

    dialogs = [
        d(UNDRIAS, "talk", "30130-02.htm", when="absent", races=[2], minLevel=2),
        d(UNDRIAS, "talk", "30130-01.htm", when="absent", races=[2]),
        d(UNDRIAS, "talk", "30130-00.htm", when="absent"),
        d(UNDRIAS, "event", "30130-03.htm", when="absent", event="30130-03", races=[2], minLevel=2,
          ops=START + [give(ULETTER), cond(1)]),
        d(UNDRIAS, "talk", "30130-05.html", cond=2, hasItems=[ULETTER, DAGGER, WINE, SCRIPT],
          ops=[take(ULETTER), take(DAGGER), take(WINE), take(SCRIPT),
               {"op": "give_exp_sp", "exp": 5672, "sp": 466}, {"op": "give_adena", "count": 2966},
               {"op": "complete"}]),
        d(UNDRIAS, "talk", "30130-04.html", cond=1),
        d(UNDRIAS, "talk", "30130-04.html", cond=2, lacksItems=arts),
    ]
    for npc_id, item_id, last in ((IRIA, DAGGER, False), (DORANKUS, WINE, False), (TRUDY, SCRIPT, True)):
        dialogs.extend(_q166_artifact(d, npc_id, item_id, other_two(item_id), last))
    return {
        "id": Q,
        "name": "Mass of Darkness",
        "repeatable": False,
        "abortable": True,
        "questItems": [ULETTER, DAGGER, WINE, SCRIPT],
        "dialogs": dialogs,
    }


def q168():
    Q, d = 168, html_loader("Q00168_DeliverSupplies", 168)
    JENNA, ROSELYN, KRISTIN, HARANT = 30349, 30355, 30357, 30360
    JLETTER = 1153
    B1, B2, B3, SWORD = 1154, 1155, 1156, 1157
    dialogs = [
        d(JENNA, "talk", "30349-02.htm", when="absent", races=[2], minLevel=3),
        d(JENNA, "talk", "30349-01.htm", when="absent", races=[2]),
        d(JENNA, "talk", "30349-00.htm", when="absent"),
        d(JENNA, "event", "30349-03.htm", when="absent", event="30349-03", races=[2], minLevel=3,
          ops=START + [give(JLETTER), cond(1)]),
        d(JENNA, "talk", "30349-04.html", cond=1, hasItem=JLETTER),
        d(JENNA, "talk", "30349-05.html", cond=2, hasItems=[B1, B2, B3], ops=[take(B1), cond(3)]),
        d(JENNA, "talk", "30349-07.html", cond=3, hasAny=[B2, B3]),
        d(JENNA, "talk", "30349-07.html", cond=4, atLeast=j({SWORD: 2}),
          ops=[take(SWORD, 2), {"op": "give_adena", "count": 820}, {"op": "complete"}]),
        d(HARANT, "talk", "30360-01.html", cond=1, hasItem=JLETTER,
          ops=[take(JLETTER), give(B1), give(B2), give(B3), cond(2)]),
        d(HARANT, "talk", "30360-02.html", cond=2),
        d(ROSELYN, "talk", "30355-01.html", cond=3, hasItem=B2, hasItems=[B3, SWORD],
          ops=[take(B2), give(SWORD), cond(4)]),
        d(KRISTIN, "talk", "30357-01.html", cond=3, hasItem=B3, hasItems=[B2, SWORD],
          ops=[take(B3), give(SWORD), cond(4)]),
        d(ROSELYN, "talk", "30355-01.html", cond=3, hasItem=B2, lacksItems=[SWORD],
          ops=[take(B2), give(SWORD)]),
        d(KRISTIN, "talk", "30357-01.html", cond=3, hasItem=B3, lacksItems=[SWORD],
          ops=[take(B3), give(SWORD)]),
        d(ROSELYN, "talk", "30355-02.html", cond=3, lacksItems=[B2], hasItem=SWORD),
        d(KRISTIN, "talk", "30357-02.html", cond=3, lacksItems=[B3], hasItem=SWORD),
    ]
    return {
        "id": Q,
        "name": "Deliver Supplies",
        "repeatable": False,
        "abortable": True,
        "questItems": [JLETTER, B1, B2, B3, SWORD],
        "dialogs": dialogs,
    }


def main():
    builders = {
        1: q001,
        2: q002,
        4: q004,
        5: q005,
        6: q006,
        7: q007,
        8: q008,
        9: q009,
        10: q010,
        153: q153,
        155: q155,
        166: q166,
        168: q168,
    }
    new = [builders[i]() for i in IDS]
    questio.upsert_quests(new)
    for q in new:
        print(f"quest {q['id']}: {len(q['dialogs'])} dialogs")


if __name__ == "__main__":
    main()
