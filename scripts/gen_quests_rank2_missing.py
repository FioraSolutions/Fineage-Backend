#!/usr/bin/env python3
"""Mobius gap audit rank-2 batch: Q385 / Q426 / Q505 / Q646.

  Q385 Yoke of the Past — gatekeeper ziggurats, ancient scroll → blank scroll exchange
  Q426 Quest for Fishing Shot — guild members, per-mob sweet-fluid drops
  Q505 Blood Offering — festival guides / witches / rift posts (Seven Signs cabal gates N/A in JSON;
      rift posts use dawn-inner teleport as competition-period stand-in; seal-validation → riftpost-2)
  Q646 Signs of Revolt — Torrant, cursed dolls

Skip: Q255 (Fineage Q998), Q634/Q635 (dimensional rift instances).

After: python3 scripts/gen_quests_rank2_missing.py
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
    QROOT = ROOT / "ref/mobius-ct0-interlude/dist/game/data/scripts/quests"

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


def tp(x, y, z):
    return {"op": "teleport", "x": x, "y": y, "z": z}


def html_loader(script: str, qid: int):
    src = QROOT / script

    def _ev(raw: str) -> str:
        return re.sub(r"\.html?$", "", raw, flags=re.I)

    def rewrite(text: str) -> str:
        text = re.sub(
            rf"bypass -h Quest {re.escape(script)} ([A-Za-z0-9_\-]+)(?:\.html?)?",
            lambda m: f"quest_event:{qid}:{_ev(m.group(1))}",
            text,
        )
        text = re.sub(
            rf"bypass(?: -h)? Script {re.escape(script)} ([A-Za-z0-9_\-\.]+)(?:\.html?)?",
            lambda m: f"quest_event:{qid}:{_ev(m.group(1))}",
            text,
        )
        return text

    def html(name: str) -> str:
        candidates = [src / name] if re.search(r"\.html?$", name, re.I) else [
            src / f"{name}{ext}" for ext in ("", ".htm", ".html")
        ]
        for p in candidates:
            if p.exists():
                return rewrite(p.read_text(encoding="utf-8").strip())
        raise SystemExit(f"{script}: missing html {name}")

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


def expand_npcs(npc_ids: list[int], maker):
    out = []
    for nid in npc_ids:
        out.extend(maker(nid))
    return out


def parse_java_int_map(java_path: pathlib.Path, map_name: str) -> dict[int, int]:
    text = java_path.read_text(encoding="utf-8")
    rows = re.findall(rf"{re.escape(map_name)}\.put\((\d+),\s*(\d+)\)", text)
    return {int(a): int(b) for a, b in rows}


def parse_java_special(java_path: pathlib.Path) -> dict[int, tuple[int, int]]:
    text = java_path.read_text(encoding="utf-8")
    rows = re.findall(r"MOB_SPECIAL\.put\((\d+),\s*new int\[\]\{(\d+),\s*(\d+)\}\)", text)
    return {int(a): (int(b), int(c)) for a, b, c in rows}


GATEKEEPER_ZIGGURAT = [
    31095, 31096, 31097, 31098, 31099, 31100, 31101, 31102, 31103, 31104, 31105, 31106, 31107, 31108, 31109,
    31110, 31114, 31115, 31116, 31117, 31118, 31119, 31120, 31121, 31122, 31123, 31124, 31125, 31126,
]
ANCIENT_SCROLL = 5902
BLANK_SCROLL = 5965


def q385():
    Q = 385
    html, d = html_loader("Q00385_YokeOfThePast", Q)
    java = QROOT / "Q00385_YokeOfThePast/Q00385_YokeOfThePast.java"
    chances = parse_java_int_map(java, "CHANCES")

    def gatekeeper(nid: int):
        return [
            d(nid, "talk", "02.htm", when="absent", maxLevel=19),
            d(nid, "talk", "01.htm", when="absent", minLevel=20),
            d(nid, "event", "03.htm", when="absent", event="03", minLevel=20),
            d(nid, "event", "04.htm", when="absent", event="04", minLevel=20),
            d(nid, "event", "05.htm", when="absent", event="05", minLevel=20, ops=START),
            d(nid, "event", "06.htm", when="absent", event="06"),
            d(nid, "event", "07.htm", when="absent", event="07"),
            d(nid, "event", "05.htm", when="started", event="05", ops=START),
            d(nid, "event", "06.htm", when="started", event="06"),
            d(nid, "event", "07.htm", when="started", event="07"),
            d(nid, "event", "11.htm", when="started", event="11"),
            d(nid, "event", "10.htm", when="started", event="10", ops=[{"op": "complete"}]),
            d(nid, "talk", "08.htm", when="started", lacksItems=[ANCIENT_SCROLL]),
            d(
                nid,
                "talk",
                "09.htm",
                when="started",
                hasItem=ANCIENT_SCROLL,
                ops=[{"op": "exchange_all", "itemId": ANCIENT_SCROLL, "rewardItemId": BLANK_SCROLL, "per": 1}],
            ),
        ]

    dialogs = expand_npcs(GATEKEEPER_ZIGGURAT, gatekeeper)
    for mob, pct in sorted(chances.items()):
        dialogs.append(
            d(mob, "kill", None, when="started", party=True, raw="", ops=[drop(ANCIENT_SCROLL, pct / 1_000_000, count=1)])
        )

    return {
        "id": Q,
        "name": "Yoke of the Past",
        "repeatable": True,
        "abortable": True,
        "questItems": [ANCIENT_SCROLL],
        "dialogs": dialogs,
    }


FISHERMEN = [
    31562, 31563, 31564, 31565, 31566, 31567, 31568, 31569, 31570, 31571, 31572, 31573, 31574, 31575, 31576, 31577,
    31578, 31579, 31696, 31697, 31989, 32007,
]
SWEET_FLUID = 7586


def fishing_kill_ops(mob: int, maps, special) -> list[dict]:
    if mob in special:
        chance, base = special[mob]
        return [drop(SWEET_FLUID, 1.0, count=base), drop(SWEET_FLUID, chance / 1000.0, count=1)]
    for idx, mp in enumerate(maps):
        if mob in mp:
            chance = mp[mob]
            base = idx  # MOBS1→0, MOBS2→1, …
            ops = []
            if base > 0:
                ops.append(drop(SWEET_FLUID, 1.0, count=base))
            ops.append(drop(SWEET_FLUID, chance / 1000.0, count=1))
            return ops
    return []


def q426():
    Q = 426
    html, d = html_loader("Q00426_QuestForFishingShot", Q)
    java = QROOT / "Q00426_QuestForFishingShot/Q00426_QuestForFishingShot.java"
    maps = [parse_java_int_map(java, f"MOBS{i}") for i in range(1, 6)]
    special = parse_java_special(java)

    def fisher(nid: int):
        return [
            d(nid, "talk", "01.htm", when="absent"),
            d(nid, "event", "03.htm", when="absent", event="03", ops=START),
            d(nid, "event", "08.htm", when="started", event="08", ops=[{"op": "complete"}]),
            d(nid, "talk", "05.htm", when="started", hasItem=SWEET_FLUID),
            d(nid, "talk", "04.htm", when="started", lacksItems=[SWEET_FLUID]),
        ]

    dialogs = expand_npcs(FISHERMEN, fisher)
    all_mobs = set()
    for mp in maps:
        all_mobs.update(mp)
    all_mobs.update(special)
    for mob in sorted(all_mobs):
        ops = fishing_kill_ops(mob, maps, special)
        if ops:
            dialogs.append(d(mob, "kill", None, when="started", party=True, raw="", ops=ops))

    return {
        "id": Q,
        "name": "Quest for Fishing Shot",
        "repeatable": True,
        "abortable": True,
        "questItems": [SWEET_FLUID],
        "dialogs": dialogs,
    }


FESTIVAL_GUIDE = [31127, 31128, 31129, 31130, 31131, 31137, 31138, 31139, 31140, 31141]
FESTIVAL_WITCH_LOCS = {
    31132: (-80204, 87056, -5154),
    31133: (-77198, 87678, -5182),
    31134: (-76183, 87135, -5179),
    31135: (-76945, 86602, 5153),
    31136: (-79970, 85997, -5154),
    31142: (-79182, 111893, -4898),
    31143: (-76176, 112505, -4899),
    31144: (-75198, 111969, -4898),
    31145: (-75920, 111435, -4900),
    31146: (-78928, 110825, -4926),
}
RIFT_POST = [31488, 31489, 31490, 31491, 31492, 31493]


def q505():
    Q = 505
    _, d = html_loader("Q00505_BloodOffering", Q)
    dialogs = []
    for nid in FESTIVAL_GUIDE:
        dialogs.append(
            d(nid, "talk", "guide.htm", when="absent", ops=[tp(-114796, -179334, -6752)])
        )
    for nid, (x, y, z) in FESTIVAL_WITCH_LOCS.items():
        dialogs.append(d(nid, "talk", "witch.htm", when="absent", ops=[tp(x, y, z)]))
    for nid in RIFT_POST:
        # Competition period inner rift (dawn cabal teleport); seal-validation / dusk pending Seven Signs quest gates.
        dialogs.append(
            d(
                nid,
                "talk",
                "riftpost-1.htm",
                when="absent",
                ops=START + [cond(1), tp(-80157, 111344, -4901)],
            )
        )
    return {
        "id": Q,
        "name": "Blood Offering",
        "repeatable": True,
        "abortable": True,
        "questItems": [],
        "dialogs": dialogs,
    }


def q646():
    Q = 646
    _, d = html_loader("Q00646_SignsOfRevolt", Q)
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
        22029, 22030, 22031, 22032, 22033, 22034, 22035, 22036, 22037, 22038, 22039, 22040, 22041, 22042, 22043, 22044,
        22045, 22047, 22049,
    ]
    dialogs = [
        d(TORRANT, "talk", "32016-02.htm", when="absent", maxLevel=39),
        d(TORRANT, "talk", "32016-01.htm", when="absent", minLevel=40),
        d(TORRANT, "event", "32016-03.htm", when="absent", event="32016-03", minLevel=40, ops=START + [cond(1)]),
        d(TORRANT, "talk", "32016-04.htm", when="started", cond=1),
        d(TORRANT, "talk", "32016-05.htm", when="started", cond=2, atLeast={str(DOLL): NEED}),
        d(TORRANT, "talk", "32016-04.htm", when="started"),
        d(
            TORRANT,
            "event",
            "32016-06.htm",
            when="started",
            event="32016-06",
            cond=2,
            atLeast={str(DOLL): NEED},
        ),
    ]
    for ev, (item, count) in rewards.items():
        reward_ops = [take(DOLL, NEED), {"op": "complete"}]
        if item == 57:
            reward_ops.insert(1, {"op": "give_adena", "count": count})
        else:
            reward_ops.insert(1, give(item, count))
        dialogs.append(
            d(
                TORRANT,
                "event",
                "32016-07.htm",
                event=ev,
                cond=2,
                atLeast={str(DOLL): NEED},
                ops=reward_ops,
            )
        )
        dialogs.append(d(TORRANT, "event", "32016-04.htm", event=ev, when="started"))
    for mob in mobs:
        dialogs.append(
            d(mob, "kill", None, when="started", cond=1, party=True, raw="", ops=[drop(DOLL, 0.75, limit=NEED, at=2)])
        )
    return {
        "id": Q,
        "name": "Signs of Revolt",
        "repeatable": True,
        "abortable": True,
        "questItems": [DOLL],
        "dialogs": dialogs,
    }


def main() -> None:
    questio.ensure_meta(max_active=25, talk_range=250)
    for q in (q385(), q426(), q505(), q646()):
        questio.upsert_quest(q)
        print(f"upserted {q['id']} {q['name']} dialogs={len(q['dialogs'])}")
    questio.ensure_meta(max_active=25, talk_range=250)


if __name__ == "__main__":
    main()