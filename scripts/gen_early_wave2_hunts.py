#!/usr/bin/env python3
"""Upsert Mobius CT0 early hunt/collect quests (MIN_LEVEL<=21) into data/quests/ pack (questio.upsert).

IDs: 151, 152, 157, 164, 165, 169, 170, 257, 258, 260, 263, 264, 265, 266, 271, 272, 273, 274

Source: ref/mobius-ct0-interlude/dist/game/data/scripts/quests/*/ Java + HTML.

Patterns: gen_elven_hunt_quests.py / gen_q102.py — rewrite bypass links to quest_event:QID:event (strip .htm/.html).

Chance: getRandom(100)<N -> N/100.0; getRandom(10)<4 -> 0.4; getRandom(5)==0 -> 0.2; getRandom(3)<k -> k/3.0.

OnKill: one dialog per (quest, npcId); multiple independent rolls for the same mob go in that dialog's ops list.

Q169: Mobius uses if/else-if with two getRandom(10) calls; one kill dialog — perfect skull first (20%), else cracked (30%) while no perfect yet.

Q258: Mobius picks random armor at turn-in (getRandom(16)); engine has no give_random — fixed reward Cotton Shirt (390), highest probability branch.

Q264: Mobius tiers wolf-claw amounts via ItemHolder thresholds + giveItemRandomly; simplified to one drop_item per mob (see comments).

Q266: Mobius two-roll kill + complex reward tiers; kill uses approximate single drop; turn-in reward fixed to Glass Shard + Lesser Healing (most common tier).

Q271: Mobius sometimes gives 2 fangs per kill (25%, if count<49); encoded as 1 fang/kill for simplicity (e2e test grinds to 50).

Race ids: Human 0, Elf 1, Dark Elf 2, Orc 3, Dwarf 4 (QuestCatalog / Mobius order).

Turn-ins use complete + questItems wipe (no take_item -1); only exact take_item for mid-flow trades.

After merge run: python3 scripts/gen_early_wave2_hunts.py && python3 scripts/gen_drop_items.py && python3 scripts/gen_quest_item_flags.py
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


def drop(i, chance, limit=0, at=None, count=1):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


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


def q151():
    """Cure for Fever — poison sac 1/5 per eligible kill, then medicine hand-in."""
    Q, d = 151, html_loader("Q00151_CureForFever", 151)
    ELLIAS, YOHANES = 30050, 30032
    MOBS = (20103, 20106, 20108)
    POISON, MED, SHIELD = 703, 704, 102
    dialogs = [
        d(ELLIAS, "talk", "30050-01.htm", when="absent", maxLevel=14),
        d(ELLIAS, "talk", "30050-02.htm", when="absent", minLevel=15),
        d(ELLIAS, "event", "30050-03.htm", when="absent", event="30050-03", minLevel=15, ops=START + [cond(1)]),
        d(ELLIAS, "talk", "30050-04.html", cond=1),
        d(ELLIAS, "talk", "30050-05.html", cond=1, hasItem=POISON),
        d(ELLIAS, "talk", "30050-06.html", cond=3, hasItem=MED, ops=[
            give(SHIELD), {"op": "give_exp_sp", "exp": 13106, "sp": 613}, {"op": "complete"},
        ]),
        d(YOHANES, "talk", "30032-01.html", cond=2, hasItem=POISON, ops=[
            take(POISON, 1), give(MED), cond(3),
        ]),
        d(YOHANES, "talk", "30032-02.html", cond=3, hasItem=MED),
    ]
    for mob in MOBS:
        dialogs.append(
            d(mob, "kill", None, cond=1, lacksItems=[POISON], raw="", ops=[drop(POISON, 1.0 / 5, limit=1, at=2)]),
        )
    return {
        "id": Q,
        "name": "Cure for Fever",
        "repeatable": False,
        "abortable": True,
        "questItems": [POISON, MED],
        "dialogs": dialogs,
    }


def q152():
    """Shards of Golem — receipts, 30% shard x5, toolbox return."""
    Q, d = 152, html_loader("Q00152_ShardsOfGolem", 152)
    HARRYS, ALTRAN = 30035, 30283
    R1, R2, SHARD, BOX, ARMOR = 1008, 1009, 1010, 1011, 23
    dialogs = [
        d(HARRYS, "talk", "30035-01.htm", when="absent", maxLevel=9),
        d(HARRYS, "talk", "30035-02.htm", when="absent", minLevel=10),
        d(HARRYS, "event", "30035-03.htm", when="absent", event="30035-03", minLevel=10, ops=START + [give(R1), cond(1)]),
        d(HARRYS, "talk", "30035-04a.html", cond=1, hasItem=R1),
        d(HARRYS, "talk", "30035-04.html", cond=2, hasItem=R2),
        d(HARRYS, "talk", "30035-04.html", cond=3, hasItem=R2),
        d(HARRYS, "talk", "30035-05.html", cond=4, hasItems=[R2, BOX], ops=[
            give(ARMOR), {"op": "give_exp_sp", "exp": 5000, "sp": 0}, {"op": "complete"},
        ]),
        d(ALTRAN, "event", "30283-02.html", when="started", event="30283-02", cond=1, hasItem=R1, ops=[
            take(R1, 1), give(R2), cond(2),
        ]),
        d(ALTRAN, "talk", "30283-01.html", cond=1, hasItem=R1),
        d(ALTRAN, "talk", "30283-03.html", cond=2, hasItem=R2, lacksItems=[BOX]),
        d(ALTRAN, "talk", "30283-04.html", cond=3, hasItems=[R2, SHARD], atLeast={SHARD: 5}, ops=[
            take(SHARD, 5), give(BOX), cond(4),
        ]),
        d(ALTRAN, "talk", "30283-05.html", cond=4, hasItems=[R2, BOX]),
        d(20016, "kill", None, cond=2, raw="", ops=[drop(SHARD, 0.3, limit=5, at=3)]),
    ]
    return {
        "id": Q,
        "name": "Shards of Golem",
        "repeatable": False,
        "abortable": True,
        "questItems": [R1, R2, SHARD, BOX],
        "dialogs": dialogs,
    }


def q157():
    """Recover Smuggled Goods — 40% ore to 20, buckler reward."""
    Q, d = 157, html_loader("Q00157_RecoverSmuggledGoods", 157)
    WILFORD = 30005
    ORE, BUCKLER = 1024, 20
    dialogs = [
        d(WILFORD, "talk", "30005-01.htm", when="absent", maxLevel=4),
        d(WILFORD, "talk", "30005-02.htm", when="absent", minLevel=5),
        d(WILFORD, "talk", "30005-05.html", when="started"),
        d(WILFORD, "talk", "30005-06.html", cond=2, hasItems=[ORE], atLeast={ORE: 20}, ops=[
            give(BUCKLER), {"op": "complete"},
        ]),
        d(WILFORD, "event", "30005-03.htm", when="any", event="30005-03"),
        d(WILFORD, "event", "30005-04.htm", when="absent", event="30005-04", minLevel=5, ops=START + [cond(1)]),
        d(20121, "kill", None, cond=1, raw="", ops=[drop(ORE, 0.4, limit=20, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Recover Smuggled Goods",
        "repeatable": False,
        "abortable": True,
        "questItems": [ORE],
        "dialogs": dialogs,
    }


def q164():
    """Blood Fiend — non–Dark Elf, Kirunak skull."""
    Q, d = 164, html_loader("Q00164_BloodFiend", 164)
    CREAMEES = 30149
    SKULL = 1044
    dialogs = [
        d(CREAMEES, "talk", "30149-00.htm", when="absent", races=[2]),
        d(CREAMEES, "talk", "30149-02.htm", when="absent", races=[0, 1, 3, 4], maxLevel=20),
        d(CREAMEES, "talk", "30149-03.htm", when="absent", races=[0, 1, 3, 4], minLevel=21),
        d(CREAMEES, "event", "30149-04.htm", when="absent", event="30149-04", races=[0, 1, 3, 4], minLevel=21, ops=START + [cond(1)]),
        d(CREAMEES, "talk", "30149-05.html", cond=1),
        d(CREAMEES, "talk", "30149-06.html", cond=2, hasItem=SKULL, ops=[
            {"op": "give_adena", "count": 42130},
            {"op": "give_exp_sp", "exp": 35637, "sp": 1854}, {"op": "complete"},
        ]),
        d(27021, "kill", None, cond=1, raw="", ops=[give(SKULL), cond(2)]),
    ]
    return {
        "id": Q,
        "name": "Blood Fiend",
        "repeatable": False,
        "abortable": True,
        "questItems": [SKULL],
        "dialogs": dialogs,
    }


def q165():
    """Shilen's Hunt — Dark Elf only, bezoars with per-mob 1/3, 2/3, 2/3, 1/3."""
    Q, d = 165, html_loader("Q00165_ShilensHunt", 165)
    NELSYA = 30348
    BEZOAR, POT = 1160, 1060
    chances = {20456: 1 / 3, 20529: 1 / 3, 20532: 1 / 3, 20536: 2 / 3}
    dialogs = [
        d(NELSYA, "talk", "30348-00.htm", when="absent", races=[0, 1, 3, 4]),
        d(NELSYA, "talk", "30348-01.htm", when="absent", races=[2], maxLevel=2),
        d(NELSYA, "talk", "30348-02.htm", when="absent", races=[2], minLevel=3),
        d(NELSYA, "event", "30348-03.htm", when="absent", event="30348-03", races=[2], minLevel=3, ops=START + [cond(1)]),
        d(NELSYA, "talk", "30348-04.html", cond=1),
        d(NELSYA, "talk", "30348-05.html", cond=2, atLeast={BEZOAR: 13}, ops=[
            give(POT, 5), {"op": "give_exp_sp", "exp": 1000, "sp": 0}, {"op": "complete"},
        ]),
    ]
    for mob, ch in chances.items():
        dialogs.append(
            d(mob, "kill", None, cond=1, raw="", ops=[drop(BEZOAR, ch, limit=13, at=2)]),
        )
    return {
        "id": Q,
        "name": "Shilen's Hunt",
        "repeatable": False,
        "abortable": True,
        "questItems": [BEZOAR],
        "dialogs": dialogs,
    }


def q169():
    """Offspring of Nightmares — DE only; perfect skull (20%) / cracked (30% on 2nd roll) approximated by two drops."""
    Q, d = 169, html_loader("Q00169_OffspringOfNightmares", 169)
    V = 30145
    CRACKED, PERFECT, LEGS = 1030, 1031, 31
    dialogs = [
        d(V, "talk", "30145-00.htm", when="absent", races=[0, 1, 3, 4]),
        d(V, "talk", "30145-01.htm", when="absent", races=[2], maxLevel=14),
        d(V, "talk", "30145-02.htm", when="absent", races=[2], minLevel=15),
        d(V, "event", "30145-03.htm", when="absent", event="30145-03", races=[2], minLevel=15, ops=START + [cond(1)]),
        d(V, "talk", "30145-04.html", cond=1, lacksItems=[PERFECT]),
        d(V, "talk", "30145-05.html", cond=1, hasItem=CRACKED, lacksItems=[PERFECT]),
        d(V, "talk", "30145-06.html", cond=2, hasItem=PERFECT),
        d(V, "event", "30145-07.html", when="started", event="30145-07", cond=2, hasItem=PERFECT, ops=[
            give(LEGS),
            {"op": "give_exp_sp", "exp": 17475, "sp": 818},
            {"op": "give_adena", "count": 17030},  # base; cracked bonus in Mobius is +10 each
            {"op": "complete"},
        ]),
    ]
    for mob in (20025, 20105):
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                lacksItems=[PERFECT],
                raw="",
                ops=[
                    drop(PERFECT, 0.2, limit=1, at=2),
                    drop(CRACKED, 0.3, limit=0),
                ],
            ),
        )
    return {
        "id": Q,
        "name": "Offspring of Nightmares",
        "repeatable": False,
        "abortable": True,
        "questItems": [CRACKED, PERFECT],
        "dialogs": dialogs,
    }


def q170():
    """Dangerous Seduction — DE only, Merkenis crystal."""
    Q, d = 170, html_loader("Q00170_DangerousSeduction", 170)
    VELLIOR = 30305
    CRYSTAL = 1046
    dialogs = [
        d(VELLIOR, "talk", "30305-03.htm", when="absent", races=[0, 1, 3, 4]),
        d(VELLIOR, "talk", "30305-02.htm", when="absent", races=[2], maxLevel=20),
        d(VELLIOR, "talk", "30305-01.htm", when="absent", races=[2], minLevel=21),
        d(VELLIOR, "event", "30305-04.htm", when="absent", event="30305-04", races=[2], minLevel=21, ops=START + [cond(1)]),
        d(VELLIOR, "talk", "30305-05.html", cond=1),
        d(VELLIOR, "talk", "30305-06.html", cond=2, ops=[
            {"op": "give_adena", "count": 102680}, {"op": "give_exp_sp", "exp": 38607, "sp": 4018}, {"op": "complete"},
        ]),
        d(27022, "kill", None, cond=1, raw="", ops=[give(CRYSTAL), cond(2)]),
    ]
    return {
        "id": Q,
        "name": "Dangerous Seduction",
        "repeatable": False,
        "abortable": True,
        "questItems": [CRYSTAL],
        "dialogs": dialogs,
    }


def q257():
    """The Guard is Busy — repeatable Gludio hunt drops + per-item adena."""
    Q, d = 257, html_loader("Q00257_TheGuardIsBusy", 257)
    G = 30039
    MARK, AMULET, NECK, FANG = 1084, 752, 1085, 1086
    dialogs = [
        d(G, "talk", "30039-01.html", when="absent", maxLevel=5),
        d(G, "talk", "30039-02.htm", when="absent", minLevel=6),
        d(G, "event", "30039-03.htm", when="absent", event="30039-03", minLevel=6, ops=START + [give(MARK), cond(1)]),
        d(G, "event", "30039-05.html", when="started", event="30039-05", ops=[{"op": "complete"}]),
        d(G, "event", "30039-06.html", when="started", event="30039-06"),
        d(G, "talk", "30039-04.html", cond=1, lacksItems=[AMULET, NECK, FANG]),
        d(
            G,
            "talk",
            "30039-07.html",
            cond=1,
            hasAny=[AMULET, NECK, FANG],
            ops=[
                {
                    "op": "sell_items",
                    "prices": [
                        {"itemId": AMULET, "adena": 10},
                        {"itemId": NECK, "adena": 20},
                        {"itemId": FANG, "adena": 20},
                    ],
                    "bonusMin": 10,
                    "bonusAdena": 1000,
                }
            ],
        ),
    ]
    # Mobius tries drops in list order; first match wins (two rolls for 20006).
    dialogs.append(
        d(
            20006,
            "kill",
            None,
            cond=1,
            raw="",
            ops=[
                drop(AMULET, 0.2, limit=0, count=2),
                drop(AMULET, 1.0, limit=0),
            ],
        ),
    )
    dialogs.append(d(20093, "kill", None, cond=1, raw="", ops=[drop(NECK, 0.85)]))
    dialogs.append(d(20096, "kill", None, cond=1, raw="", ops=[drop(NECK, 0.95)]))
    dialogs.append(d(20098, "kill", None, cond=1, raw="", ops=[drop(NECK, 1.0)]))
    dialogs.append(d(20130, "kill", None, cond=1, raw="", ops=[drop(AMULET, 0.7)]))
    dialogs.append(d(20131, "kill", None, cond=1, raw="", ops=[drop(AMULET, 0.9)]))
    dialogs.append(d(20132, "kill", None, cond=1, raw="", ops=[drop(FANG, 0.7)]))
    dialogs.append(d(20342, "kill", None, cond=1, raw="", ops=[drop(FANG, 1.0)]))
    dialogs.append(d(20343, "kill", None, cond=1, raw="", ops=[drop(FANG, 0.85)]))
    return {
        "id": Q,
        "name": "The Guard is Busy",
        "repeatable": True,
        "abortable": True,
        "questItems": [MARK, AMULET, NECK, FANG],
        "dialogs": dialogs,
    }


def q258():
    """Bring Wolf Pelts — repeatable; reward fixed to Cotton Shirt (390), Mobius random among 5."""
    Q, d = 258, html_loader("Q00258_BringWolfPelts", 258)
    LECTOR = 30001
    PELT = 702
    dialogs = [
        d(LECTOR, "talk", "30001-01.html", when="absent", maxLevel=2),
        d(LECTOR, "talk", "30001-02.htm", when="absent", minLevel=3),
        d(LECTOR, "event", "30001-03.html", when="absent", event="30001-03", minLevel=3, ops=START + [cond(1)]),
        d(LECTOR, "talk", "30001-04.html", cond=1),
        d(
            LECTOR,
            "talk",
            "30001-05.html",
            cond=2,
            atLeast={PELT: 40},
            ops=[give(390), {"op": "complete"}],
        ),
    ]
    for mob in (20120, 20442):
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(PELT, 1.0, limit=40, at=2)]))
    return {
        "id": Q,
        "name": "Bring Wolf Pelts",
        "repeatable": True,
        "abortable": True,
        "questItems": [PELT],
        "dialogs": dialogs,
    }


def q260():
    """Orc Hunting — Elf repeatable; 50% kill drop; sell_items payout."""
    Q, d = 260, html_loader("Q00260_OrcHunting", 260)
    RAYEN = 30221
    AMU, NECK = 1114, 1115
    mapping = {
        20468: AMU,
        20469: AMU,
        20470: AMU,
        20471: NECK,
        20472: NECK,
        20473: NECK,
    }
    dialogs = [
        d(RAYEN, "talk", "30221-01.html", when="absent", races=[0, 2, 3, 4]),
        d(RAYEN, "talk", "30221-02.html", when="absent", races=[1], maxLevel=5),
        d(RAYEN, "talk", "30221-03.htm", when="absent", races=[1], minLevel=6),
        d(RAYEN, "event", "30221-04.htm", when="absent", event="30221-04", races=[1], minLevel=6, ops=START + [cond(1)]),
        d(RAYEN, "event", "30221-07.html", when="started", event="30221-07", ops=[{"op": "complete"}]),
        d(RAYEN, "event", "30221-08.html", when="started", event="30221-08"),
        d(RAYEN, "talk", "30221-05.html", cond=1, lacksItems=[AMU, NECK]),
        d(
            RAYEN,
            "talk",
            "30221-06.html",
            cond=1,
            hasAny=[AMU, NECK],
            ops=[
                {
                    "op": "sell_items",
                    "prices": [{"itemId": AMU, "adena": 12}, {"itemId": NECK, "adena": 30}],
                    "bonusMin": 10,
                    "bonusAdena": 1000,
                }
            ],
        ),
    ]
    for mob, item in mapping.items():
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(item, 0.5)]))
    return {
        "id": Q,
        "name": "Orc Hunting",
        "repeatable": True,
        "abortable": True,
        "questItems": [AMU, NECK],
        "dialogs": dialogs,
    }


def q263():
    """Orc Subjugation — Dark Elf repeatable."""
    Q, d = 263, html_loader("Q00263_OrcSubjugation", 263)
    K = 30346
    AMU, NECK = 1116, 1117
    mapping = {20385: AMU, 20386: NECK, 20387: NECK, 20388: NECK}
    dialogs = [
        d(K, "talk", "30346-01.htm", when="absent", races=[0, 1, 3, 4]),
        d(K, "talk", "30346-02.htm", when="absent", races=[2], maxLevel=7),
        d(K, "talk", "30346-03.htm", when="absent", races=[2], minLevel=8),
        d(K, "event", "30346-04.htm", when="absent", event="30346-04", races=[2], minLevel=8, ops=START + [cond(1)]),
        d(K, "event", "30346-07.html", when="started", event="30346-07", ops=[{"op": "complete"}]),
        d(K, "event", "30346-08.html", when="started", event="30346-08"),
        d(K, "talk", "30346-05.html", cond=1, lacksItems=[AMU, NECK]),
        d(
            K,
            "talk",
            "30346-06.html",
            cond=1,
            hasAny=[AMU, NECK],
            ops=[
                {
                    "op": "sell_items",
                    "prices": [{"itemId": AMU, "adena": 20}, {"itemId": NECK, "adena": 30}],
                    "bonusMin": 10,
                    "bonusAdena": 1100,
                }
            ],
        ),
    ]
    for mob, item in mapping.items():
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(item, 0.5)]))
    return {
        "id": Q,
        "name": "Orc Subjugation",
        "repeatable": True,
        "abortable": True,
        "questItems": [AMU, NECK],
        "dialogs": dialogs,
    }


def q264():
    """Keen Claws — simplified wolf claw collection (Mobius tiered giveItemRandomly)."""
    Q, d = 264, html_loader("Q00264_KeenClaws", 264)
    PAINT = 30136
    CLAW = 1367
    dialogs = [
        d(PAINT, "talk", "30136-01.htm", when="absent", maxLevel=2),
        d(PAINT, "talk", "30136-02.htm", when="absent", minLevel=3),
        d(PAINT, "event", "30136-03.htm", when="absent", event="30136-03", minLevel=3, ops=START + [cond(1)]),
        d(PAINT, "talk", "30136-04.html", cond=1),
        d(
            PAINT,
            "talk",
            "30136-05.html",
            cond=2,
            atLeast={CLAW: 50},
            ops=[
                give(1336),
                give(1060),
                {"op": "complete"},
            ],
        ),
    ]
    # Approximate average drop rate from Mobius tier tables.
    dialogs.append(d(20003, "kill", None, cond=1, raw="", ops=[drop(CLAW, 0.45, limit=50, at=2)]))
    dialogs.append(d(20456, "kill", None, cond=1, raw="", ops=[drop(CLAW, 0.6, limit=50, at=2)]))
    return {
        "id": Q,
        "name": "Keen Claws",
        "repeatable": False,
        "abortable": True,
        "questItems": [CLAW],
        "dialogs": dialogs,
    }


def q265():
    """Bonds of Slavery — Dark Elf repeatable shackles."""
    Q, d = 265, html_loader("Q00265_BondsOfSlavery", 265)
    K = 30357
    SHACK = 1368
    dialogs = [
        d(K, "talk", "30357-01.html", when="absent", races=[0, 1, 3, 4]),
        d(K, "talk", "30357-02.html", when="absent", races=[2], maxLevel=5),
        d(K, "talk", "30357-03.htm", when="absent", races=[2], minLevel=6),
        d(K, "event", "30357-04.htm", when="absent", event="30357-04", races=[2], minLevel=6, ops=START + [cond(1)]),
        d(K, "event", "30357-07.html", when="started", event="30357-07", ops=[{"op": "complete"}]),
        d(K, "event", "30357-08.html", when="started", event="30357-08"),
        d(K, "talk", "30357-05.html", cond=1, lacksItems=[SHACK]),
        d(
            K,
            "talk",
            "30357-06.html",
            cond=1,
            hasItem=SHACK,
            ops=[{"op": "sell_items", "prices": [{"itemId": SHACK, "adena": 12}], "bonusMin": 10, "bonusAdena": 500}],
        ),
    ]
    dialogs.append(d(20004, "kill", None, cond=1, raw="", ops=[drop(SHACK, 0.5)]))
    dialogs.append(d(20005, "kill", None, cond=1, raw="", ops=[drop(SHACK, 0.6)]))
    return {
        "id": Q,
        "name": "Bonds of Slavery",
        "repeatable": True,
        "abortable": True,
        "questItems": [SHACK],
        "dialogs": dialogs,
    }


def q266():
    """Pleas of Pixies — Elf only; kill/collect simplified; turn-in reward fixed (tier 3 common)."""
    Q, d = 266, html_loader("Q00266_PleasOfPixies", 266)
    PIXY = 31852
    FANG = 1334
    dialogs = [
        d(PIXY, "talk", "31852-01.htm", when="absent", races=[0, 2, 3, 4]),
        d(PIXY, "talk", "31852-02.htm", when="absent", races=[1], maxLevel=2),
        d(PIXY, "talk", "31852-03.htm", when="absent", races=[1], minLevel=3),
        d(PIXY, "event", "31852-04.htm", when="absent", event="31852-04", races=[1], minLevel=3, ops=START + [cond(1)]),
        d(PIXY, "talk", "31852-05.html", cond=1),
        d(
            PIXY,
            "talk",
            "31852-06.html",
            cond=2,
            atLeast={FANG: 100},
            ops=[give(1336), give(1060), {"op": "complete"}],
        ),
    ]
    dialogs.append(d(20537, "kill", None, cond=1, raw="", ops=[drop(FANG, 0.2, limit=100, at=2)]))
    dialogs.append(
        d(
            20525,
            "kill",
            None,
            cond=1,
            raw="",
            ops=[drop(FANG, 0.2, limit=100, at=2), drop(FANG, 0.1, limit=100, at=2)],
        ),
    )
    dialogs.append(d(20534, "kill", None, cond=1, raw="", ops=[drop(FANG, 0.6, limit=100, at=2)]))
    dialogs.append(d(20530, "kill", None, cond=1, raw="", ops=[drop(FANG, 0.8, limit=100, at=2)]))
    return {
        "id": Q,
        "name": "Pleas of Pixies",
        "repeatable": False,
        "abortable": True,
        "questItems": [FANG],
        "dialogs": dialogs,
    }


def q271():
    """Proof of Valor — Orc; 1fang/kill simplification; necklace roll fixed to Necklace of Courage (1506)."""
    Q, d = 271, html_loader("Q00271_ProofOfValor", 271)
    RUKAIN = 30577
    FANG = 1473
    NECK_COURAGE, NECK_VALOR = 1506, 1507
    dialogs = [
        d(RUKAIN, "talk", "30577-01.htm", when="absent", races=[0, 1, 2, 4]),
        d(RUKAIN, "talk", "30577-02.htm", when="absent", races=[3], maxLevel=3),
        d(RUKAIN, "talk", "30577-07.htm", when="absent", races=[3], hasAny=[NECK_COURAGE, NECK_VALOR]),
        d(RUKAIN, "talk", "30577-03.htm", when="absent", races=[3], minLevel=4, lacksItems=[NECK_COURAGE, NECK_VALOR]),
        d(
            RUKAIN,
            "event",
            "30577-04.htm",
            when="absent",
            event="30577-04",
            races=[3],
            minLevel=4,
            hasAny=[NECK_COURAGE, NECK_VALOR],
        ),
        d(
            RUKAIN,
            "event",
            "30577-04.htm",
            when="absent",
            event="30577-04",
            races=[3],
            minLevel=4,
            lacksItems=[NECK_COURAGE, NECK_VALOR],
            ops=START + [cond(1)],
        ),
        d(RUKAIN, "talk", "30577-05.html", cond=1),
        d(
            RUKAIN,
            "talk",
            "30577-06.html",
            cond=2,
            atLeast={FANG: 50},
            ops=[
                give(NECK_COURAGE),
                {"op": "complete"},
            ],
        ),
    ]
    dialogs.append(d(20475, "kill", None, cond=1, raw="", ops=[drop(FANG, 1.0, limit=50, at=2)]))
    return {
        "id": Q,
        "name": "Proof of Valor",
        "repeatable": True,
        "abortable": True,
        "questItems": [FANG],
        "dialogs": dialogs,
    }


def q272():
    """Wrath of Ancestors — Orc repeatable."""
    Q, d = 272, html_loader("Q00272_WrathOfAncestors", 272)
    LIVINA = 30572
    HEAD = 1474
    dialogs = [
        d(LIVINA, "talk", "30572-01.htm", when="absent", races=[0, 1, 2, 4]),
        d(LIVINA, "talk", "30572-02.htm", when="absent", races=[3], maxLevel=4),
        d(LIVINA, "talk", "30572-03.htm", when="absent", races=[3], minLevel=5),
        d(LIVINA, "event", "30572-04.htm", when="absent", event="30572-04", races=[3], minLevel=5, ops=START + [cond(1)]),
        d(LIVINA, "talk", "30572-05.html", cond=1),
        d(
            LIVINA,
            "talk",
            "30572-06.html",
            cond=2,
            atLeast={HEAD: 50},
            ops=[{"op": "give_adena", "count": 1500}, {"op": "complete"}],
        ),
    ]
    for mob in (20319, 20320):
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(HEAD, 1.0, limit=50, at=2)]))
    return {
        "id": Q,
        "name": "Wrath of Ancestors",
        "repeatable": True,
        "abortable": True,
        "questItems": [HEAD],
        "dialogs": dialogs,
    }


def q273():
    """Invaders of the Holy Land — Orc repeatable dual stones + adena."""
    Q, d = 273, html_loader("Q00273_InvadersOfTheHolyLand", 273)
    V = 30566
    BLACK, RED = 1475, 1476
    rates = {20311: 0.9, 20312: 0.87, 20313: 0.77}
    dialogs = [
        d(V, "talk", "30566-01.htm", when="absent", races=[0, 1, 2, 4]),
        d(V, "talk", "30566-02.htm", when="absent", races=[3], maxLevel=5),
        d(V, "talk", "30566-03.htm", when="absent", races=[3], minLevel=6),
        d(V, "event", "30566-04.htm", when="absent", event="30566-04", races=[3], minLevel=6, ops=START + [cond(1)]),
        d(V, "event", "30566-08.html", when="started", event="30566-08", ops=[{"op": "complete"}]),
        d(V, "event", "30566-09.html", when="started", event="30566-09"),
        d(V, "talk", "30566-05.html", cond=1, lacksItems=[BLACK, RED]),
        d(
            V,
            "talk",
            "30566-06.html",
            cond=1,
            hasItems=[BLACK],
            lacksItems=[RED],
            ops=[
                {"op": "sell_items", "prices": [{"itemId": BLACK, "adena": 3}], "bonusMin": 10, "bonusAdena": 1500},
                {"op": "complete"},
            ],
        ),
        d(
            V,
            "talk",
            "30566-07.html",
            cond=1,
            hasItem=RED,
            ops=[
                {"op": "sell_items", "prices": [{"itemId": RED, "adena": 10}, {"itemId": BLACK, "adena": 3}], "bonusMin": 10, "bonusAdena": 1800},
                {"op": "complete"},
            ],
        ),
    ]
    for mob, p_black in rates.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                raw="",
                ops=[
                    drop(BLACK, p_black, limit=0),
                    drop(RED, 1.0 - p_black, limit=0),
                ],
            ),
        )
    return {
        "id": Q,
        "name": "Invaders of the Holy Land",
        "repeatable": True,
        "abortable": True,
        "questItems": [BLACK, RED],
        "dialogs": dialogs,
    }


def q274():
    """Skirmish with the Werewolves — Orc + necklace gate; heads + 5% totem."""
    Q, d = 274, html_loader("Q00274_SkirmishWithTheWerewolves", 274)
    B = 30569
    HEAD, TOTEM = 1477, 1501
    NECK_COURAGE, NECK_VALOR = 1506, 1507
    dialogs = [
        d(B, "talk", "30569-08.html", when="absent", lacksItems=[NECK_COURAGE, NECK_VALOR]),
        d(B, "talk", "30569-01.html", when="absent", races=[0, 1, 2, 4], hasAny=[NECK_COURAGE, NECK_VALOR]),
        d(B, "talk", "30569-02.html", when="absent", races=[3], maxLevel=8, hasAny=[NECK_COURAGE, NECK_VALOR]),
        d(B, "talk", "30569-03.htm", when="absent", races=[3], minLevel=9, hasAny=[NECK_COURAGE, NECK_VALOR]),
        d(B, "event", "30569-04.htm", when="absent", event="30569-04", races=[3], minLevel=9, hasAny=[NECK_COURAGE, NECK_VALOR], ops=START + [cond(1)]),
        d(B, "talk", "30569-05.html", cond=1),
        d(
            B,
            "talk",
            "30569-06.html",
            cond=2,
            atLeast={HEAD: 40},
            lacksItems=[TOTEM],
            ops=[
                {
                    "op": "sell_items",
                    "prices": [{"itemId": HEAD, "adena": 30}],
                },
                {"op": "give_adena", "count": 2300},
                {"op": "complete"},
            ],
        ),
        d(
            B,
            "talk",
            "30569-07.html",
            cond=2,
            atLeast={HEAD: 40},
            hasItem=TOTEM,
            ops=[
                {
                    "op": "sell_items",
                    "prices": [{"itemId": HEAD, "adena": 30}, {"itemId": TOTEM, "adena": 600}],
                },
                {"op": "give_adena", "count": 2300},
                {"op": "complete"},
            ],
        ),
    ]
    for mob in (20363, 20364):
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                raw="",
                ops=[
                    drop(HEAD, 1.0, limit=40, at=2),
                    drop(TOTEM, 0.05, limit=0),
                ],
            ),
        )
    return {
        "id": Q,
        "name": "Skirmish with the Werewolves",
        "repeatable": True,
        "abortable": True,
        "questItems": [HEAD, TOTEM],
        "dialogs": dialogs,
    }


def main():
    new = [
        q151(),
        q152(),
        q157(),
        q164(),
        q165(),
        q169(),
        q170(),
        q257(),
        q258(),
        q260(),
        q263(),
        q264(),
        q265(),
        q266(),
        q271(),
        q272(),
        q273(),
        q274(),
    ]
    questio.upsert_quests(new)
    for q in new:
        print(f"quest {q['id']}: {len(q['dialogs'])} dialogs")


if __name__ == "__main__":
    main()
