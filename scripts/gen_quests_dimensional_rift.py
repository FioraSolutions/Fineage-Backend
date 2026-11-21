#!/usr/bin/env python3
"""Port Q634 / Q635 (Mobius dimensional rift quests). After: python3 scripts/gen_quests_dimensional_rift.py"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/scripts/quests"
FRAGMENT = 7079
GATES = list(range(31494, 31508))
VORTEX = list(range(31488, 31507))


def html_loader(folder: str, qid: int):
    src = QROOT / folder

    def rewrite(text: str) -> str:
        text = re.sub(
            rf"bypass(?: -h)? Script {re.escape(folder)} ([A-Za-z0-9_\-\.]+)(?:\.html?)?",
            lambda m: f"quest_event:{qid}:{re.sub(r'\.html?$', '', m.group(1), flags=re.I)}",
            text,
        )
        return text

    def html(name: str) -> str:
        for ext in ("", ".htm", ".html"):
            p = src / f"{name}{ext}" if not re.search(r"\.html?$", name, re.I) else src / name
            if p.exists():
                return rewrite(p.read_text(encoding="utf-8").strip())
        raise SystemExit(f"{folder}: missing {name}")

    return html


def main() -> None:
    h634 = html_loader("Q00634_InSearchOfFragmentsOfDimension", 634)
    h635 = html_loader("Q00635_IntoTheDimensionalRift", 635)

    kills = list(range(21208, 21256))
    q634 = {
        "id": 634,
        "name": "In Search of Fragments of Dimension",
        "repeatable": True,
        "questItems": [FRAGMENT],
        "dialogs": [],
    }
    for npc in GATES:
        q634["dialogs"].extend(
            [
                {"npcId": npc, "hook": "talk", "when": "absent", "minLevel": 20, "html": h634("01")},
                {"npcId": npc, "hook": "talk", "when": "absent", "maxLevel": 19, "html": h634("01a")},
                {"npcId": npc, "hook": "talk", "when": "started", "html": h634("03")},
                {"npcId": npc, "hook": "event", "event": "02", "when": "created", "html": h634("02"), "ops": [{"op": "start"}]},
                {"npcId": npc, "hook": "event", "event": "05", "when": "started", "html": h634("05"), "ops": [{"op": "complete"}]},
            ]
        )
    for npc in kills:
        q634["dialogs"].append(
            {
                "npcId": npc,
                "hook": "kill",
                "when": "started",
                "party": True,
                "chance": 0.08,
                "ops": [{"op": "drop_item", "itemId": FRAGMENT, "count": 1, "chance": 1.0, "limit": 0}],
            }
        )

    coords = [
        (-41572, 209731, -5087),
        (-52872, -250283, -7908),
        (45256, 123906, -5411),
        (46192, 170290, -4981),
        (111273, 174015, -5437),
        (-20604, -250789, -8165),
        (-21726, 77385, -5171),
        (140405, 79679, -5427),
        (-52366, 79097, -4741),
        (118311, 132797, -4829),
        (172185, -17602, -4901),
        (83000, 209213, -5439),
        (-19500, 13508, -4901),
        (113865, 84543, -6541),
    ]
    q635 = {
        "id": 635,
        "name": "In the Dimensional Rift",
        "repeatable": True,
        "dialogs": [],
    }
    for npc in GATES:
        gate_id = npc - 31493
        q635["dialogs"].extend(
            [
                {
                    "npcId": npc,
                    "hook": "talk",
                    "when": "any",
                    "minLevel": 20,
                    "maxStarted": 23,
                    "lacksItems": [FRAGMENT],
                    "html": h635("3"),
                },
                {
                    "npcId": npc,
                    "hook": "talk",
                    "when": "any",
                    "minLevel": 20,
                    "maxStarted": 23,
                    "hasItems": [FRAGMENT],
                    "ops": [{"op": "create"}, {"op": "set", "key": "id", "value": str(gate_id)}],
                    "html": h635("4"),
                },
                {
                    "npcId": npc,
                    "hook": "talk",
                    "when": "any",
                    "maxLevel": 19,
                    "html": h635("1"),
                    "ops": [{"op": "complete"}],
                },
                {
                    "npcId": npc,
                    "hook": "talk",
                    "when": "any",
                    "maxStarted": 23,
                    "whenVar": "overflow",
                    "whenValue": "1",
                    "html": h635("1a"),
                    "ops": [{"op": "complete"}],
                },
            ]
        )
    for idx, (x, y, z) in enumerate(coords, start=1):
        npc = 31493 + idx
        if npc > 31507:
            break
        q635["dialogs"].append(
            {
                "npcId": npc,
                "hook": "talk",
                "when": "started",
                "inRift": True,
                "whenVar": "id",
                "whenValue": str(idx),
                "html": h635("7"),
                "ops": [{"op": "teleport", "x": x, "y": y, "z": z}, {"op": "complete"}],
            }
        )
    q635["dialogs"].append(
        {
            "npcId": 31494,
            "hook": "event",
            "event": "TELEPORT",
            "when": "started",
            "inRiftPeace": True,
            "varAtLeast": {"count": 1},
            "html": h635("5a"),
            "ops": [
                {"op": "add", "key": "count", "value": "1"},
                {"op": "set_cond", "cond": 1},
                {"op": "teleport", "x": -114790, "y": -180576, "z": -6752},
            ],
        }
    )
    q635["dialogs"].append(
        {
            "npcId": 31494,
            "hook": "event",
            "event": "TELEPORT",
            "when": "started",
            "inRiftPeace": True,
            "whenVar": "id",
            "whenValue": "0",
            "html": h635("6"),
            "ops": [{"op": "complete"}],
        }
    )
    q635["dialogs"].append(
        {
            "npcId": 31494,
            "hook": "event",
            "event": "6",
            "when": "started",
            "html": h635("6"),
            "ops": [{"op": "complete"}],
        }
    )

    questio.ensure_meta(
        reference="Mobius CT_0 Interlude Q634/Q635 via scripts/gen_quests_dimensional_rift.py"
    )
    questio.upsert_quests([q634, q635])
    print("Wrote quests 634 and 635")


if __name__ == "__main__":
    main()