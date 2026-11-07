#!/usr/bin/env python3
"""Hand-parity Mobius Q636 / Q379 / Q645 / Q647 (pagan mark + wine + grave + golems).

  Q636 Truth Beyond — Eliyah → Flauron visitor mark (zone fade omitted)
  Q379 Fantasy Wine — leaf×80 + chill×100 → old wine (most-common 30yr)
  Q645 Ghosts of Batur — cursed goods×500 → material pick
  Q647 Influx of Machines — shards×500 → adena (recipes 4963–4972 missing)

After: python3 scripts/gen_quests_636_647_mix.py
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

cond = lambda c: {"op": "set_cond", "cond": c}
START = [{"op": "create"}, {"op": "start"}]


def drop(i, chance, limit=0, at=None, count=1):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


def give(i, count=1):
    return {"op": "give_item", "itemId": i, "count": count}


def take(i, count=1):
    return {"op": "take_item", "itemId": i, "count": count}


def adena(n):
    return {"op": "give_adena", "count": n}


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


def q636():
    Q, d = 636, html_loader("Q00636_TruthBeyond", 636)
    ELIYAH, FLAURON = 31329, 32010
    VISITOR, FADED, MARK = 8064, 8065, 8067
    dialogs = [
        d(
            ELIYAH,
            "talk",
            "31329-mark.htm",
            when="absent",
            hasAny=[VISITOR, FADED, MARK],
            ops=[{"op": "complete"}],
        ),
        d(ELIYAH, "talk", "31329-01.htm", when="absent", maxLevel=72),
        d(ELIYAH, "talk", "31329-02.htm", when="absent", minLevel=73),
        d(ELIYAH, "event", "31329-03.htm", when="absent", event="31329-03", minLevel=73),
        d(
            ELIYAH,
            "event",
            "31329-04.htm",
            when="absent",
            event="31329-04",
            minLevel=73,
            ops=START + [cond(1)],
        ),
        d(ELIYAH, "talk", "31329-05.htm", when="started"),
        d(FLAURON, "talk", "32010-01.htm", cond=1),
        d(
            FLAURON,
            "event",
            "32010-02.htm",
            when="started",
            event="32010-02",
            cond=1,
            ops=[give(VISITOR), {"op": "complete"}],
        ),
        d(FLAURON, "talk", "32010-03.htm", when="started"),
    ]
    return {
        "id": Q,
        "name": "Truth Beyond",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def q379():
    Q, d = 379, html_loader("Q00379_FantasyWine", 379)
    HARLAN = 30074
    LEAF, STONE = 5893, 5894
    WINE30 = 5957  # most common (random < 9 → 60%)
    dialogs = [
        d(HARLAN, "talk", "30074-06.html", when="absent", maxLevel=19),
        d(HARLAN, "talk", "30074-01.htm", when="absent", minLevel=20),
        d(HARLAN, "event", "30074-02.htm", when="absent", event="30074-02", minLevel=20),
        d(HARLAN, "event", "30074-03.htm", when="absent", event="30074-03", minLevel=20),
        d(
            HARLAN,
            "event",
            "30074-04.htm",
            when="absent",
            event="30074-04",
            minLevel=20,
            ops=START + [cond(1)],
        ),
        d(HARLAN, "event", "30074-05.html", when="absent", event="30074-05"),
        d(
            HARLAN,
            "talk",
            "30074-10.html",
            when="started",
            atLeast={LEAF: 80, STONE: 100},
        ),
        # Partial leaf/stone nuance (08/09) collapsed — no "below count" gate yet
        d(HARLAN, "talk", "30074-07.html", when="started"),
        d(
            HARLAN,
            "event",
            "30074-12.html",
            when="started",
            event="30074-11",
            atLeast={LEAF: 80, STONE: 100},
            ops=[take(LEAF, 80), take(STONE, 100), give(WINE30), {"op": "complete"}],
        ),
    ]
    # Stay on cond=1 until turn-in (dual-item; no multi-item condAtLimit).
    dialogs.append(
        d(20291, "kill", None, cond=1, raw="", ops=[drop(LEAF, 1.0, limit=80)])
    )
    dialogs.append(
        d(20292, "kill", None, cond=1, raw="", ops=[drop(STONE, 1.0, limit=100)])
    )
    return {
        "id": Q,
        "name": "Fantasy Wine",
        "repeatable": True,
        "abortable": True,
        "questItems": [LEAF, STONE],
        "dialogs": dialogs,
    }


def q645():
    Q, d = 645, html_loader("Q00645_GhostsOfBatur", 645)
    KARUDA = 32017
    GOODS = 8089
    NEED = 500  # Mobius Java (HTML text still says 180)
    rewards = {
        "0": (1878, 18),  # Braided Hemp
        "1": (1879, 7),   # Cokes
        "2": (1880, 4),   # Steel
        "3": (1881, 6),   # Coarse Bone Powder
        "4": (1882, 10),  # Leather
        "5": (1883, 2),   # Steel Mold
    }
    mobs = [22007, 22009, 22010, 22011, 22012, 22013, 22014, 22015, 22016]
    dialogs = [
        d(KARUDA, "talk", "32017-02.htm", when="absent", maxLevel=22),
        d(KARUDA, "talk", "32017-01.htm", when="absent", minLevel=23),
        d(
            KARUDA,
            "event",
            "32017-03.htm",
            when="absent",
            event="32017-03",
            minLevel=23,
            ops=START + [cond(1)],
        ),
        d(KARUDA, "talk", "32017-05.htm", cond=2, atLeast={GOODS: NEED}),
        d(KARUDA, "talk", "32017-04.htm", when="started"),
        d(
            KARUDA,
            "event",
            "32017-06.htm",
            event="32017-06",
            cond=2,
            atLeast={GOODS: NEED},
        ),
    ]
    for ev, (item, count) in rewards.items():
        dialogs.append(
            d(
                KARUDA,
                "event",
                "32017-07.htm",
                event=ev,
                cond=2,
                atLeast={GOODS: NEED},
                ops=[take(GOODS, NEED), give(item, count), {"op": "complete"}],
            )
        )
        dialogs.append(d(KARUDA, "event", "32017-04.htm", event=ev, when="started"))
    for mob in mobs:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(GOODS, 0.75, limit=NEED, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Ghosts of Batur",
        "repeatable": True,
        "abortable": True,
        "questItems": [GOODS],
        "dialogs": dialogs,
    }


def q647():
    Q, d = 647, html_loader("Q00647_InfluxOfMachines", 647)
    GUTEN = 32069
    SHARD = 8100
    NEED = 500
    # Recipes 4963–4972 missing → fixed adena stand-in
    dialogs = [
        d(GUTEN, "talk", "32069-03.htm", when="absent", maxLevel=45),
        d(GUTEN, "talk", "32069-01.htm", when="absent", minLevel=46),
        d(
            GUTEN,
            "event",
            "32069-02.htm",
            when="absent",
            event="32069-02",
            minLevel=46,
            ops=START + [cond(1)],
        ),
        d(GUTEN, "talk", "32069-05.htm", cond=2, atLeast={SHARD: NEED}),
        d(GUTEN, "talk", "32069-04.htm", when="started"),
        d(
            GUTEN,
            "event",
            "32069-06.htm",
            event="32069-06",
            cond=2,
            atLeast={SHARD: NEED},
            ops=[take(SHARD, NEED), adena(180000), {"op": "complete"}],
        ),
    ]
    for mob in range(22052, 22079):
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(SHARD, 0.3, limit=NEED, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Influx of Machines",
        "repeatable": True,
        "abortable": True,
        "questItems": [SHARD],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q636(), q379(), q645(), q647()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
