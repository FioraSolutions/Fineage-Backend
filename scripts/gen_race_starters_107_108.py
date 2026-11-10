#!/usr/bin/env python3
"""Hand-parity Mobius race starters Q107 / Q108 (batch 0–10).

  Q107 Merciless Punishment — Orc (Hatos/Parugon), lvl 10+
  Q108 Jumble, Tumble, Diamond Fuss — Dwarf (Gouph chain), lvl 10+

Simplifications:
  Q107 quit (30568-07) uses complete + 200 adena (Mobius exitQuest(true) allows retake).
  Q108: cond 6 may be skipped if both gems hit 10 on the same kill — Brunon also
        accepts cond 5 with 10+10 gems.

After: python3 scripts/gen_race_starters_107_108.py
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
ECHO = [(1060, 100), (4412, 10), (4413, 10), (4414, 10), (4415, 10), (4416, 10)]


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


def finish(weapon: int, exp: int, sp: int, aden: int) -> list:
    ops = [give(weapon)]
    for iid, c in ECHO:
        ops.append(give(iid, c))
    ops.extend([adena(aden), exp_sp(exp, sp), {"op": "complete"}])
    return ops


def q107():
    Q, d = 107, html_loader("Q00107_MercilessPunishment", 107)
    HATOS, PARUGON = 30568, 30580
    O1, O2, O3 = 1553, 1554, 1555
    L_DE, L_HU, L_ELF = 1556, 1557, 1558
    BUTCHER = 1510
    MOB = 27041
    dialogs = [
        d(HATOS, "talk", "30568-01.htm", when="absent", races=[0, 1, 2, 4]),
        d(HATOS, "talk", "30568-02.htm", when="absent", races=[3], maxLevel=9),
        d(HATOS, "talk", "30568-03.htm", when="absent", races=[3], minLevel=10),
        d(
            HATOS,
            "event",
            "30568-04.htm",
            when="absent",
            event="30568-04",
            races=[3],
            minLevel=10,
            ops=START + [give(O1), cond(1)],
        ),
        d(HATOS, "talk", "30568-05.html", cond=1, hasItem=O1),
        d(HATOS, "talk", "30568-05.html", cond=2, hasItem=O1),
        # Quit / continue after first letter
        d(HATOS, "talk", "30568-06.html", cond=3, hasItems=[O1, L_HU]),
        d(
            HATOS,
            "event",
            "30568-07.html",
            event="30568-07",
            when="started",
            ops=[adena(200), {"op": "complete"}],
        ),
        d(
            HATOS,
            "event",
            "30568-08.html",
            event="30568-08",
            cond=3,
            hasItem=O1,
            ops=[take(O1), give(O2), cond(4)],
        ),
        d(HATOS, "talk", "30568-08.html", cond=4, hasItems=[O2, L_HU]),
        d(HATOS, "talk", "30568-09.html", cond=5, hasItems=[O2, L_HU, L_DE]),
        d(
            HATOS,
            "event",
            "30568-10.html",
            event="30568-10",
            cond=5,
            hasItem=O2,
            ops=[take(O2), give(O3), cond(6)],
        ),
        d(HATOS, "talk", "30568-10.html", cond=6, hasItems=[O3, L_HU, L_DE]),
        d(
            HATOS,
            "talk",
            "30568-11.html",
            cond=7,
            hasItems=[O3, L_HU, L_DE, L_ELF],
            ops=finish(BUTCHER, 34565, 2962, 14666),
        ),
        d(PARUGON, "talk", "30580-01.html", cond=1, hasItem=O1, ops=[cond(2)]),
        d(MOB, "kill", None, cond=2, hasItem=O1, raw="", ops=[give(L_HU), cond(3)]),
        d(MOB, "kill", None, cond=4, hasItem=O2, raw="", ops=[give(L_DE), cond(5)]),
        d(MOB, "kill", None, cond=6, hasItem=O3, raw="", ops=[give(L_ELF), cond(7)]),
    ]
    return {
        "id": Q,
        "name": "Merciless Punishment",
        "repeatable": False,
        "abortable": True,
        "questItems": [O1, O2, O3, L_DE, L_HU, L_ELF],
        "dialogs": dialogs,
    }


def q108():
    Q, d = 108, html_loader("Q00108_JumbleTumbleDiamondFuss", 108)
    GOUPH, REEP, TOROCCO, MARON = 30523, 30516, 30555, 30529
    BRUNON, MURDOC, AIRY = 30526, 30521, 30522
    (
        G_CON,
        R_CON,
        WINE,
        DICE,
        B_CON,
        AQUA,
        CHRYS,
        BOX,
        COAL,
        LETTER,
        TART,
        DIAG,
        STAR,
    ) = range(1559, 1572)
    HAMMER = 1511
    LEADER, LT, BAT = 20323, 20324, 20480
    MID = [R_CON, WINE, DICE, B_CON]
    LATE = [COAL, LETTER, TART, DIAG]

    def gem_turnin(c):
        return d(
            BRUNON,
            "talk",
            "30526-04.html",
            cond=c,
            hasItem=B_CON,
            atLeast={AQUA: 10, CHRYS: 10},
            ops=[take(B_CON), take(AQUA, 10), take(CHRYS, 10), give(BOX), cond(7)],
        )

    dialogs = [
        d(GOUPH, "talk", "30523-01.htm", when="absent", races=[0, 1, 2, 3]),
        d(GOUPH, "talk", "30523-02.htm", when="absent", races=[4], maxLevel=9),
        d(GOUPH, "talk", "30523-03.htm", when="absent", races=[4], minLevel=10),
        d(
            GOUPH,
            "event",
            "30523-04.htm",
            when="absent",
            event="30523-04",
            races=[4],
            minLevel=10,
            ops=START + [give(G_CON), cond(1)],
        ),
        d(GOUPH, "talk", "30523-05.html", cond=1, hasItem=G_CON),
        d(
            GOUPH,
            "talk",
            "30523-09.html",
            cond=12,
            hasItem=STAR,
            ops=finish(HAMMER, 34565, 2962, 14666),
        ),
        d(
            GOUPH,
            "talk",
            "30523-07.html",
            cond=7,
            hasItem=BOX,
            ops=[take(BOX), give(COAL), cond(8)],
        ),
        d(GOUPH, "talk", "30523-06.html", when="started", hasAny=MID),
        # After finish/box — diagram may still be held with the star.
        d(GOUPH, "talk", "30523-08.html", when="started", hasAny=LATE, lacksItems=[STAR]),
        # Reep
        d(
            REEP,
            "talk",
            "30516-01.html",
            cond=1,
            hasItem=G_CON,
            ops=[take(G_CON), give(R_CON), cond(2)],
        ),
        d(REEP, "talk", "30516-02.html", when="started", hasAny=[R_CON] + MID[1:]),
        # Torocco
        d(TOROCCO, "talk", "30555-01.html", cond=2, hasItem=R_CON),
        d(
            TOROCCO,
            "event",
            "30555-02.html",
            event="30555-02",
            cond=2,
            hasItem=R_CON,
            ops=[take(R_CON), give(WINE), cond(3)],
        ),
        d(TOROCCO, "talk", "30555-03.html", cond=3, hasItem=WINE),
        d(TOROCCO, "talk", "30555-04.html", cond=7, hasItem=BOX),
        d(TOROCCO, "talk", "30555-05.html", when="started"),
        # Maron
        d(
            MARON,
            "talk",
            "30529-01.html",
            cond=3,
            hasItem=WINE,
            ops=[take(WINE), give(DICE), cond(4)],
        ),
        d(MARON, "talk", "30529-02.html", cond=4, hasItem=DICE),
        d(MARON, "talk", "30529-03.html", when="started"),
        # Brunon
        d(BRUNON, "talk", "30526-01.html", cond=4, hasItem=DICE),
        d(
            BRUNON,
            "event",
            "30526-02.html",
            event="30526-02",
            cond=4,
            hasItem=DICE,
            ops=[take(DICE), give(B_CON), cond(5)],
        ),
        # Gem turn-in before the "still hunting" page (same cond/item gates).
        gem_turnin(6),
        gem_turnin(5),
        d(BRUNON, "talk", "30526-03.html", cond=5, hasItem=B_CON),
        d(BRUNON, "talk", "30526-05.html", cond=7, hasItem=BOX),
        d(
            BRUNON,
            "talk",
            "30526-06.html",
            cond=8,
            hasItem=COAL,
            ops=[take(COAL), give(LETTER), cond(9)],
        ),
        d(BRUNON, "talk", "30526-07.html", cond=9, hasItem=LETTER),
        d(BRUNON, "talk", "30526-08.html", when="started", hasAny=[TART, DIAG, STAR]),
        # Murdoc
        d(
            MURDOC,
            "talk",
            "30521-01.html",
            cond=9,
            hasItem=LETTER,
            ops=[take(LETTER), give(TART), cond(10)],
        ),
        d(MURDOC, "talk", "30521-02.html", cond=10, hasItem=TART),
        d(MURDOC, "talk", "30521-03.html", when="started"),
        # Airy
        d(
            AIRY,
            "talk",
            "30522-01.html",
            cond=10,
            hasItem=TART,
            ops=[take(TART), give(DIAG), cond(11)],
        ),
        d(AIRY, "talk", "30522-02.html", cond=11, hasItem=DIAG),
        d(AIRY, "talk", "30522-03.html", cond=12, hasItem=STAR),
        d(AIRY, "talk", "30522-04.html", when="started"),
    ]
    for mob, ch in ((LEADER, 0.8), (LT, 0.6)):
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=5,
                hasItem=B_CON,
                atLeast={CHRYS: 10},
                raw="",
                ops=[drop(AQUA, ch, limit=10, at=6)],
            )
        )
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=5,
                hasItem=B_CON,
                atLeast={AQUA: 10},
                raw="",
                ops=[drop(CHRYS, ch, limit=10, at=6)],
            )
        )
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=5,
                hasItem=B_CON,
                raw="",
                ops=[drop(AQUA, ch, limit=10), drop(CHRYS, ch, limit=10)],
            )
        )
    dialogs.append(
        d(
            BAT,
            "kill",
            None,
            cond=11,
            hasItem=DIAG,
            raw="",
            ops=[drop(STAR, 0.2, limit=1, at=12), take(DIAG)],
        )
    )
    # Bat: take diagram only when star drops — take always would strip diagram on miss.
    # Fix: two-step — drop sets cond 12; separate talk isn't available. Use:
    # drop star at 12, and on cond 12 diagram may remain until Gouph? Mobius takes on drop.
    # Approximate: kill dialog with only drop; Gouph finish doesn't need diagram gone.
    # Re-add as drop-only; take diagram when star present via... next kill won't match cond 11.
    dialogs[-1] = d(
        BAT,
        "kill",
        None,
        cond=11,
        hasItem=DIAG,
        lacksItems=[STAR],
        raw="",
        ops=[drop(STAR, 0.2, limit=1, at=12)],
    )
    # When star obtained, strip diagram on any started talk to Airy/Gouph — or take on cond12 kill miss.
    # Take diagram when talking Airy on cond 12 / Gouph finish wipes quest items on complete.
    return {
        "id": Q,
        "name": "Jumble, Tumble, Diamond Fuss",
        "repeatable": False,
        "abortable": True,
        "questItems": [
            G_CON,
            R_CON,
            WINE,
            DICE,
            B_CON,
            AQUA,
            CHRYS,
            BOX,
            COAL,
            LETTER,
            TART,
            DIAG,
            STAR,
        ],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q107(), q108()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
