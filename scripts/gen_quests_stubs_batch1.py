#!/usr/bin/env python3
"""Stub / missing quest batch 1: Q255, Q431, Q637, Q374, Q375, Q377.

After: python3 scripts/gen_quests_stubs_batch1.py
       python3 scripts/gen_drop_items.py && python3 scripts/gen_quest_item_flags.py
       python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude
"""
from __future__ import annotations

import json
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


def drop(i, chance, limit=0, at=None, count=1, party=False):
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


def q255() -> dict | None:
    """Mobius Q00255 is client-timer driven; Fineage mirrors playable Q999 on id 255 for pack coverage."""
    import shutil

    src_dir = ROOT / "data/quests/00999_newbie_tutorial"
    if not (src_dir / "quest.json").is_file():
        raise SystemExit("00999_newbie_tutorial pack required before generating Q255")
    body = json.loads((src_dir / "quest.json").read_text(encoding="utf-8"))
    body["id"] = 255
    body["name"] = "Tutorial"
    questio.ensure_meta()
    for old in questio.QUESTS_DIR.glob("00255_*"):
        if old.is_dir():
            shutil.rmtree(old)
    dest = questio.quest_dir(255, body["name"])
    shutil.copytree(src_dir, dest, dirs_exist_ok=True)
    (dest / "quest.json").write_text(json.dumps(body, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return None


def q431() -> dict:
    Q = 431
    html, d = html_loader("Q00431_WeddingMarch", Q)
    KANTABILON = 31042
    SILVER, ECHO = 7540, 7062
    dialogs = [
        d(KANTABILON, "talk", "31042-00.htm", when="absent", maxLevel=37),
        d(KANTABILON, "talk", "31042-01.htm", when="absent", minLevel=38),
        d(KANTABILON, "event", "31042-02.htm", when="absent", event="31042-02", ops=START + [cond(1)]),
        d(
            KANTABILON,
            "talk",
            "31042-04.htm",
            atLeast={str(SILVER): 50},
            ops=[take_all(SILVER), give(ECHO, 25), DONE],
        ),
        d(KANTABILON, "talk", "31042-02.htm", cond=1),
        d(KANTABILON, "talk", "31042-03.htm", cond=2, atLeast={str(SILVER): 1}, below={str(SILVER): 50}),
        d(
            KANTABILON,
            "event",
            "31042-05.htm",
            event="31042-05",
            atLeast={str(SILVER): 50},
            ops=[take_all(SILVER), give(ECHO, 25), DONE],
        ),
        d(KANTABILON, "event", "31042-03.htm", event="31042-05", below={str(SILVER): 50}),
    ]
    for mob in (20786, 20787):
        dialogs.append(
            d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(SILVER, 0.5, limit=50, at=2)])
        )
    return {
        "id": Q,
        "name": "Wedding March",
        "repeatable": True,
        "abortable": True,
        "questItems": [SILVER],
        "dialogs": dialogs,
    }


def q637() -> dict:
    Q = 637
    html, d = html_loader("Q00637_ThroughOnceMore", Q)
    FLAURON = 32010
    FADED, HEART, PAGAN = 8065, 8066, 8067
    dialogs = [
        d(FLAURON, "talk", "32010-00.htm", when="absent", hasItem=PAGAN),
        d(FLAURON, "talk", "32010-01a.htm", when="absent", lacksItems=[FADED], minLevel=73),
        d(FLAURON, "talk", "32010-01a.htm", when="absent", maxLevel=72, hasItem=FADED),
        d(FLAURON, "talk", "32010-01.htm", when="absent", minLevel=73, hasItem=FADED, lacksItems=[PAGAN]),
        d(FLAURON, "event", "32010-04.htm", event="32010-04", when="absent", minLevel=73, hasItem=FADED, ops=START + [cond(1)]),
        d(FLAURON, "event", "32010-10.htm", event="32010-10", when="started", ops=[DONE]),
        d(
            FLAURON,
            "talk",
            "32010-06.htm",
            atLeast={str(HEART): 10},
            ops=[take(FADED), take_all(HEART), give(PAGAN), give(8273, 10), DONE],
        ),
        d(FLAURON, "talk", "32010-05.htm", cond=1),
    ]
    for mob in (21565, 21566, 21567):
        dialogs.append(
            d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(HEART, 0.4, limit=10, at=2)])
        )
    return {
        "id": Q,
        "name": "Through the Gate Once More",
        "repeatable": True,
        "abortable": True,
        "questItems": [HEART],
        "dialogs": dialogs,
    }


def q374() -> dict:
    Q = 374
    html, d = html_loader("Q00374_WhisperOfDreamsPart1", Q)
    MANAKIA, TORAI = 30515, 30557
    TOOTH, LIGHT, SEALED, STONE = 5884, 5885, 5886, 5887
    rewards = [
        (5486, 3, 2950),
        (5487, 2, 18050),
        (5488, 2, 18050),
        (5485, 4, 10450),
        (5489, 6, 15550),
    ]
    dialogs = [
        d(MANAKIA, "talk", "30515-01.htm", when="absent", maxLevel=55),
        d(MANAKIA, "talk", "30515-02.htm", when="absent", minLevel=56),
        d(MANAKIA, "event", "30515-03.htm", when="absent", minLevel=56, event="30515-03", ops=START + [cond(1), {"op": "set", "key": "condStone", "value": "1"}]),
        d(MANAKIA, "talk", "30515-04.htm", cond=1, lacksItems=[SEALED]),
        d(MANAKIA, "talk", "30515-05.htm", cond=1, atLeast={str(TOOTH): 65, str(LIGHT): 65}, lacksItems=[SEALED]),
        d(MANAKIA, "talk", "30515-09.htm", cond=1, hasItem=SEALED, ops=[cond(2)]),
        d(MANAKIA, "talk", "30515-10.htm", cond=2, hasItem=SEALED),
        d(MANAKIA, "event", "30515-08.htm", event="30515-08", when="started", ops=[DONE]),
        d(TORAI, "talk", "30557-01.htm", cond=2, hasItem=SEALED),
        d(
            TORAI,
            "event",
            "30557-02.htm",
            event="30557-02",
            cond=2,
            hasItem=SEALED,
            ops=[cond(3), take_all(SEALED), give(STONE)],
        ),
        d(TORAI, "event", "30557-03.htm", event="30557-02", cond=2, lacksItems=[SEALED]),
    ]
    for idx, (item, count, adena) in enumerate(rewards):
        ev = f"30515-06-{idx}"
        dialogs.append(
            d(
                MANAKIA,
                "event",
                "30515-06.htm",
                event=ev,
                atLeast={str(TOOTH): 65, str(LIGHT): 65},
                ops=[take_all(TOOTH, LIGHT), give(item, count), {"op": "give_adena", "count": adena}],
            )
        )
        dialogs.append(d(MANAKIA, "event", "30515-07.htm", event=ev, when="started"))
    for mob, item in ((20620, TOOTH), (20621, LIGHT)):
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(item, 0.5)]))
    dialogs.append(
        d(
            20620,
            "kill",
            None,
            cond=1,
            party=True,
            varAtLeast={"condStone": 1},
            raw="",
            ops=[drop(SEALED, 0.01, limit=1), {"op": "unset", "key": "condStone"}],
        )
    )
    dialogs.append(
        d(
            20621,
            "kill",
            None,
            cond=1,
            party=True,
            varAtLeast={"condStone": 1},
            raw="",
            ops=[drop(SEALED, 0.01, limit=1), {"op": "unset", "key": "condStone"}],
        )
    )
    return {
        "id": Q,
        "name": "Whisper of Dreams, Part 1",
        "repeatable": True,
        "abortable": True,
        "questItems": [TOOTH, LIGHT, SEALED, STONE],
        "dialogs": dialogs,
    }


def q375() -> dict:
    Q = 375
    html, d = html_loader("Q00375_WhisperOfDreamsPart2", Q)
    MANAKIA = 30515
    STONE, HORN, SKULL = 5887, 5888, 5889
    RECIPES = [5348, 5350, 5352]
    turn_in = [
        take(HORN, 100),
        take(SKULL, 100),
        {"op": "set", "key": "rew", "value": "5348 5350 5352"},
        {"op": "pick_random", "key": "rew", "name": "pick"},
        give(RECIPES[0]),
    ]
    dialogs = [
        d(MANAKIA, "talk", "30515-01.htm", when="absent", lacksItems=[STONE]),
        d(MANAKIA, "talk", "30515-01.htm", when="absent", maxLevel=59, hasItem=STONE),
        d(MANAKIA, "talk", "30515-02.htm", when="absent", minLevel=60, hasItem=STONE),
        d(MANAKIA, "event", "30515-03.htm", when="absent", minLevel=60, hasItem=STONE, event="30515-03", ops=START + [take(STONE), cond(1)]),
        d(MANAKIA, "talk", "30515-04.htm", cond=1, below={str(HORN): 100}),
        d(MANAKIA, "talk", "30515-05.htm", cond=1, atLeast={str(HORN): 100, str(SKULL): 100}, ops=turn_in),
        d(MANAKIA, "event", "30515-07.htm", event="30515-07", when="started", ops=[DONE]),
    ]
    dialogs.append(d(20629, "kill", None, cond=1, party=True, raw="", ops=[drop(HORN, 1.0)]))
    dialogs.append(d(20624, "kill", None, cond=1, party=True, raw="", ops=[drop(SKULL, 0.9)]))
    return {
        "id": Q,
        "name": "Whisper of Dreams, Part 2",
        "repeatable": True,
        "abortable": True,
        "questItems": [HORN, SKULL],
        "dialogs": dialogs,
    }


def q377() -> dict:
    Q = 377
    html, d = html_loader("Q00377_ExplorationOfTheGiantsCavePart2", Q)
    SOBLING = 31147
    DICT = 5892
    ANCIENT = 5955
    set_a = [5945, 5946, 5947, 5948, 5949]
    set_b = [5950, 5951, 5952, 5953, 5954]
    recip_a = [5338, 5336]
    recip_b = [5420, 5422]

    def take_set(items: list[int]):
        return [take(i) for i in items]

    dialogs = [
        d(SOBLING, "talk", "31147-01.htm", when="absent", lacksItems=[DICT]),
        d(SOBLING, "talk", "31147-01.htm", when="absent", maxLevel=56, hasItem=DICT),
        d(SOBLING, "talk", "31147-02.htm", when="absent", minLevel=57, hasItem=DICT),
        d(SOBLING, "event", "31147-03.htm", when="absent", minLevel=57, hasItem=DICT, event="31147-03", ops=START + [cond(1)]),
        d(SOBLING, "talk", "31147-05.htm", cond=1),
        d(
            SOBLING,
            "talk",
            "31147-04.htm",
            cond=1,
            hasItems=set_a,
            ops=take_set(set_a) + [give(recip_a[0])],
        ),
        d(
            SOBLING,
            "talk",
            "31147-04.htm",
            cond=1,
            hasItems=set_b,
            ops=take_set(set_b) + [give(recip_b[0])],
        ),
        d(SOBLING, "event", "31147-07.htm", event="31147-07", when="started", ops=[DONE]),
    ]
    for mob in (20654, 20656, 20657, 20658):
        dialogs.append(d(mob, "kill", None, cond=1, party=True, raw="", ops=[drop(ANCIENT, 0.8)]))
    return {
        "id": Q,
        "name": "Exploration of the Giants Cave, Part 2",
        "repeatable": True,
        "abortable": True,
        "questItems": [ANCIENT] + set_a + set_b,
        "dialogs": dialogs,
    }


def main() -> None:
    questio.ensure_meta(reference="Mobius CT_0 batch1 via scripts/gen_quests_stubs_batch1.py")
    q255()
    print("upserted 255 Tutorial (clone of Q999 pack)")
    for q in (q431(), q637(), q374(), q375(), q377()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")
    questio.ensure_meta(reference="Mobius CT_0 batch1 via scripts/gen_quests_stubs_batch1.py")


if __name__ == "__main__":
    main()