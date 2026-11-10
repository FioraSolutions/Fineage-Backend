#!/usr/bin/env python3
"""Hand-parity Mobius Q262 / Q303 / Q313 / Q320 (batch 8–10 collects).

  Q262 Trade With The Ivory Tower — Vollodos, spore sacs ×10
  Q303 Collect Arrowheads — Minia, orcish arrowheads ×10
  Q313 Collect Spores — Herbiel, spore sacs ×10
  Q320 Bones Tell The Future — Kaitar DE, bone fragments ×10

Simplifications:
  Q262/303/320: party credit via party:true (Mobius getRandomPartyMember*).
  Q313: killer-only (Mobius getQuestState(killer)).

After: python3 scripts/gen_quests_8_10_collects.py
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


def q262():
    Q, d = 262, html_loader("Q00262_TradeWithTheIvoryTower", 262)
    NPC, SAC = 30137, 707
    dialogs = [
        d(NPC, "talk", "30137-01.htm", when="absent", maxLevel=7),
        d(NPC, "talk", "30137-02.htm", when="absent", minLevel=8),
        d(
            NPC,
            "event",
            "30137-03.htm",
            when="absent",
            event="30137-03",
            minLevel=8,
            ops=START + [cond(1)],
        ),
        d(NPC, "talk", "30137-04.html", cond=1),
        d(
            NPC,
            "talk",
            "30137-05.html",
            cond=2,
            atLeast={SAC: 10},
            ops=[adena(3000), {"op": "complete"}],
        ),
        d(20007, "kill", None, cond=1, party=True, raw="", ops=[drop(SAC, 0.3, limit=10, at=2)]),
        d(20400, "kill", None, cond=1, party=True, raw="", ops=[drop(SAC, 0.4, limit=10, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Trade With The Ivory Tower",
        "repeatable": True,
        "abortable": True,
        "questItems": [SAC],
        "dialogs": dialogs,
    }


def q303():
    Q, d = 303, html_loader("Q00303_CollectArrowheads", 303)
    NPC, HEAD = 30029, 963
    dialogs = [
        d(NPC, "talk", "30029-02.htm", when="absent", maxLevel=9),
        d(NPC, "talk", "30029-03.htm", when="absent", minLevel=10),
        d(
            NPC,
            "event",
            "30029-04.htm",
            when="absent",
            event="30029-04",
            minLevel=10,
            ops=START + [cond(1)],
        ),
        d(NPC, "talk", "30029-05.html", cond=1),
        d(
            NPC,
            "talk",
            "30029-06.html",
            cond=2,
            atLeast={HEAD: 10},
            ops=[adena(1000), exp_sp(2000, 0), {"op": "complete"}],
        ),
        d(20361, "kill", None, cond=1, party=True, raw="", ops=[drop(HEAD, 0.4, limit=10, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Collect Arrowheads",
        "repeatable": True,
        "abortable": True,
        "questItems": [HEAD],
        "dialogs": dialogs,
    }


def q313():
    Q, d = 313, html_loader("Q00313_CollectSpores", 313)
    NPC, SAC = 30150, 1118
    dialogs = [
        d(NPC, "talk", "30150-02.htm", when="absent", maxLevel=7),
        d(NPC, "talk", "30150-03.htm", when="absent", minLevel=8),
        d(NPC, "event", "30150-04.htm", when="absent", event="30150-04", minLevel=8),
        d(
            NPC,
            "event",
            "30150-05.htm",
            when="absent",
            event="30150-05",
            minLevel=8,
            ops=START + [cond(1)],
        ),
        d(NPC, "talk", "30150-06.html", cond=1),
        d(
            NPC,
            "talk",
            "30150-07.html",
            cond=2,
            atLeast={SAC: 10},
            ops=[adena(3500), {"op": "complete"}],
        ),
        # Killer-only — NetQuestKillTests.Spore_fungus_kill_drops_spore_sacs
        d(20509, "kill", None, cond=1, raw="", ops=[drop(SAC, 0.4, limit=10, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Collect Spores",
        "repeatable": True,
        "abortable": True,
        "questItems": [SAC],
        "dialogs": dialogs,
    }


def q320():
    Q, d = 320, html_loader("Q00320_BonesTellTheFuture", 320)
    NPC, BONE = 30359, 809
    dialogs = [
        d(NPC, "talk", "30359-00.htm", when="absent", races=[0, 1, 3, 4]),
        d(NPC, "talk", "30359-02.htm", when="absent", races=[2], maxLevel=9),
        d(NPC, "talk", "30359-03.htm", when="absent", races=[2], minLevel=10),
        d(
            NPC,
            "event",
            "30359-04.htm",
            when="absent",
            event="30359-04",
            races=[2],
            minLevel=10,
            ops=START + [cond(1)],
        ),
        d(
            NPC,
            "talk",
            "30359-06.html",
            when="started",
            atLeast={BONE: 10},
            ops=[adena(8470), {"op": "complete"}],
        ),
        d(NPC, "talk", "30359-05.html", when="started"),
        d(20517, "kill", None, cond=1, party=True, raw="", ops=[drop(BONE, 0.18, limit=10, at=2)]),
        d(20518, "kill", None, cond=1, party=True, raw="", ops=[drop(BONE, 0.18, limit=10, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Bones Tell The Future",
        "repeatable": True,
        "abortable": True,
        "questItems": [BONE],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q262(), q303(), q313(), q320()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
