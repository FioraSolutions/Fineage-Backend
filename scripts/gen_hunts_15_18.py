#!/usr/bin/env python3
"""Hand-parity Mobius hunts Q276 / Q277 / Q296 / Q297 / Q324 (batch 15–18).

  Q276 Totem of the Hestui — Tanapi (Orc), Kasha crystal via totem spawn
  Q277 Gatekeeper's Offering — Tamil, starstone ×20
  Q296 Tarantula's Spider Silk — Mion/Nathan, silk sell + spinnerette convert
  Q297 Gatekeeper's Favor — Wirphy, starstone ×20
  Q324 Sweetest Venom — Astaron, venom sacs ×10

Simplifications:
  Q276: spawn totem when parasite stack ≥50 (Mobius tiered chances 39–79).
  Q296: Nathan converts spinnerette → 19 silk each (Mobius 15+rand(9)).

After: python3 scripts/gen_hunts_15_18.py
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
spawn = lambda n: {"op": "spawn_npc", "npcId": n}
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
    added = [
        mobius[i]
        if i in mobius
        else {"id": i, "name": f"Quest Item {i}", "kind": "etc", "icon": "icon.etc_question_mark_i00", "price": 0, "weight": 0}
        for i in missing
    ]
    data["items"].extend(added)
    data["items"].sort(key=lambda r: int(r["id"]))
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"items: added {[a['id'] for a in added]}")


def q276():
    Q, d = 276, html_loader("Q00276_TotemOfTheHestui", 276)
    TANAPI = 30571
    PARA, CRYSTAL = 1480, 1481
    BEAR, TOTEM = 20479, 27044
    PANTS, TOTEM_ITEM = 29, 1500
    dialogs = [
        d(TANAPI, "talk", "30571-00.htm", when="absent", races=[0, 1, 2, 4]),
        d(TANAPI, "talk", "30571-01.htm", when="absent", races=[3], maxLevel=14),
        d(TANAPI, "talk", "30571-02.htm", when="absent", races=[3], minLevel=15),
        d(
            TANAPI,
            "event",
            "30571-03.htm",
            when="absent",
            event="30571-03",
            races=[3],
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(TANAPI, "talk", "30571-04.html", cond=1),
        d(
            TANAPI,
            "talk",
            "30571-05.html",
            cond=2,
            hasItem=CRYSTAL,
            ops=[give(PANTS), give(TOTEM_ITEM), {"op": "complete"}],
        ),
        # ≥50 parasites → spawn totem (Mobius tiered 39–79).
        d(
            BEAR,
            "kill",
            None,
            cond=1,
            atLeast={PARA: 50},
            lacksItems=[CRYSTAL],
            raw="",
            ops=[spawn(TOTEM), take(PARA, 50)],
        ),
        d(BEAR, "kill", None, cond=1, lacksItems=[CRYSTAL], raw="", ops=[drop(PARA, 1.0)]),
        d(
            TOTEM,
            "kill",
            None,
            cond=1,
            lacksItems=[CRYSTAL],
            raw="",
            ops=[give(CRYSTAL), cond(2)],
        ),
    ]
    return {
        "id": Q,
        "name": "Totem of the Hestui",
        "repeatable": True,
        "abortable": True,
        "questItems": [PARA, CRYSTAL],
        "dialogs": dialogs,
    }


def q277():
    Q, d = 277, html_loader("Q00277_GatekeepersOffering", 277)
    TAMIL = 30576
    STONE, CHARM = 1572, 1658
    GOLEM = 20333
    dialogs = [
        d(TAMIL, "talk", "30576-02.htm", when="absent"),
        d(TAMIL, "event", "30576-01.htm", when="absent", event="30576-03", maxLevel=14),
        d(
            TAMIL,
            "event",
            "30576-03.htm",
            when="absent",
            event="30576-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(TAMIL, "talk", "30576-04.html", cond=1),
        d(
            TAMIL,
            "talk",
            "30576-05.html",
            cond=2,
            atLeast={STONE: 20},
            ops=[give(CHARM, 2), {"op": "complete"}],
        ),
        d(GOLEM, "kill", None, when="started", raw="", ops=[drop(STONE, 1.0, limit=20, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Gatekeeper's Offering",
        "repeatable": True,
        "abortable": True,
        "questItems": [STONE],
        "dialogs": dialogs,
    }


def q296():
    Q, d = 296, html_loader("Q00296_TarantulasSpiderSilk", 296)
    MION, NATHAN = 30519, 30548
    SILK, SPIN = 1493, 1494
    MOBS = (20394, 20403, 20508)
    dialogs = [
        d(MION, "talk", "30519-01.htm", when="absent", maxLevel=14),
        d(MION, "talk", "30519-02.htm", when="absent", minLevel=15),
        d(
            MION,
            "event",
            "30519-03.htm",
            when="absent",
            event="30519-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(MION, "event", "30519-06.html", when="started", event="30519-06", ops=[{"op": "complete"}]),
        d(MION, "event", "30519-07.html", when="started", event="30519-07"),
        d(
            MION,
            "talk",
            "30519-05.html",
            when="started",
            hasItem=SILK,
            ops=[
                {
                    "op": "sell_items",
                    "prices": [{"itemId": SILK, "adena": 30}],
                    "bonusMin": 10,
                    "bonusAdena": 2000,
                }
            ],
        ),
        d(MION, "talk", "30519-04.html", when="started", lacksItems=[SILK]),
        d(NATHAN, "talk", "30548-01.html", when="started"),
        d(
            NATHAN,
            "event",
            "30548-03.html",
            when="started",
            event="30548-03",
            hasItem=SPIN,
            # Fixed mid-roll: 19 silk per spinnerette (Mobius 15+rand(9)).
            # exchange_all: each spinnerette → 19 silk
            ops=[{"op": "exchange_all", "itemId": SPIN, "rewardItemId": SILK, "per": 19}],
        ),
        d(NATHAN, "event", "30548-02.html", when="started", event="30548-03", lacksItems=[SPIN]),
    ]
    for mob in MOBS:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                raw="",
                ops=[drop(SPIN, 0.05), drop(SILK, 0.5)],
            )
        )
    return {
        "id": Q,
        "name": "Tarantula's Spider Silk",
        "repeatable": True,
        "abortable": True,
        "questItems": [SILK, SPIN],
        "dialogs": dialogs,
    }


def q297():
    Q, d = 297, html_loader("Q00297_GatekeepersFavor", 297)
    WIRPHY = 30540
    STONE, TOKEN = 1573, 1659
    GOLEM = 20521
    dialogs = [
        d(WIRPHY, "talk", "30540-02.htm", when="absent"),
        d(WIRPHY, "event", "30540-01.htm", when="absent", event="30540-03", maxLevel=14),
        d(
            WIRPHY,
            "event",
            "30540-03.htm",
            when="absent",
            event="30540-03",
            minLevel=15,
            ops=START + [cond(1)],
        ),
        d(WIRPHY, "talk", "30540-04.html", cond=1),
        d(
            WIRPHY,
            "talk",
            "30540-05.html",
            cond=2,
            atLeast={STONE: 20},
            ops=[give(TOKEN, 2), {"op": "complete"}],
        ),
        d(GOLEM, "kill", None, when="started", raw="", ops=[drop(STONE, 1.0, limit=20, at=2)]),
    ]
    return {
        "id": Q,
        "name": "Gatekeeper's Favor",
        "repeatable": True,
        "abortable": True,
        "questItems": [STONE],
        "dialogs": dialogs,
    }


def q324():
    Q, d = 324, html_loader("Q00324_SweetestVenom", 324)
    ASTARON = 30351
    SAC = 1077
    chances = {20034: 0.26, 20038: 0.29, 20043: 0.30}
    dialogs = [
        d(ASTARON, "talk", "30351-02.html", when="absent", maxLevel=17),
        d(ASTARON, "talk", "30351-03.htm", when="absent", minLevel=18),
        d(
            ASTARON,
            "event",
            "30351-04.htm",
            when="absent",
            event="30351-04",
            minLevel=18,
            ops=START + [cond(1)],
        ),
        d(ASTARON, "talk", "30351-05.html", cond=1),
        d(
            ASTARON,
            "talk",
            "30351-06.html",
            cond=2,
            atLeast={SAC: 10},
            ops=[adena(5810), {"op": "complete"}],
        ),
    ]
    for mob, ch in chances.items():
        dialogs.append(d(mob, "kill", None, cond=1, raw="", ops=[drop(SAC, ch, limit=10, at=2)]))
    return {
        "id": Q,
        "name": "Sweetest Venom",
        "repeatable": True,
        "abortable": True,
        "questItems": [SAC],
        "dialogs": dialogs,
    }


def main() -> None:
    quests = [q276(), q277(), q296(), q297(), q324()]
    ensure_items([1480, 1481, 1500, 1572, 1573, 1658, 1659, 1077, 29, 1493, 1494])
    questio.upsert_quests(quests)
    for q in quests:
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")


if __name__ == "__main__":
    main()
