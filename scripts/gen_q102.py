#!/usr/bin/env python3
"""Q102 Sea of Spores Fever -> data/quests/ pack (questio.upsert) (insert/replace id 102).

Transcribes Mobius CT0 Interlude Q00102_SeaOfSporesFever.java. HTML links rewritten to
quest_event:102:<event>. Run gen_drop_items.py + gen_quest_item_flags.py afterwards.
"""
from __future__ import annotations

import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import questio
SRC = ROOT / "ref/mobius-ct0-interlude/dist/game/data/scripts/quests/Q00102_SeaOfSporesFever"
if not SRC.is_dir():
    SRC = ROOT / "ref/mobius-ct0-interlude/data/scripts/quests/Q00102_SeaOfSporesFever"
Q = 102

COBENDELL, BERROS, VELTRESS, RAYEN, ALBERIUS, GARTRANDELL = 30156, 30217, 30219, 30221, 30284, 30285
DRYAD, DRYAD_ELDER = 20013, 20019
SWORD, STAFF = 743, 744
LIST, LETTER, AMULET, TEAR = 746, 964, 965, 966
POTION = 1060
MED1, MED2, MED3, MED4, MED5 = 1130, 1131, 1132, 1133, 1134
MEDS = [MED1, MED2, MED3, MED4, MED5]
SS, SPS = 1835, 2509
ECHO = [4412, 4413, 4414, 4415, 4416]
SENTINELS = {GARTRANDELL: MED5, RAYEN: MED4, VELTRESS: MED3, BERROS: MED2}


def html(name: str) -> str:
    for ext in ("", ".htm", ".html"):
        p = SRC / (name + ext)
        if p.exists():
            text = p.read_text(encoding="utf-8").strip()
            return re.sub(
                r"bypass -h Quest Q00102_SeaOfSporesFever ([A-Za-z0-9_\-]+)(?:\.html?)?",
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


give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
cond = lambda c: {"op": "set_cond", "cond": c}
START = [{"op": "create"}, {"op": "start"}]


def reward(weapon: int, shot: int, shot_count: int) -> list:
    return [
        give(POTION, 100),
        *[give(e, 10) for e in ECHO],
        give(weapon),
        give(shot, shot_count),
        {"op": "give_exp_sp", "exp": 30202, "sp": 1339},
        {"op": "give_adena", "count": 6331},
        {"op": "complete"},
    ]


dialogs: list[dict] = [
    d(ALBERIUS, "talk", "30284-07.htm", when="absent", races=[1], minLevel=12),
    d(ALBERIUS, "talk", "30284-08.htm", when="absent", races=[1]),
    d(ALBERIUS, "talk", "30284-00.htm", when="absent"),
    d(ALBERIUS, "event", "30284-02.htm", when="absent", event="30284-02", races=[1], minLevel=12,
      ops=START + [give(LETTER), cond(1)]),
    d(ALBERIUS, "talk", "30284-03.html", cond=1, hasItem=LETTER),
    d(ALBERIUS, "talk", "30284-09.html", cond=2, hasItem=AMULET),
    d(ALBERIUS, "talk", "30284-04.html", cond=4, hasItem=MED1,
      ops=[take(MED1), give(LIST), cond(5)]),
    d(ALBERIUS, "talk", "30284-05.html", cond=5, hasAny=MEDS),
    d(ALBERIUS, "talk", "30284-06.html", cond=6, isMage=True, lacksItems=MEDS,
      ops=reward(STAFF, SPS, 500)),
    d(ALBERIUS, "talk", "30284-06.html", cond=6, isMage=False, lacksItems=MEDS,
      ops=reward(SWORD, SS, 500)),
    d(COBENDELL, "talk", "30156-03.html", cond=1, hasItem=LETTER,
      ops=[take(LETTER), give(AMULET), cond(2)]),
    d(COBENDELL, "talk", "30156-05.html", cond=3, atLeast={TEAR: 10},
      ops=[take(AMULET), take(TEAR, 10),
           give(MED1), give(MED2), give(MED3), give(MED4), give(MED5), cond(4)]),
    d(COBENDELL, "talk", "30156-04.html", cond=2, hasItem=AMULET),
    d(COBENDELL, "talk", "30156-06.html", cond=4, hasAny=MEDS),
    d(COBENDELL, "talk", "30156-07.html", cond=5, hasAny=MEDS),
]

for npc, med in SENTINELS.items():
    others = [m for m in MEDS if m != med]
    dialogs.append(d(npc, "talk", f"{npc}-01.html", cond=5, hasItems=[LIST, med], lacksItems=others,
                     ops=[take(med), cond(6)]))
    dialogs.append(d(npc, "talk", f"{npc}-01.html", cond=5, hasItems=[LIST, med], ops=[take(med)]))

for mob in (DRYAD, DRYAD_ELDER):
    dialogs.append(d(mob, "kill", None, cond=2, raw="",
                     ops=[{"op": "drop_item", "itemId": TEAR, "count": 1, "chance": 0.3,
                           "limit": 10, "condAtLimit": 3}]))

quest = {
    "id": Q,
    "name": "Sea of Spores Fever",
    "repeatable": False,
    "abortable": True,
    "questItems": [LIST, LETTER, AMULET, TEAR, MED1, MED2, MED3, MED4, MED5],
    "dialogs": dialogs,
}

questio.upsert_quest(quest)
print(f"quest {Q}: {len(dialogs)} dialogs")
