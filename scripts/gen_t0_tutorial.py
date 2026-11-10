#!/usr/bin/env python3
"""Mobius Q00999_T0Tutorial → data/quests/00999_* (replaces Fineage Roxxy smoke on id 999).

Linearized without Q255 timers/Ex: start at race Newbie Helper → kill for Blue Gem →
recommendation + novice shots → Grand Master turn-in. HTML from Mobius Q00999_T0Tutorial.
Roxxy smoke moved to id 998 (≤1023 for completedMask). Run gen_drop_items + gen_quest_item_flags after.
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

SRC = ROOT / "ref/mobius-ct0-interlude/dist/game/data/scripts/quests/Q00999_T0Tutorial"
if not SRC.is_dir():
    SRC = ROOT / "ref/mobius-ct0-interlude/data/scripts/quests/Q00999_T0Tutorial"

Q = 999
BLUE_GEM = 6353
SS, SPS = 5789, 5790
REC_H_FIGHT, REC_H_MAGE = 1067, 1068
LEAF, BLOOD, LICENSE, VOUCHER = 1069, 1070, 1498, 1496

give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
START = [{"op": "create"}, {"op": "start"}, cond(1)]
drop = lambda chance=1.0: {
    "op": "drop_item", "itemId": BLUE_GEM, "count": 1, "chance": chance, "limit": 1, "condAtLimit": 1
}


def html(name: str) -> str:
    for ext in ("", ".htm", ".html"):
        p = SRC / (name + ext)
        if p.exists():
            text = p.read_text(encoding="utf-8").strip()
            return re.sub(
                r"bypass -h Quest Q00999_T0Tutorial ([A-Za-z0-9_\-]+)(?:\.html?)?",
                lambda m: f"quest_event:{Q}:{m.group(1)}",
                text,
            )
    raise SystemExit(f"missing html {name}")


def d(npc, hook, page, when="started", ops=None, event=None, raw=None, **gates):
    row = {"npcId": npc, "hook": hook, "when": when}
    if event:
        row["event"] = event
    row.update(gates)
    row["html"] = raw if raw is not None else html(page)
    if ops:
        row["ops"] = ops
    return row


def path(helper, master, rec, race, mage: bool, start_page: str, mid_page: str, done_page: str, shots, shot_count):
    """One race/class track: helper starts hunt, gem→rec, master finishes."""
    gates_h = {"races": [race], "isMage": mage, "maxLevel": 9}
    gates_m = {"races": [race], "isMage": mage, "maxLevel": 9}
    rows = [
        # Quest list / accept: start hunting
        d(helper, "talk", start_page, when="absent", ops=START, **gates_h),
        # Waiting for gem
        d(helper, "talk", "30530-02.htm" if not mage else "30131-02.htm", cond=1, lacksItems=[BLUE_GEM, rec], **gates_h),
        # Turn in gem
        d(helper, "talk", mid_page, cond=1, hasItem=BLUE_GEM, ops=[
            take(BLUE_GEM), give(rec), give(shots, shot_count), cond(2)
        ], **gates_h),
        # After recommendation
        d(helper, "talk", done_page, cond=2, hasItem=rec, **gates_h),
        # Master before letter
        d(master, "talk", f"{master}-01.htm", when="absent", **gates_m),
        d(master, "talk", f"{master}-01.htm", cond=1, **gates_m),
        # Master with letter
        d(master, "talk", f"{master}-02.htm", cond=2, hasItem=rec, **gates_m),
        d(master, "event", f"{master}-03.htm", event=f"{master}_02", cond=2, hasItem=rec, ops=[
            take(rec),
            {"op": "give_exp_sp", "exp": 0, "sp": 50},
            {"op": "complete"},
        ], **gates_m),
        d(master, "talk", f"{master}-04.htm", when="completed", **gates_m),
    ]
    return rows


dialogs: list[dict] = []
# Human fighter / mystic
dialogs += path(30009, 30008, REC_H_FIGHT, 0, False, "30530-01.htm", "30009-03.htm", "30009-04.htm", SS, 200)
dialogs += path(30019, 30017, REC_H_MAGE, 0, True, "30530-01.htm", "30019-03a.htm", "30019-04.htm", SPS, 100)
# Elf
dialogs += path(30400, 30370, LEAF, 1, False, "30530-01.htm", "30400-03.htm", "30400-04.htm", SS, 200)
dialogs += path(30400, 30370, LEAF, 1, True, "30530-01.htm", "30400-03a.htm", "30400-04.htm", SPS, 100)
# Dark Elf
dialogs += path(30131, 30129, BLOOD, 2, False, "30530-01.htm", "30131-03.htm", "30131-04.htm", SS, 200)
dialogs += path(30131, 30129, BLOOD, 2, True, "30530-01.htm", "30131-03a.htm", "30131-04.htm", SPS, 100)
# Orc
dialogs += path(30575, 30573, VOUCHER, 3, False, "30530-01.htm", "30575-03.htm", "30575-04.htm", SS, 200)
dialogs += path(30575, 30573, VOUCHER, 3, True, "30530-01.htm", "30575-03a.htm", "30575-04.htm", SPS, 100)
# Dwarf fighter only
dialogs += path(30530, 30528, LICENSE, 4, False, "30530-01.htm", "30530-03.htm", "30530-04.htm", SS, 200)

# Blue gem from training-hall gremlins / goblins while hunting
for mob in (18342, 20001):
    dialogs.append(d(mob, "kill", None, cond=1, lacksItems=[BLUE_GEM], raw="", ops=[drop(1.0)]))

quest = {
    "id": Q,
    "name": "Newbie Tutorial",
    "repeatable": False,
    "abortable": True,
    "questItems": [BLUE_GEM, REC_H_FIGHT, REC_H_MAGE, LEAF, BLOOD, LICENSE, VOUCHER],
    "dialogs": dialogs,
}

questio.upsert_quest(quest)
print(f"quest {Q}: {len(dialogs)} dialogs")
