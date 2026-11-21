#!/usr/bin/env python3
"""Stub batch 2: Q109, Q376, Q504, Q508, Q509, Q510, Q628 + Q634/Q635 start fix.

After: python3 scripts/gen_quests_stubs_batch2.py
       python3 scripts/gen_quests_dimensional_rift.py
       python3 scripts/gen_drop_items.py .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/items
       python3 tools/quest_status.py
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = questio.mobius_quest_root()

START = [{"op": "create"}, {"op": "start"}]
cond = lambda c: {"op": "set_cond", "cond": c}
DONE = {"op": "complete"}


def drop(i, chance, limit=0, at=None, count=1):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


def give(i, count=1):
    return {"op": "give_item", "itemId": i, "count": count}


def take(i, count=1):
    return {"op": "take_item", "itemId": i, "count": count}


def take_all(*ids: int):
    return {"op": "take_all", "items": list(ids)}


def adena(c):
    return {"op": "give_adena", "count": c}


def html_loader(script: str, qid: int):
    def html(name: str) -> str:
        return questio.mobius_quest_html(QROOT, script, qid, name)

    def d(npc, hook, page, when="started", ops=None, event=None, raw=None, **gates):
        row = {"npcId": npc, "hook": hook, "when": when}
        if event is not None:
            row["event"] = event
        row.update(gates)
        row["html"] = raw if raw is not None else (html(page) if page else "")
        if ops:
            row["ops"] = ops
        return row

    return html, d


def clan_raid_bounty(qid: int, script: str, name: str, npc: int, rows: list[tuple[int, int, int, int]]):
    """Mobius clan RB bounty: pick raid index, kill once for quest item, turn in (rep → adena placeholder)."""
    html, d = html_loader(script, qid)
    items = [r[1] for r in rows]
    quit_ev = f"{npc}-7" if qid == 508 else f"{npc}-6"
    dialogs = [
        d(npc, "talk", f"{npc}-0c.htm" if qid == 508 else f"{npc}-1.htm", when="absent"),
        d(npc, "event", f"{npc}-7.htm" if qid == 508 else f"{npc}-6.htm",
          event=quit_ev, when="started", ops=[DONE]),
    ]
    for idx, (boss, item, rep_lo, _rep_hi) in enumerate(rows, start=1):
        ev = str(idx)
        dialogs.append(
            d(
                npc,
                "event",
                f"{npc}-{idx}.htm",
                when="absent",
                event=ev,
                ops=START + [cond(1), {"op": "set", "key": "raid", "value": ev}],
            )
        )
        dialogs.append(
            d(
                npc,
                "talk",
                f"{npc}-{idx}b.htm",
                cond=1,
                whenVar="raid",
                whenValue=ev,
                hasItem=item,
                ops=[take(item), adena(rep_lo * 100), DONE],
            )
        )
        dialogs.append(
            d(
                boss,
                "kill",
                None,
                cond=1,
                whenVar="raid",
                whenValue=ev,
                lacksItems=[item],
                raw="",
                ops=[drop(item, 1.0, limit=1)],
            )
        )
    return {
        "id": qid,
        "name": name,
        "repeatable": True,
        "abortable": True,
        "questItems": items,
        "dialogs": dialogs,
    }


def q109():
    Q = 109
    html, d = html_loader("Q00109_InSearchOfTheNest", Q)
    PIERCE, CORPSE, KAHMAN = 31553, 32015, 31554
    MEMO = 8083
    BADGES = [7246, 7247]
    dialogs = [
        d(PIERCE, "talk", "31553-00a.htm", when="absent", maxLevel=65),
        d(PIERCE, "talk", "31553-00a.htm", when="absent", minLevel=66, lacksItems=BADGES),
        d(PIERCE, "talk", "31553-00.htm", when="absent", minLevel=66, hasAny=BADGES),
        d(PIERCE, "event", "31553-01.htm", when="absent", minLevel=66, hasAny=BADGES,
          event="31553-01", ops=START + [cond(1)]),
        d(PIERCE, "talk", "31553-01a.htm", cond=1),
        d(PIERCE, "event", "31553-03.htm", cond=1, event="31553-03", ops=[cond(3), take(MEMO)]),
        d(PIERCE, "talk", "31553-02.htm", cond=2, hasItem=MEMO),
        d(CORPSE, "talk", "32015-01.htm", cond=1),
        d(CORPSE, "event", "32015-02.htm", cond=1, event="32015-02", ops=[cond(2), give(MEMO)]),
        d(CORPSE, "talk", "32015-02.htm", cond=2),
        d(KAHMAN, "talk", "31554-01.htm", cond=3),
        d(KAHMAN, "event", "31554-02.htm", cond=3, event="31554-02", ops=[adena(5168), DONE]),
    ]
    return {
        "id": Q,
        "name": "In Search of the Nest",
        "repeatable": False,
        "abortable": True,
        "questItems": [MEMO],
        "dialogs": dialogs,
    }


def q376():
    Q = 376
    html, d = html_loader("Q00376_ExplorationOfTheGiantsCavePart1", Q)
    SOBLING, CLIFF = 31147, 30182
    PARCHMENT, DICT_B, BOOK, DICT_I = 5944, 5891, 5890, 5892
    book_sets = [
        [5937, 5938, 5939, 5940, 5941],
        [5932, 5933, 5934, 5935, 5936],
        [5922, 5923, 5924, 5925, 5926],
        [5927, 5928, 5929, 5930, 5931],
    ]
    recipes = [5346, 5332, 5416, 5424]
    dialogs = [
        d(SOBLING, "talk", "31147-01.htm", when="absent", maxLevel=50),
        d(SOBLING, "talk", "31147-02.htm", when="absent", minLevel=51),
        d(SOBLING, "event", "31147-03.htm", when="absent", minLevel=51, event="31147-03",
          ops=START + [cond(1), {"op": "set", "key": "condBook", "value": "1"}, give(DICT_B)]),
        d(SOBLING, "talk", "31147-05.htm", cond=1, lacksItems=[BOOK]),
        d(SOBLING, "event", "31147-09.htm", event="31147-09", when="started", ops=[DONE]),
        d(CLIFF, "talk", "30182-01.htm", cond=2, hasItem=BOOK),
        d(CLIFF, "event", "30182-02.htm", event="30182-02", cond=2, hasItem=BOOK,
          ops=[cond(3), take_all(BOOK), give(DICT_I)]),
        d(CLIFF, "talk", "30182-03.htm", cond=3),
    ]
    for books, recipe in zip(book_sets, recipes):
        dialogs.append(
            d(SOBLING, "talk", "31147-04.htm", cond=1, hasItems=books,
              ops=[*(take(i) for i in books), give(recipe)])
        )
    dialogs.append(
        d(SOBLING, "talk", "31147-07.htm", cond=1, hasItem=BOOK, ops=[cond(2)]),
    )
    for mob in (20647, 20648, 20649, 20650):
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(PARCHMENT, 0.2)]))
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                varAtLeast={"condBook": 1},
                lacksItems=[BOOK],
                raw="",
                ops=[drop(BOOK, 0.001, limit=1), {"op": "unset", "key": "condBook"}],
            )
        )
    quest_items = [PARCHMENT, DICT_B, BOOK, DICT_I] + [b for s in book_sets for b in s]
    return {
        "id": Q,
        "name": "Exploration of the Giants Cave, Part 1",
        "repeatable": True,
        "abortable": True,
        "questItems": quest_items,
        "dialogs": dialogs,
    }


def q504():
    Q = 504
    html, d = html_loader("Q00504_CompetitionForTheBanditStronghold", Q)
    MESSENGER = 35437
    AMULET, TROPHY = 4332, 5009
    dialogs = [
        d(MESSENGER, "talk", "35437-01.htm", when="absent", ops=START + [cond(1)]),
        d(MESSENGER, "talk", "35437-02.htm", cond=1, below={str(AMULET): 30}),
        d(MESSENGER, "talk", "35437-03.htm", cond=1, atLeast={str(AMULET): 30},
          ops=[take_all(AMULET), give(TROPHY), DONE]),
    ]
    for mob in (20570, 20571, 20572, 20573, 20574):
        dialogs.append(
            d(mob, "kill", None, cond=1, raw="", ops=[drop(AMULET, 1.0, limit=30, at=2)])
        )
    return {
        "id": Q,
        "name": "Competition for the Bandit Stronghold",
        "repeatable": True,
        "abortable": True,
        "questItems": [AMULET],
        "dialogs": dialogs,
    }


def q508():
    rows = [
        (25252, 8277, 65, 100),
        (25140, 8279, 40, 75),
        (25255, 8280, 30, 65),
        (25245, 8281, 105, 140),
        (25051, 8282, 40, 75),
        (25524, 8494, 60, 95),
    ]
    return clan_raid_bounty(508, "Q00508_AClansReputation", "A Clan's Reputation", 30868, rows)


def q509():
    rows = [
        (25290, 8489, 180, 215),
        (25293, 8490, 430, 465),
        (25523, 8491, 380, 415),
        (25322, 8492, 220, 255),
        (25514, 8493, 130, 165),
    ]
    return clan_raid_bounty(509, "Q00509_AClansFame", "The Clan's Prestige", 31331, rows)


def q510():
    Q = 510
    html, d = html_loader("Q00510_AClansPrestige", Q)
    VALDIS = 31331
    CLAW = 8767
    dialogs = [
        d(VALDIS, "talk", "31331-1.htm", when="absent"),
        d(VALDIS, "event", "31331-3.htm", when="absent", event="31331-3", ops=START + [cond(1)]),
        d(VALDIS, "talk", "31331-4.htm", cond=1, below={str(CLAW): 10}),
        d(VALDIS, "talk", "31331-5.htm", cond=1, atLeast={str(CLAW): 10},
          ops=[take_all(CLAW), adena(5000), DONE]),
        d(VALDIS, "event", "31331-6.htm", event="31331-6", when="started", ops=[DONE]),
    ]
    for mob in (22215, 22216, 22217):
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(CLAW, 0.5)]))
    return {
        "id": Q,
        "name": "A Clan's Reputation",
        "repeatable": True,
        "abortable": True,
        "questItems": [CLAW],
        "dialogs": dialogs,
    }


def q628():
    Q = 628
    html, d = html_loader("Q00628_HuntGoldenRam", Q)
    KAHMAN = 31554
    SPLINTER, NEEDLE = 7248, 7249
    RECRUIT, SOLDIER = 7246, 7247
    chances_s = {
        21508: 0.5, 21509: 0.43, 21510: 0.521, 21511: 0.575, 21512: 0.746,
        21513: 0.5, 21514: 0.43, 21515: 0.52, 21516: 0.531, 21517: 0.744,
    }
    dialogs = [
        d(KAHMAN, "talk", "31554-01a.htm", when="absent", maxLevel=65),
        d(KAHMAN, "talk", "31554-01.htm", when="absent", minLevel=66),
        d(KAHMAN, "event", "31554-02.htm", when="absent", minLevel=66, event="31554-02", ops=START + [cond(1)]),
        d(KAHMAN, "event", "31554-03a.htm", event="31554-03a", cond=1, atLeast={str(SPLINTER): 100},
          ops=[cond(2), take_all(SPLINTER), give(RECRUIT)]),
        d(KAHMAN, "talk", "31554-05.htm", cond=2, atLeast={str(SPLINTER): 100, str(NEEDLE): 100},
          hasItem=RECRUIT,
          ops=[cond(3), take_all(SPLINTER, NEEDLE), take(RECRUIT), give(SOLDIER), DONE]),
        d(KAHMAN, "event", "31554-07.htm", event="31554-07", when="started", ops=[DONE]),
    ]
    for mob, ch in chances_s.items():
        item = SPLINTER if mob <= 21512 else NEEDLE
        c_gate = 1 if mob <= 21512 else 2
        dialogs.append(
            d(mob, "kill", None, cond=c_gate, party=True, below={str(item): 100}, raw="",
              ops=[drop(item, ch)])
        )
    return {
        "id": Q,
        "name": "Hunt of the Golden Ram Mercenary Force",
        "repeatable": True,
        "abortable": True,
        "questItems": [SPLINTER, NEEDLE, RECRUIT, SOLDIER],
        "dialogs": dialogs,
    }


def refresh_dimensional_rift() -> None:
    import subprocess

    subprocess.run([sys.executable, str(ROOT / "scripts" / "gen_quests_dimensional_rift.py")], check=True)


def main() -> None:
    questio.ensure_meta(reference="Mobius CT_0 batch2 via scripts/gen_quests_stubs_batch2.py")
    for q in (q109(), q376(), q504(), q508(), q509(), q510(), q628()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")
    refresh_dimensional_rift()
    print("refreshed Q634/Q635 dimensional rift packs")
    questio.ensure_meta(reference="Mobius CT_0 batch2 via scripts/gen_quests_stubs_batch2.py")


if __name__ == "__main__":
    main()