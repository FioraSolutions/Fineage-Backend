#!/usr/bin/env python3
"""Hand-parity port of Mobius dwarf early hunts Q291–294 (batch 0–10).

Mobius:
  Q291 Revenge of the Redbonnet — Maryse, Black Wolf pelts ×40, random grandma reward
  Q292 Brigands Sweep — Spiron/Balanki, goblin jewelry + suspicious memo/contract
  Q293 The Hidden Veins — Filaur/Chichirin, chrysolite ore + map fragments
  Q294 Covert Business — Keef, bat fangs ×100 → Ring of Raccoon

Simplifications (same style as gen_early_wave2_hunts.py):
  Q291: random grandma reward → Grandma's Hairpin + SoE (most common Mobius branch, ~55%).
  Q292: memo→contract converts on Spiron talk when ≥3 memos (Mobius converts on the 3rd kill).
  Q292/Q293: independent drop rolls (Mobius if/else exclusive) — rates match Mobius thresholds.
  Q294: fang count per kill fixed to expected Mobius average (2).

After: python3 scripts/gen_dwarf_hunts_291_294.py
Then: python3 scripts/ensure_quest_items_backend.py  (or inline ensure below)
      python3 scripts/gen_quest_item_flags.py
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


def sell(prices, bonus_min=0, bonus_adena=0):
    row = {"op": "sell_items", "prices": [{"itemId": i, "adena": a} for i, a in prices]}
    if bonus_min:
        row["bonusMin"] = bonus_min
    if bonus_adena:
        row["bonusAdena"] = bonus_adena
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
    """Pull missing Etc rows from Mobius item XML into data/items.json."""
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
    added = []
    for iid in missing:
        if iid in mobius:
            added.append(mobius[iid])
        else:
            added.append(
                {
                    "id": iid,
                    "name": f"Quest Item {iid}",
                    "kind": "etc",
                    "icon": "icon.etc_question_mark_i00",
                    "price": 0,
                    "weight": 0,
                }
            )
    data["items"].extend(added)
    data["items"].sort(key=lambda r: int(r["id"]))
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"items: added {len(added)} -> {[a['id'] for a in added]}")


def q291():
    Q, d = 291, html_loader("Q00291_RevengeOfTheRedbonnet", 291)
    MARYSE = 30553
    PELT, SOE, HAIRPIN = 1482, 736, 1505
    WOLF = 20317
    dialogs = [
        d(MARYSE, "talk", "30553-01.htm", when="absent", maxLevel=3),
        d(MARYSE, "talk", "30553-02.htm", when="absent", minLevel=4),
        d(
            MARYSE,
            "event",
            "30553-03.htm",
            when="absent",
            event="30553-03",
            minLevel=4,
            ops=START + [cond(1)],
        ),
        d(MARYSE, "talk", "30553-04.html", cond=1),
        d(
            MARYSE,
            "talk",
            "30553-05.html",
            cond=2,
            atLeast={PELT: 40},
            ops=[
                take(PELT, 40),
                give(HAIRPIN),
                give(SOE),
                {"op": "complete"},
            ],
        ),
        d(WOLF, "kill", None, cond=1, raw="", ops=[drop(PELT, 1.0, limit=40, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Revenge of the Redbonnet",
        "repeatable": True,
        "abortable": True,
        "questItems": [PELT],
        "dialogs": dialogs,
    }


def q292():
    Q, d = 292, html_loader("Q00292_BrigandsSweep", 292)
    SPIRON, BALANKI = 30532, 30533
    NECK, PEND, LORD, MEMO, CONTRACT = 1483, 1484, 1485, 1486, 1487
    JEWEL = [NECK, PEND, LORD]
    MOBS = {20322: NECK, 20323: PEND, 20324: NECK, 20327: NECK, 20528: LORD}
    prices = [(NECK, 12), (PEND, 36), (LORD, 33)]
    payout = sell(prices, bonus_min=10, bonus_adena=1000)
    dialogs = [
        d(SPIRON, "talk", "30532-00.htm", when="absent", races=[0, 1, 2, 3]),
        d(SPIRON, "talk", "30532-01.htm", when="absent", races=[4], maxLevel=4),
        d(SPIRON, "talk", "30532-02.htm", when="absent", races=[4], minLevel=5),
        d(
            SPIRON,
            "event",
            "30532-03.htm",
            when="absent",
            event="30532-03",
            races=[4],
            minLevel=5,
            ops=START + [cond(1)],
        ),
        d(SPIRON, "event", "30532-06.html", when="started", event="30532-06", ops=[{"op": "complete"}]),
        d(SPIRON, "event", "30532-07.html", when="started", event="30532-07"),
        # 3 memos → contract turn-in (+ jewelry if held). Mobius converts on the 3rd kill.
        d(
            SPIRON,
            "talk",
            "30532-10.html",
            when="started",
            hasAny=JEWEL,
            atLeast={MEMO: 3},
            lacksItems=[CONTRACT],
            ops=[payout, take(MEMO, 3), cond(2), adena(1120)],
        ),
        d(
            SPIRON,
            "talk",
            "30532-10.html",
            when="started",
            atLeast={MEMO: 3},
            lacksItems=[CONTRACT],
            ops=[take(MEMO, 3), cond(2), adena(1120)],
        ),
        # Jewelry + contract
        d(
            SPIRON,
            "talk",
            "30532-10.html",
            when="started",
            hasAny=JEWEL,
            hasItem=CONTRACT,
            ops=[payout, take(CONTRACT), adena(1120)],
        ),
        # Jewelry + ≥2 memos (still hunting contract)
        d(
            SPIRON,
            "talk",
            "30532-09.html",
            when="started",
            hasAny=JEWEL,
            atLeast={MEMO: 2},
            lacksItems=[CONTRACT],
            ops=[payout],
        ),
        # Jewelry + 1 memo
        d(
            SPIRON,
            "talk",
            "30532-08.html",
            when="started",
            hasAny=JEWEL,
            hasItem=MEMO,
            lacksItems=[CONTRACT],
            ops=[payout],
        ),
        # Jewelry only
        d(
            SPIRON,
            "talk",
            "30532-05.html",
            when="started",
            hasAny=JEWEL,
            lacksItems=[MEMO, CONTRACT],
            ops=[payout],
        ),
        # Contract only
        d(
            SPIRON,
            "talk",
            "30532-10.html",
            when="started",
            hasItem=CONTRACT,
            lacksItems=JEWEL,
            ops=[take(CONTRACT), adena(1120)],
        ),
        # Memo progress (no jewelry) — ≥2 before 1
        d(
            SPIRON,
            "talk",
            "30532-09.html",
            when="started",
            atLeast={MEMO: 2},
            lacksItems=JEWEL + [CONTRACT],
        ),
        d(
            SPIRON,
            "talk",
            "30532-08.html",
            when="started",
            hasItem=MEMO,
            lacksItems=JEWEL + [CONTRACT],
        ),
        d(SPIRON, "talk", "30532-04.html", when="started", lacksItems=JEWEL + [MEMO, CONTRACT]),
        d(
            BALANKI,
            "talk",
            "30533-02.html",
            when="started",
            hasItem=CONTRACT,
            ops=[take(CONTRACT), adena(620)],
        ),
        d(BALANKI, "talk", "30533-01.html", when="started", lacksItems=[CONTRACT]),
    ]
    # One kill dialog per mob (Find returns first match). Cond1: jewelry + memo; else jewelry only.
    for mob, item in MOBS.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                cond=1,
                lacksItems=[CONTRACT],
                raw="",
                ops=[drop(item, 0.4), drop(MEMO, 0.1, limit=3, at=2)],
            )
        )
        dialogs.append(d(mob, "kill", None, when="started", raw="", ops=[drop(item, 0.4)]))
    return {
        "id": Q,
        "name": "Brigands Sweep",
        "repeatable": True,
        "abortable": True,
        "questItems": [NECK, PEND, LORD, MEMO, CONTRACT],
        "dialogs": dialogs,
    }


def q293():
    Q, d = 293, html_loader("Q00293_TheHiddenVeins", 293)
    FILAUR, CHICHIRIN = 30535, 30539
    ORE, FRAG, MAP = 1488, 1489, 1490
    MOBS = (20446, 20447, 20448)
    dialogs = [
        d(FILAUR, "talk", "30535-01.htm", when="absent", races=[0, 1, 2, 3]),
        d(FILAUR, "talk", "30535-02.htm", when="absent", races=[4], maxLevel=5),
        d(FILAUR, "talk", "30535-03.htm", when="absent", races=[4], minLevel=6),
        d(
            FILAUR,
            "event",
            "30535-04.htm",
            when="absent",
            event="30535-04",
            races=[4],
            minLevel=6,
            ops=START + [cond(1)],
        ),
        d(FILAUR, "event", "30535-07.html", when="started", event="30535-07", ops=[{"op": "complete"}]),
        d(FILAUR, "event", "30535-08.html", when="started", event="30535-08"),
        d(
            FILAUR,
            "talk",
            "30535-10.html",
            when="started",
            hasItems=[ORE, MAP],
            ops=[sell([(ORE, 5), (MAP, 500)], bonus_min=10, bonus_adena=2000)],
        ),
        d(
            FILAUR,
            "talk",
            "30535-06.html",
            when="started",
            hasItem=ORE,
            lacksItems=[MAP],
            ops=[sell([(ORE, 5), (MAP, 500)], bonus_min=10, bonus_adena=2000)],
        ),
        d(
            FILAUR,
            "talk",
            "30535-09.html",
            when="started",
            hasItem=MAP,
            lacksItems=[ORE],
            ops=[sell([(ORE, 5), (MAP, 500)], bonus_min=10, bonus_adena=2000)],
        ),
        d(FILAUR, "talk", "30535-05.html", when="started", lacksItems=[ORE, MAP]),
        d(CHICHIRIN, "talk", "30539-01.html", when="started"),
        d(
            CHICHIRIN,
            "event",
            "30539-03.html",
            when="started",
            event="30539-03",
            atLeast={FRAG: 4},
            ops=[take(FRAG, 4), give(MAP)],
        ),
        # Same event, no atLeast — refuse when under 4 fragments.
        d(CHICHIRIN, "event", "30539-02.html", when="started", event="30539-03"),
    ]
    for mob in MOBS:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                raw="",
                ops=[drop(ORE, 0.5), drop(FRAG, 0.05)],
            )
        )
    return {
        "id": Q,
        "name": "The Hidden Veins",
        "repeatable": True,
        "abortable": True,
        "questItems": [ORE, FRAG, MAP],
        "dialogs": dialogs,
    }


def q294():
    Q, d = 294, html_loader("Q00294_CovertBusiness", 294)
    KEEF = 30534
    FANG, RING = 1491, 1508
    # Mobius expected fangs/kill ≈ 2.3 (20370) / 1.9 (20480) — use count=2 always.
    dialogs = [
        d(KEEF, "talk", "30534-00.htm", when="absent", races=[0, 1, 2, 3]),
        d(KEEF, "talk", "30534-01.htm", when="absent", races=[4], maxLevel=9),
        d(KEEF, "talk", "30534-02.htm", when="absent", races=[4], minLevel=10),
        d(
            KEEF,
            "event",
            "30534-03.htm",
            when="absent",
            event="30534-03",
            races=[4],
            minLevel=10,
            ops=START + [cond(1)],
        ),
        d(KEEF, "talk", "30534-04.html", cond=1),
        d(
            KEEF,
            "talk",
            "30534-06.html",
            cond=2,
            atLeast={FANG: 100},
            hasItem=RING,
            ops=[adena(2400), exp_sp(0, 600), {"op": "complete"}],
        ),
        d(
            KEEF,
            "talk",
            "30534-05.html",
            cond=2,
            atLeast={FANG: 100},
            lacksItems=[RING],
            ops=[give(RING), exp_sp(0, 600), {"op": "complete"}],
        ),
        d(20370, "kill", None, cond=1, raw="", ops=[drop(FANG, 1.0, limit=100, at=2, count=2)]),
        d(20480, "kill", None, cond=1, raw="", ops=[drop(FANG, 1.0, limit=100, at=2, count=2)]),
    ]
    return {
        "id": Q,
        "name": "Covert Business",
        "repeatable": True,
        "abortable": True,
        "questItems": [FANG],
        "dialogs": dialogs,
    }


def main() -> None:
    quests = [q291(), q292(), q293(), q294()]
    need = []
    for q in quests:
        need.extend(q.get("questItems") or [])
        for dlg in q["dialogs"]:
            if dlg.get("hasItem"):
                need.append(int(dlg["hasItem"]))
            for k in ("hasItems", "lacksItems", "hasAny"):
                need.extend(int(x) for x in dlg.get(k) or [])
            for o in dlg.get("ops") or []:
                if o.get("itemId"):
                    need.append(int(o["itemId"]))
                for p in o.get("prices") or []:
                    need.append(int(p["itemId"]))
        # rewards
        if q["id"] == 291:
            need.extend([736, 1505])
        if q["id"] == 294:
            need.append(1508)
    ensure_items(sorted(set(need)))
    questio.upsert_quests(quests)
    for q in quests:
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
