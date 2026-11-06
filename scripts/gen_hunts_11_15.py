#!/usr/bin/env python3
"""Hand-parity Mobius hunts Q261 / Q275 / Q295 / Q319 (batch 11–15).

  Q261 Collector's Dream — Alshupes, spider legs ×8, lvl 15+
  Q275 Dark Winged Spies — Tantus (Orc), bat fangs ×70, lvl 11+
  Q295 Dreaming of the Skies — Arin, floating stones ×50, lvl 11+
  Q319 Scent of Death — Minaless, zombie skins ×5, lvl 11+

Simplifications:
  Q275: skip Varangkas Tracker spawn/parasite bonus fangs (Mobius addSpawn).
  Q261: skip newbie-guide screen message.
  Q295: 1 stone + 25% bonus stone ≈ Mobius 75%/25% of 1/2.

After: python3 scripts/gen_hunts_11_15.py
"""
from __future__ import annotations

import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = ROOT / "ref/mobius-ct0-interlude/dist/game/data/scripts/quests"
if not QROOT.is_dir():
    QROOT = ROOT / "ref/mobius-ct0-interlude/data/scripts/quests"
ITEMS_XML = ROOT / "ref/mobius-ct0-interlude/dist/game/data/stats/items"
if not ITEMS_XML.is_dir():
    ITEMS_XML = ROOT / "ref/mobius-ct0-interlude/data/stats/items"

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


def ensure_items(ids: list[int]) -> None:
    path = ROOT / "data" / "items.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    have = {int(i["id"]) for i in data["items"]}
    missing = [i for i in ids if i not in have and i != 57]
    if not missing:
        return
    mobius: dict[int, dict] = {}
    for f in sorted(ITEMS_XML.glob("*.xml")):
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError:
            continue
        for e in root.iter("item"):
            iid = int(e.get("id"))
            if iid not in missing:
                continue
            s = {x.get("name"): x.get("val") for x in e.findall("set")}
            row = {
                "id": iid,
                "name": e.get("name") or f"Item {iid}",
                "kind": "etc",
                "icon": s.get("icon") or "icon.etc_question_mark_i00",
                "price": int(s.get("price") or 0),
                "weight": int(s.get("weight") or 0),
            }
            if s.get("is_stackable") == "true":
                row["stackable"] = True
            if s.get("is_questitem") == "true":
                row["quest"] = True
            mobius[iid] = row
    added = [mobius[i] if i in mobius else {"id": i, "name": f"Quest Item {i}", "kind": "etc", "icon": "icon.etc_question_mark_i00", "price": 0, "weight": 0} for i in missing]
    data["items"].extend(added)
    data["items"].sort(key=lambda r: int(r["id"]))
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"items: added {[a['id'] for a in added]}")


def q261():
    Q, d = 261, html_loader("Q00261_CollectorsDream", 261)
    ALSHUPES = 30222
    LEG = 1087
    MOBS = (20308, 20460, 20466)
    dialogs = [
        d(ALSHUPES, "talk", "30222-01.htm", when="absent", maxLevel=14),
        d(ALSHUPES, "talk", "30222-02.htm", when="absent", minLevel=15),
        d(
            ALSHUPES,
            "event",
            "30222-03.htm",
            when="absent",
            event="30222-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(ALSHUPES, "talk", "30222-04.html", cond=1),
        d(
            ALSHUPES,
            "talk",
            "30222-05.html",
            cond=2,
            atLeast={LEG: 8},
            ops=[adena(1000), exp_sp(2000, 0), {"op": "complete"}],
        ),
    ]
    for mob in MOBS:
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(LEG, 1.0, limit=8, at=2)]))
    return {
        "id": Q,
        "name": "Collector's Dream",
        "repeatable": True,
        "abortable": True,
        "questItems": [LEG],
        "dialogs": dialogs,
    }


def q275():
    Q, d = 275, html_loader("Q00275_DarkWingedSpies", 275)
    TANTUS = 30567
    FANG = 1478
    BAT = 20316
    dialogs = [
        d(TANTUS, "talk", "30567-00.htm", when="absent", races=[0, 1, 2, 4]),
        d(TANTUS, "talk", "30567-01.htm", when="absent", races=[3], maxLevel=10),
        d(TANTUS, "talk", "30567-02.htm", when="absent", races=[3], minLevel=11),
        d(
            TANTUS,
            "event",
            "30567-03.htm",
            when="absent",
            event="30567-03",
            races=[3],
            minLevel=11,
            ops=START + [cond(1)],
        ),
        # Mobius Java wrongly shows 05 for cond1; 04.html is the hunt text.
        d(TANTUS, "talk", "30567-04.html", cond=1),
        d(
            TANTUS,
            "talk",
            "30567-05.html",
            cond=2,
            atLeast={FANG: 70},
            ops=[
                {
                    "op": "sell_items",
                    "prices": [{"itemId": FANG, "adena": 60}],
                },
                {"op": "complete"},
            ],
        ),
        d(BAT, "kill", None, cond=1, raw="", ops=[drop(FANG, 1.0, limit=70, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Dark Winged Spies",
        "repeatable": True,
        "abortable": True,
        "questItems": [FANG],
        "dialogs": dialogs,
    }


def q295():
    Q, d = 295, html_loader("Q00295_DreamingOfTheSkies", 295)
    ARIN = 30536
    STONE, RING = 1492, 1509
    WEAVER = 20153
    dialogs = [
        d(ARIN, "talk", "30536-01.htm", when="absent", maxLevel=10),
        d(ARIN, "talk", "30536-02.htm", when="absent", minLevel=11),
        d(
            ARIN,
            "event",
            "30536-03.htm",
            when="absent",
            event="30536-03",
            minLevel=11,
            ops=START + [cond(1)],
        ),
        d(ARIN, "talk", "30536-04.html", cond=1),
        d(
            ARIN,
            "talk",
            "30536-06.html",
            cond=2,
            atLeast={STONE: 50},
            hasItem=RING,
            ops=[adena(2400), {"op": "complete"}],
        ),
        d(
            ARIN,
            "talk",
            "30536-05.html",
            cond=2,
            atLeast={STONE: 50},
            lacksItems=[RING],
            ops=[give(RING), {"op": "complete"}],
        ),
        # ≈ Mobius: 75%×1 + 25%×2
        d(
            WEAVER,
            "kill",
            None,
            cond=1,
            raw="",
            ops=[drop(STONE, 1.0, limit=50, at=2), drop(STONE, 0.25, limit=50, at=2)],
        ),
    ]
    return {
        "id": Q,
        "name": "Dreaming of the Skies",
        "repeatable": True,
        "abortable": True,
        "questItems": [STONE],
        "dialogs": dialogs,
    }


def q319():
    Q, d = 319, html_loader("Q00319_ScentOfDeath", 319)
    MINALESS = 30138
    SKIN, POT = 1045, 1060
    # getRandom(10) > 7 → 2/10 = 0.2
    dialogs = [
        d(MINALESS, "talk", "30138-02.htm", when="absent", maxLevel=10),
        d(MINALESS, "talk", "30138-03.htm", when="absent", minLevel=11),
        d(
            MINALESS,
            "event",
            "30138-04.htm",
            when="absent",
            event="30138-04",
            minLevel=11,
            ops=START + [cond(1)],
        ),
        d(MINALESS, "talk", "30138-05.html", cond=1),
        d(
            MINALESS,
            "talk",
            "30138-06.html",
            cond=2,
            atLeast={SKIN: 5},
            ops=[adena(3350), give(POT), {"op": "complete"}],
        ),
    ]
    for mob in (20015, 20020):
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(SKIN, 0.2, limit=5, at=2)]))
    return {
        "id": Q,
        "name": "Scent of Death",
        "repeatable": True,
        "abortable": True,
        "questItems": [SKIN],
        "dialogs": dialogs,
    }


def main() -> None:
    quests = [q261(), q275(), q295(), q319()]
    need = [1492, 1509, 1045, 1087, 1478, 1479, 1060]
    ensure_items(need)
    questio.upsert_quests(quests)
    for q in quests:
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
