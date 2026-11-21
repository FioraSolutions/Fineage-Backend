#!/usr/bin/env python3
"""Mobius gap rank 2 — missing quest packs Q385, Q426, Q505, Q646.

Transcribes CT0 Interlude Java + HTML from .cache/l2j-mobius (not copied verbatim).
Q634/Q635 remain blocked until dimensional rift instances land.

After: python3 scripts/gen_quests_missing_rank2.py
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio

QROOT = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/scripts/quests"
if not QROOT.is_dir():
    raise SystemExit(f"Mobius quests not found under {QROOT}; run scripts/fetch-mobius-interlude.sh")

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


def tel(x: int, y: int, z: int):
    return {"op": "teleport", "x": x, "y": y, "z": z}


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
        if event is not None:
            row["event"] = event
        row.update(gates)
        if raw is not None:
            row["html"] = raw
        elif page is not None:
            row["html"] = html(page)
        else:
            row["html"] = ""
        if ops:
            row["ops"] = ops
        return row

    return d


def parse_java_put_map(text: str, map_name: str) -> dict[int, int]:
    pattern = re.compile(rf"{re.escape(map_name)}\.put\((\d+),\s*(\d+)\)")
    return {int(a): int(b) for a, b in pattern.findall(text)}


def parse_java_special(java_path: pathlib.Path) -> dict[int, tuple[int, int]]:
    text = java_path.read_text(encoding="utf-8")
    out: dict[int, tuple[int, int]] = {}
    for npc, a, b in re.findall(
        r"MOB_SPECIAL\.put\((\d+),\s*new int\[\]\{(\d+),\s*(\d+)\}\)", text
    ):
        out[int(npc)] = (int(a), int(b))
    return out


def replicate_gatekeeper(dialogs: list, d, npc_ids: list[int], **kwargs):
    """Same dialog row for many start NPCs (Yoke of the Past ziggurats)."""
    for npc in npc_ids:
        row = dict(kwargs)
        row["npcId"] = npc
        dialogs.append(row)


def q385():
    script = "Q00385_YokeOfThePast"
    d = html_loader(script, 385)
    java = QROOT / script / f"{script}.java"
    text = java.read_text(encoding="utf-8")
    mob_chances = parse_java_put_map(text, "CHANCES")
    gk_ids = [
        31095, 31096, 31097, 31098, 31099, 31100, 31101, 31102, 31103, 31104,
        31105, 31106, 31107, 31108, 31109, 31110, 31114, 31115, 31116, 31117,
        31118, 31119, 31120, 31121, 31122, 31123, 31124, 31125, 31126,
    ]
    ANCIENT, BLANK = 5902, 5965
    dialogs: list = []
    for gk in gk_ids:
        dialogs.append(d(gk, "talk", "02.htm", when="absent", maxLevel=19))
        dialogs.append(d(gk, "talk", "01.htm", when="absent", minLevel=20))
        dialogs.append(
            d(gk, "event", "05.htm", when="absent", event="05", minLevel=20, ops=START)
        )
        dialogs.append(d(gk, "talk", "08.htm", when="started", lacksItems=[ANCIENT]))
        dialogs.append(
            d(
                gk,
                "talk",
                "09.htm",
                when="started",
                hasAny=[ANCIENT],
                ops=[
                    {
                        "op": "exchange_all",
                        "itemId": ANCIENT,
                        "rewardItemId": BLANK,
                        "per": 1,
                    }
                ],
            )
        )
        dialogs.append(
            d(gk, "event", "10.htm", when="started", event="10", ops=[{"op": "complete"}])
        )
    for mob, ppm in mob_chances.items():
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                party=True,
                ops=[drop(ANCIENT, ppm / 1_000_000, count=1)],
            )
        )
    return {
        "id": 385,
        "name": "Yoke of the Past",
        "repeatable": True,
        "abortable": True,
        "questItems": [ANCIENT],
        "dialogs": dialogs,
    }


def q426():
    script = "Q00426_QuestForFishingShot"
    d = html_loader(script, 426)
    java = QROOT / script / f"{script}.java"
    text = java.read_text(encoding="utf-8")
    FLUID = 7586
    fishers = [
        31562, 31563, 31564, 31565, 31566, 31567, 31568, 31569, 31570, 31571,
        31572, 31573, 31574, 31575, 31576, 31577, 31578, 31579, 31696, 31697,
        31989, 32007,
    ]
    dialogs: list = []
    for npc in fishers:
        dialogs.append(d(npc, "talk", "01.htm", when="absent"))
        dialogs.append(
            d(npc, "event", "03.htm", when="absent", event="03", ops=START)
        )
        dialogs.append(d(npc, "talk", "04.htm", when="started", lacksItems=[FLUID]))
        dialogs.append(d(npc, "talk", "05.htm", when="started", hasAny=[FLUID]))
        dialogs.append(
            d(npc, "event", "08.htm", when="started", event="08", ops=[{"op": "complete"}])
        )

    def add_map(map_name: str, base_drop: int):
        for npc, thousand in re.findall(
            rf"{map_name}\.put\((\d+),\s*(\d+)\)", text
        ):
            count = max(1, base_drop + 1) if base_drop else 1
            dialogs.append(
                d(
                    int(npc),
                    "kill",
                    None,
                    when="started",
                    party=True,
                    ops=[drop(FLUID, int(thousand) / 1000, count=count)],
                )
            )

    add_map("MOBS1", 0)
    add_map("MOBS2", 1)
    add_map("MOBS3", 2)
    add_map("MOBS4", 3)
    add_map("MOBS5", 4)
    for npc, (thousand, count) in parse_java_special(java).items():
        dialogs.append(
            d(
                npc,
                "kill",
                None,
                when="started",
                party=True,
                ops=[drop(FLUID, thousand / 1000, count=count)],
            )
        )
    return {
        "id": 426,
        "name": "Quest for Fishing Shot",
        "repeatable": True,
        "abortable": True,
        "questItems": [FLUID],
        "dialogs": dialogs,
    }


def q646():
    Q, d = 646, html_loader("Q00646_SignsOfRevolt", 646)
    TORRANT = 32016
    DOLL = 8087
    NEED = 180
    rewards = {
        "0": (1880, 9),
        "1": (1881, 12),
        "2": (1882, 20),
        "3": (57, 21600),
    }
    mobs = [
        22029, 22030, 22031, 22032, 22033, 22034, 22035, 22036, 22037, 22038,
        22039, 22040, 22041, 22042, 22043, 22044, 22045, 22047, 22049,
    ]
    dialogs = [
        d(TORRANT, "talk", "32016-02.htm", when="absent", maxLevel=39),
        d(TORRANT, "talk", "32016-01.htm", when="absent", minLevel=40),
        d(
            TORRANT,
            "event",
            "32016-03.htm",
            when="absent",
            event="32016-03",
            minLevel=40,
            ops=START + [cond(1)],
        ),
        d(TORRANT, "talk", "32016-04.htm", when="started", cond=1),
        d(TORRANT, "talk", "32016-05.htm", when="started", cond=2),
    ]
    for key, (item, cnt) in rewards.items():
        dialogs.append(
            d(
                TORRANT,
                "event",
                "32016-07.htm",
                when="started",
                event=key,
                cond=2,
                atLeast={DOLL: NEED},
                ops=(
                    [take(DOLL, NEED), give(item, cnt), {"op": "complete"}]
                    if item != 57
                    else [take(DOLL, NEED), adena(cnt), {"op": "complete"}]
                ),
            )
        )
    for mob in mobs:
        dialogs.append(
            d(
                mob,
                "kill",
                None,
                when="started",
                cond=1,
                party=True,
                ops=[drop(DOLL, 0.75, limit=NEED, at=2)],
            )
        )
    return {
        "id": Q,
        "name": "Signs of Revolt",
        "repeatable": True,
        "abortable": True,
        "questItems": [DOLL],
        "dialogs": dialogs,
    }


def q505():
    """Blood Offering — festival / rift teleports (cabal gates TODO when Q635 ships)."""
    d = html_loader("Q00505_BloodOffering", 505)
    FESTIVAL_GUIDE = [31127, 31128, 31129, 31130, 31131, 31137, 31138, 31139, 31140, 31141]
    FESTIVAL_WITCH = {
        31132: (-80204, 87056, -5154),
        31133: (-77198, 87678, -5182),
        31134: (-76183, 87135, -5179),
        31135: (-76945, 86602, -5153),
        31136: (-79970, 85997, -5154),
        31142: (-79182, 111893, -4898),
        31143: (-76176, 112505, -4899),
        31144: (-75198, 111969, -4898),
        31145: (-75920, 111435, -4900),
        31146: (-78928, 110825, -4926),
    }
    RIFT_POST = [31488, 31489, 31490, 31491, 31492, 31493]
    dialogs: list = []
    for npc in FESTIVAL_GUIDE:
        dialogs.append(
            d(
                npc,
                "talk",
                "guide.htm",
                when="absent",
                ops=[tel(-114796, -179334, -6752), {"op": "complete"}],
            )
        )
    for npc, (x, y, z) in FESTIVAL_WITCH.items():
        dialogs.append(d(npc, "talk", "witch.htm", ops=[tel(x, y, z)]))
    for npc in RIFT_POST:
        dialogs.append(
            d(
                npc,
                "talk",
                "riftpost-1.htm",
                ops=START + [cond(1), tel(-80157, 111344, -4901)],
            )
        )
        dialogs.append(d(npc, "talk", "riftpost-2.htm"))
    return {
        "id": 505,
        "name": "Blood Offering",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def main() -> int:
    quests = [q385(), q426(), q505(), q646()]
    for q in quests:
        folder = questio.upsert_quest(q)
        print(f"Q{q['id']:03d} -> {folder.name} ({len(q['dialogs'])} dialogs)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())