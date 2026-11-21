#!/usr/bin/env python3
"""Hand-parity Mobius Q11–Q18 (Goddard/Rune delivery + altar chain), issues #808/#810.

  Q11 Secret Meeting With Ketra Orcs   — Cadmon → Leon → Wahkan, munitions box
  Q12 Secret Meeting With Varka Silenos — Cadmon → Helmut → Naran Ashanuk
  Q13 Parcel Delivery                  — Fundin → Vulcan package (adena via ledger op)
  Q14 Whereabouts of the Archaeologist — Liesel → Ghost of Adventurer letter
  Q15 Sweet Whispers                   — Vladimir → Mysterious Necro → Hierarch
  Q16 The Coming Darkness              — Hierarch + 5 evil altars, seal crystals
  Q17 Light and Darkness               — Hierarch + 4 saint altars, blood of saint
  Q18 Meeting with the Golden Ram      — Donal → Daisy → Abercrombie supply box

Auto-port guesses replaced by Mobius values (Q11/Q12 exp 79761, Q13 adena 82656, …);
events bound to the NPC that actually fires them. Q16/Q17 no-item quit uses the
shared `exit` op (Mobius st.exitQuest(true)).

After: python3 scripts/gen_quests_11_18_delivery.py
       python3 scripts/gen_drop_items.py .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/items
       python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude
"""
from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = questio.mobius_quest_root()

give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
take_all = lambda *ids: {"op": "take_all", "items": list(ids)}
cond = lambda c: {"op": "set_cond", "cond": c}
adena = lambda n: {"op": "give_adena", "count": n}
exp_sp = lambda e, s: {"op": "give_exp_sp", "exp": e, "sp": s}
DONE = {"op": "complete"}
EXIT = {"op": "exit"}
START = [{"op": "create"}, {"op": "start"}]


def html_loader(script: str, qid: int):
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


def secret_meeting(qid: int, script: str, name: str, mid_npc: int, end_npc: int, box: int) -> dict:
    """Q11/Q12 shared flow: Cadmon 31296 → courier → faction contact, exp 79761."""
    d = html_loader(script, qid)
    CADMON = 31296
    dialogs = [
        d(CADMON, "talk", "31296-02.htm", when="absent", maxLevel=73),
        d(CADMON, "talk", "31296-01.htm", when="absent", minLevel=74),
        d(CADMON, "event", "31296-03.htm", when="absent", event="31296-03", minLevel=74, ops=START + [cond(1)]),
        d(CADMON, "talk", "31296-04.htm", cond=1),
        d(mid_npc, "talk", f"{mid_npc}-01.htm", cond=1),
        d(mid_npc, "event", f"{mid_npc}-02.htm", event=f"{mid_npc}-02", cond=1, ops=[give(box), cond(2)]),
        d(mid_npc, "talk", f"{mid_npc}-03.htm", cond=2),
        d(end_npc, "talk", f"{end_npc}-01.htm", cond=2, hasItem=box),
        d(
            end_npc,
            "event",
            f"{end_npc}-02.htm",
            event=f"{end_npc}-02",
            cond=2,
            hasItem=box,
            ops=[take(box), exp_sp(79761, 0), DONE],
        ),
    ]
    return {"id": qid, "name": name, "repeatable": False, "abortable": True,
            "questItems": [box], "dialogs": dialogs}


def q13() -> dict:
    Q, d = 13, html_loader("Q00013_ParcelDelivery", 13)
    FUNDIN, VULCAN, PACKAGE = 31274, 31539, 7263
    dialogs = [
        d(FUNDIN, "talk", "31274-1.htm", when="absent", maxLevel=73),
        d(FUNDIN, "talk", "31274-0.htm", when="absent", minLevel=74),
        d(FUNDIN, "event", "31274-2.htm", when="absent", event="31274-2", minLevel=74,
          ops=START + [give(PACKAGE), cond(1)]),
        d(FUNDIN, "talk", "31274-2.htm"),
        d(VULCAN, "talk", "31539-0.htm", hasItem=PACKAGE),
        d(VULCAN, "event", "31539-1.htm", event="31539-1", hasItem=PACKAGE,
          ops=[take(PACKAGE), adena(82656), DONE]),
    ]
    return {"id": Q, "name": "Parcel Delivery", "repeatable": False, "abortable": True,
            "questItems": [PACKAGE], "dialogs": dialogs}


def q14() -> dict:
    Q, d = 14, html_loader("Q00014_WhereaboutsOfTheArchaeologist", 14)
    LIESEL, GHOST, LETTER = 31263, 31538, 7253
    dialogs = [
        d(LIESEL, "talk", "31263-1.htm", when="absent", maxLevel=73),
        d(LIESEL, "talk", "31263-0.htm", when="absent", minLevel=74),
        d(LIESEL, "event", "31263-2.htm", when="absent", event="31263-2", minLevel=74,
          ops=START + [give(LETTER), cond(1)]),
        d(LIESEL, "talk", "31263-2.htm"),
        d(GHOST, "talk", "31538-0.htm", hasItem=LETTER),
        d(GHOST, "event", "31538-1.htm", event="31538-1", hasItem=LETTER,
          ops=[take(LETTER), adena(113228), DONE]),
    ]
    return {"id": Q, "name": "Whereabouts of the Archaeologist", "repeatable": False, "abortable": True,
            "questItems": [LETTER], "dialogs": dialogs}


def q15() -> dict:
    Q, d = 15, html_loader("Q00015_SweetWhispers", 15)
    VLADIMIR, HIERARCH, NECRO = 31302, 31517, 31518
    dialogs = [
        d(VLADIMIR, "talk", "31302-00a.htm", when="absent", maxLevel=59),
        d(VLADIMIR, "talk", "31302-00.htm", when="absent", minLevel=60),
        d(VLADIMIR, "event", "31302-01.htm", when="absent", event="31302-01", minLevel=60, ops=START + [cond(1)]),
        d(VLADIMIR, "talk", "31302-01a.htm"),
        d(NECRO, "talk", "31518-00.htm", cond=1),
        d(NECRO, "event", "31518-01.htm", event="31518-01", cond=1, ops=[cond(2)]),
        d(NECRO, "talk", "31518-01a.htm", cond=2),
        d(HIERARCH, "talk", "31517-00.htm", cond=2),
        d(HIERARCH, "event", "31517-01.htm", event="31517-01", cond=2, ops=[exp_sp(60217, 0), DONE]),
    ]
    return {"id": Q, "name": "Sweet Whispers", "repeatable": False, "abortable": True,
            "questItems": [], "dialogs": dialogs}


def q16() -> dict:
    Q, d = 16, html_loader("Q00016_TheComingDarkness", 16)
    HIERARCH, CRYSTAL = 31517, 7167
    dialogs = [
        d(HIERARCH, "talk", "31517-0a.htm", when="absent", maxLevel=61),
        d(HIERARCH, "talk", "31517-0.htm", when="absent", minLevel=62),
        d(HIERARCH, "event", "31517-2.htm", when="absent", event="31517-2", minLevel=62,
          ops=START + [give(CRYSTAL, 5), cond(1)]),
        d(HIERARCH, "talk", "31517-4.htm", cond=6, ops=[exp_sp(221958, 0), DONE]),
        d(HIERARCH, "talk", "31517-3.htm", hasItem=CRYSTAL),
        d(HIERARCH, "talk", "31517-3a.htm", lacksItems=[CRYSTAL], ops=[EXIT]),
    ]
    for k in range(1, 6):
        npc = 31511 + k
        dialogs += [
            d(npc, "talk", f"{npc}-0.htm", cond=k, hasItem=CRYSTAL),
            d(npc, "talk", "altar_nocrystal.htm", cond=k, lacksItems=[CRYSTAL]),
            d(npc, "talk", f"{npc}-2.htm", minCond=k + 1),
            d(npc, "event", f"{npc}-1.htm", event=f"{npc}-1", cond=k, hasItem=CRYSTAL,
              ops=[take(CRYSTAL), cond(k + 1)]),
        ]
    return {"id": Q, "name": "The Coming Darkness", "repeatable": False, "abortable": True,
            "questItems": [CRYSTAL], "dialogs": dialogs}


def q17() -> dict:
    Q, d = 17, html_loader("Q00017_LightAndDarkness", 17)
    HIERARCH, BLOOD = 31517, 7168
    dialogs = [
        d(HIERARCH, "talk", "31517-03.htm", when="absent", maxLevel=60),
        d(HIERARCH, "talk", "31517-01.htm", when="absent", minLevel=61),
        d(HIERARCH, "event", "31517-04.htm", when="absent", event="31517-04", minLevel=61,
          ops=START + [give(BLOOD, 4), cond(1)]),
        d(HIERARCH, "talk", "31517-07.htm", cond=5, ops=[exp_sp(105527, 0), DONE]),
        d(HIERARCH, "talk", "31517-05.htm", hasItem=BLOOD),
        d(HIERARCH, "talk", "31517-06.htm", lacksItems=[BLOOD], ops=[EXIT]),
    ]
    for k in range(1, 5):
        npc = 31507 + k
        dialogs += [
            d(npc, "talk", f"{npc}-01.htm", cond=k),
            d(npc, "talk", f"{npc}-04.htm", minCond=k + 1),
            d(npc, "event", f"{npc}-02.htm", event=f"{npc}-02", cond=k, hasItem=BLOOD,
              ops=[take(BLOOD), cond(k + 1)]),
            d(npc, "event", f"{npc}-03.htm", event=f"{npc}-02", cond=k, lacksItems=[BLOOD]),
        ]
    return {"id": Q, "name": "Light and Darkness", "repeatable": False, "abortable": True,
            "questItems": [BLOOD], "dialogs": dialogs}


def q18() -> dict:
    Q, d = 18, html_loader("Q00018_MeetingWithTheGoldenRam", 18)
    DONAL, DAISY, ABERCROMBIE, BOX = 31314, 31315, 31555, 7245
    dialogs = [
        d(DONAL, "talk", "31314-02.htm", when="absent", maxLevel=65),
        d(DONAL, "talk", "31314-01.htm", when="absent", minLevel=66),
        d(DONAL, "event", "31314-03.htm", when="absent", event="31314-03", minLevel=66, ops=START + [cond(1)]),
        d(DONAL, "talk", "31314-04.htm"),
        d(DAISY, "talk", "31315-01.htm", cond=1),
        d(DAISY, "event", "31315-02.htm", event="31315-02", cond=1, ops=[cond(2), give(BOX)]),
        d(DAISY, "talk", "31315-03.htm", cond=2),
        d(ABERCROMBIE, "talk", "31555-01.htm", cond=2, hasItem=BOX),
        d(ABERCROMBIE, "event", "31555-02.htm", event="31555-02", cond=2, hasItem=BOX,
          ops=[take(BOX), adena(15000), exp_sp(50000, 0), DONE]),
    ]
    return {"id": Q, "name": "Meeting with the Golden Ram", "repeatable": False, "abortable": True,
            "questItems": [BOX], "dialogs": dialogs}


def main() -> None:
    questio.ensure_meta()
    quests = [
        secret_meeting(11, "Q00011_SecretMeetingWithKetraOrcs", "Secret Meeting With Ketra Orcs", 31256, 31371, 7231),
        secret_meeting(12, "Q00012_SecretMeetingWithVarkaSilenos", "Secret Meeting With Varka Silenos", 31258, 31378, 7232),
        q13(), q14(), q15(), q16(), q17(), q18(),
    ]
    for q in quests:
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
