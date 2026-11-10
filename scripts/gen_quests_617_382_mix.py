#!/usr/bin/env python3
"""Hand-parity Mobius Q617 / Q382 / Q358 (torches + Kail coins + snake scales).

  Q617 Gather the Flames — torch farm → S-weapon recipes (Vulcan=6881 fixed; Rooney pick)
  Q382 Kail's Magic Coin — farm coins while holding Royal Membership
  Q358 Illegitimate Child of the Goddess — snake scales×108 → random sealed recipe

After: python3 scripts/gen_quests_617_382_mix.py
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


def q617():
    Q, d = 617, html_loader("Q00617_GatherTheFlames", 617)
    HILDA, VULCAN, ROONEY = 31271, 31539, 32049
    TORCH = 7264
    chances = {
        21381: 0.51, 21653: 0.51, 21387: 0.53, 21655: 0.53, 21390: 0.56, 21656: 0.69,
        21389: 0.55, 21388: 0.53, 21383: 0.51, 21392: 0.56, 21382: 0.60, 21654: 0.52,
        21384: 0.64, 21394: 0.51, 21395: 0.56, 21385: 0.52, 21391: 0.55, 21393: 0.58,
        21657: 0.57, 21386: 0.52, 21652: 0.49, 21378: 0.49, 21376: 0.48, 21377: 0.48,
        21379: 0.59, 21380: 0.49,
    }
    rooney = [6881, 6883, 6885, 6887, 6891, 6893, 6895, 6897, 6899, 7580, 6889]
    dialogs = [
        d(VULCAN, "talk", "31539-02.htm", when="absent", maxLevel=73),
        d(VULCAN, "talk", "31539-01.htm", when="absent", minLevel=74),
        d(
            VULCAN,
            "event",
            "31539-03.htm",
            when="absent",
            event="31539-03",
            minLevel=74,
            ops=START + [cond(1)],
        ),
        d(HILDA, "talk", "31271-02.htm", when="absent", maxLevel=73),
        d(HILDA, "talk", "31271-01.htm", when="absent", minLevel=74),
        d(
            HILDA,
            "event",
            "31271-03.htm",
            when="absent",
            event="31271-03",
            minLevel=74,
            ops=START + [cond(1)],
        ),
        d(HILDA, "talk", "31271-04.htm", when="started"),
        d(VULCAN, "talk", "31539-04.htm", when="started", atLeast={TORCH: 1000}),
        d(VULCAN, "talk", "31539-05.htm", when="started"),
        d(VULCAN, "event", "31539-06.htm", when="started", event="31539-06"),
        # Mobius random among REWARD[] — fix most common / first available Forgotten Blade
        d(
            VULCAN,
            "event",
            "31539-07.htm",
            when="started",
            event="31539-05",
            atLeast={TORCH: 1000},
            ops=[take(TORCH, 1000), give(6881)],
        ),
        d(VULCAN, "event", "31539-05.htm", when="started", event="31539-05"),
        d(
            VULCAN,
            "event",
            "31539-08.htm",
            when="started",
            event="31539-08",
            ops=[{"op": "complete"}],
        ),
        d(ROONEY, "talk", "32049-01.htm", when="started", atLeast={TORCH: 1200}),
        d(ROONEY, "talk", "32049-02.htm", when="started"),
    ]
    for rid in rooney:
        dialogs.append(
            d(
                ROONEY,
                "event",
                "32049-03.htm",
                when="started",
                event=str(rid),
                atLeast={TORCH: 1200},
                ops=[take(TORCH, 1200), give(rid)],
            )
        )
        dialogs.append(
            d(ROONEY, "event", "32049-02.htm", when="started", event=str(rid))
        )
    for mob, ch in chances.items():
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(TORCH, ch)])
        )
    return {
        "id": Q,
        "name": "Gather the Flames",
        "repeatable": True,
        "abortable": True,
        "questItems": [TORCH],
        "dialogs": dialogs,
    }


def q382():
    Q, d = 382, html_loader("Q00382_KailsMagicCoin", 382)
    VERGARA = 30687
    ROYAL = 5898
    SILVER, GOLD, BLOOD = 5961, 5962, 5963
    dialogs = [
        d(VERGARA, "talk", "30687-01.htm", when="absent", lacksItems=[ROYAL]),
        d(VERGARA, "talk", "30687-01.htm", when="absent", hasItem=ROYAL, maxLevel=54),
        d(VERGARA, "talk", "30687-02.htm", when="absent", hasItem=ROYAL, minLevel=55),
        d(
            VERGARA,
            "event",
            "30687-03.htm",
            when="absent",
            event="30687-03",
            hasItem=ROYAL,
            minLevel=55,
            ops=START + [cond(1)],
        ),
        d(VERGARA, "talk", "30687-04.htm", when="started"),
        d(VERGARA, "event", "30687-05.htm", when="started", event="30687-05"),
        d(VERGARA, "event", "30687-06.htm", when="started", event="30687-06"),
    ]
    # Drops require royal membership still held
    dialogs.append(
        d(
            21017,
            "kill",
            None,
            when="started",
            hasItem=ROYAL,
            raw="",
            ops=[drop(SILVER, 0.073)],
        )
    )
    dialogs.append(
        d(
            21019,
            "kill",
            None,
            when="started",
            hasItem=ROYAL,
            raw="",
            ops=[drop(GOLD, 0.075)],
        )
    )
    dialogs.append(
        d(
            21020,
            "kill",
            None,
            when="started",
            hasItem=ROYAL,
            raw="",
            ops=[drop(BLOOD, 0.073)],
        )
    )
    # Captain: random among 3 coins — drop silver as stand-in (Mobius random 0..2)
    dialogs.append(
        d(
            21022,
            "kill",
            None,
            when="started",
            hasItem=ROYAL,
            raw="",
            ops=[drop(SILVER, 0.069)],
        )
    )
    return {
        "id": Q,
        "name": "Kail's Magic Coin",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def q358():
    Q, d = 358, html_loader("Q00358_IllegitimateChildOfTheGoddess", 358)
    OLTRAN = 30862
    SCALE = 5868
    NEED = 108
    # Random among REWARDS — fix first (Dark Crystal Shield recipe)
    REWARD = 5364
    dialogs = [
        d(OLTRAN, "talk", "30862-05.html", when="absent", maxLevel=62),
        d(OLTRAN, "talk", "30862-01.htm", when="absent", minLevel=63),
        d(OLTRAN, "event", "30862-02.htm", when="absent", event="30862-02", minLevel=63),
        d(OLTRAN, "event", "30862-03.htm", when="absent", event="30862-03", minLevel=63),
        d(
            OLTRAN,
            "event",
            "30862-04.htm",
            when="absent",
            event="30862-04",
            minLevel=63,
            ops=START + [cond(1)],
        ),
        d(
            OLTRAN,
            "talk",
            "30862-07.html",
            when="started",
            atLeast={SCALE: NEED},
            ops=[take(SCALE, NEED), give(REWARD), {"op": "complete"}],
        ),
        d(OLTRAN, "talk", "30862-06.html", when="started"),
    ]
    for mob, ch in ((20672, 0.71), (20673, 0.74)):
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                party=True,
                raw="",
                ops=[drop(SCALE, ch, limit=NEED, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Illegitimate Child of the Goddess",
        "repeatable": True,
        "abortable": True,
        "questItems": [SCALE],
        "dialogs": dialogs,
    }


def main() -> None:
    for q in (q617(), q382(), q358()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
