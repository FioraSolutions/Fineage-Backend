#!/usr/bin/env python3
"""Audit Fineage quest packs against the Mobius CT_0 Interlude quest scripts.

Reads only identifiers (quest id, NPC / monster ids, level gates, which engine calls a script makes) from the
Mobius Java to classify coverage; no Mobius code is reproduced. Writes docs/QUEST_STATUS.md.

Status rules (heuristic, see the doc header):
  missing      no data/quests/<id>_* folder
  stub         folder exists but no quest start (create/start op) or <= 2 dialogs
  partial      some Mobius talk NPC or kill target is absent from the pack, no completion op, or the script
               needs an engine feature the pack does not use (quest spawns, class change, teleport)
  implemented  everything above is covered

Usage: tools/quest_status.py [--mobius /workspace/mobius/L2J_Mobius_CT_0_Interlude] [--out docs/QUEST_STATUS.md]
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Quests implemented in C# rather than (or on top of) the declarative pack. id -> note.
SCRIPTED = {
    **{i: "saga engine (QuestCatalog.Sagas + _sagas.json); simplified: no skill casts, helper NPC chat timers" for i in range(70, 101)},
}


def kind_of(qid: int, repeatable: bool) -> str:
    if 70 <= qid <= 100:
        return "3rd class (Saga)"
    if 401 <= qid <= 418:
        return "1st class (Path)"
    if 211 <= qid <= 233:
        return "2nd class (Trial/Test)"
    if qid in (234, 235):
        return "subclass prerequisite"
    if 241 <= qid <= 247:
        return "noblesse"
    if qid in (255, 999):
        return "tutorial"
    if qid in (27, 28, 29, 30, 50, 51, 52, 53, 426):
        return "fishing"
    if 419 <= qid <= 421:
        return "pet"
    if 501 <= qid <= 510:
        return "clan"
    if qid in (11, 12) or 605 <= qid <= 616:
        return "Ketra/Varka alliance"
    if qid in (337, 501):
        return "boss entry"
    if repeatable:
        return "repeatable"
    if qid < 200:
        return "story / newbie"
    return "one-time"


def ints(text: str) -> list[int]:
    return [int(x) for x in re.findall(r"\b\d{4,6}\b", text)]


def parse_mobius(path: str) -> dict:
    src = open(path, encoding="utf-8", errors="replace").read()
    consts: dict[str, list[int]] = {}
    for m in re.finditer(r"(?:static\s+final\s+)?int\s+([A-Z0-9_]+)\s*=\s*(-?\d+)\s*;", src):
        consts[m.group(1)] = [int(m.group(2))]
    for m in re.finditer(r"int\[\]\s+([A-Z0-9_]+)\s*=\s*\{([^}]*)\}", src, re.S):
        consts[m.group(1)] = ints(m.group(2))

    def resolve(args: str) -> tuple[set[int], bool]:
        out: set[int] = set()
        unresolved = False
        for tok in re.split(r"[,\s()]+", args):
            if not tok:
                continue
            if tok.isdigit():
                out.add(int(tok))
            elif tok in consts:
                out.update(consts[tok])
            elif re.match(r"[A-Z_][A-Z0-9_]*", tok):
                unresolved = True
        return out, unresolved

    def calls(name: str) -> tuple[set[int], bool]:
        ids: set[int] = set()
        unresolved = False
        for m in re.finditer(name + r"\s*\(([^;]*)\)\s*;", src):
            got, un = resolve(m.group(1))
            ids |= got
            unresolved |= un
        return ids, unresolved

    talk, u1 = calls("addStartNpc")
    t2, u2 = calls("addTalkId")
    talk |= t2
    kill, u3 = calls("addKillId")
    a2, _ = calls("addAttackId")
    kill |= a2
    if u3 or (not kill and "addKillId" in src):
        # Map / list driven kill ids: monster ids are 18000–29999 (raid 25xxx, quest 27xxx).
        kill |= {i for i in ints(src) if 18000 <= i < 30000}
    talk = {i for i in talk if i >= 30000 or i < 18000}

    level = ""
    lo = re.search(r"MIN_LEVEL\s*=\s*(\d+)", src) or re.search(r"getLevel\(\)\s*<\s*(\d+)", src) \
        or re.search(r"getLevel\(\)\s*>=\s*(\d+)", src)
    hi = re.search(r"MAX_LEVEL\s*=\s*(\d+)", src) or re.search(r"getLevel\(\)\s*>\s*(\d+)", src)
    if lo:
        level = lo.group(1) + "+"
        if hi:
            level = f"{lo.group(1)}–{hi.group(1)}"
    name = re.search(r'super\(\s*\d+\s*,\s*"([^"]*)"', src) or re.search(r"super\(\s*\d+\s*\)", src)
    features = []
    if "addSpawn(" in src:
        features.append("spawns")
    if "startQuestTimer" in src:
        features.append("timers")
    if re.search(r"setClassId|setBaseClass|changeClass", src):
        features.append("class change")
    if re.search(r"player\.teleToLocation", src):
        features.append("teleport")
    if re.search(r"getRandomPartyMember|getParty\(\)", src):
        features.append("party")
    if "setNoble" in src:
        features.append("noblesse")
    return {
        "name": name.group(1) if name and name.groups() else "",
        "talk": talk,
        "kill": kill,
        "level": level,
        "repeatable": bool(re.search(r"exitQuest\(\s*true|QuestType\.REPEATABLE", src)),
        "features": features,
        "html": len(glob.glob(os.path.join(os.path.dirname(path), "*.htm*"))),
        "saga": "AbstractSagaQuest" in src,
    }


def walk_ops(node, out: set[str]):
    if isinstance(node, dict):
        if "op" in node:
            out.add(node["op"])
        for v in node.values():
            walk_ops(v, out)
    elif isinstance(node, list):
        for v in node:
            walk_ops(v, out)


def parse_pack(folder: str) -> dict:
    q = json.load(open(os.path.join(folder, "quest.json"), encoding="utf-8"))
    ops: set[str] = set()
    walk_ops(q, ops)
    talk = {d["npcId"] for d in q["dialogs"] if d.get("hook") in ("talk", "event", "first")}
    kill = {d["npcId"] for d in q["dialogs"] if d.get("hook") in ("kill", "attack")}
    return {"name": q["name"], "dialogs": len(q["dialogs"]), "ops": ops, "talk": talk, "kill": kill,
            "repeatable": q.get("repeatable", False)}


# Mobius registers these mobs as kill targets of the quest without any effect on kill (spawned decoys only).
NO_EFFECT_KILLS = {225: {27092}, 226: {27126}}


def auto_ported_ids() -> set[int]:
    """Packs whose quest.json was last written by the structural auto-port (#303 / #304)."""
    auto_commits = {"239dbfc8", "55ffea8b"}
    try:
        out = subprocess.run(["git", "log", "--format=@%h", "--name-only", "--", "data/quests"], cwd=ROOT,
                             capture_output=True, text=True, check=True).stdout
    except Exception:
        return set()
    last: dict[int, str] = {}
    commit = ""
    for line in out.splitlines():
        if line.startswith("@"):
            commit = line[1:9]
            continue
        m = re.match(r"data/quests/(\d{5})_[^/]+/quest\.json", line)
        if m and int(m.group(1)) not in last:
            last[int(m.group(1))] = commit
    return {qid for qid, c in last.items() if c in auto_commits}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--mobius",
        default=os.path.join(ROOT, ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude"),
    )
    ap.add_argument("--out", default=os.path.join(ROOT, "docs", "QUEST_STATUS.md"))
    args = ap.parse_args()
    qdir = os.path.join(args.mobius, "dist/game/data/scripts/quests")
    mobius: dict[int, tuple[str, dict]] = {}
    for d in sorted(os.listdir(qdir)):
        m = re.match(r"Q(\d{5})_(\w+)", d)
        if not m:
            continue
        java = os.path.join(qdir, d, d + ".java")
        if os.path.exists(java):
            mobius[int(m.group(1))] = (m.group(2), parse_mobius(java))
    packs = {int(os.path.basename(f)[:5]): f for f in glob.glob(os.path.join(ROOT, "data/quests/[0-9]*_*"))
             if os.path.exists(os.path.join(f, "quest.json"))}
    saga_path = os.path.join(ROOT, "data/quests/_sagas.json")
    saga_ids = {s["id"] for s in json.load(open(saga_path))["sagas"]} if os.path.exists(saga_path) else set()
    auto = auto_ported_ids()
    hand = {i for i in packs if i not in auto}

    rows = []
    counts = {"implemented": 0, "partial": 0, "stub": 0, "missing": 0}
    for qid in sorted(mobius):
        cls, mo = mobius[qid]
        name = mo["name"] or re.sub(r"(?<!^)(?=[A-Z])", " ", cls)
        notes: list[str] = []
        if qid in SCRIPTED and qid in saga_ids:
            status = "implemented"
            notes.append(SCRIPTED[qid])
        elif qid not in packs:
            status = "missing"
            if mo["saga"]:
                notes.append("AbstractSagaQuest")
        else:
            pk = parse_pack(packs[qid])
            if not ({"create", "start"} <= pk["ops"]) or pk["dialogs"] <= 2:
                status = "stub"
                notes.append(f"{pk['dialogs']} dialog(s), no start")
            else:
                miss_talk = sorted(mo["talk"] - pk["talk"])
                miss_kill = sorted(mo["kill"] - pk["kill"] - NO_EFFECT_KILLS.get(qid, set()))
                if miss_talk:
                    notes.append("no talk: " + ",".join(map(str, miss_talk[:6])) + ("…" if len(miss_talk) > 6 else ""))
                if miss_kill:
                    notes.append(f"no kill: {len(miss_kill)}/{len(mo['kill'])} mobs")
                if "complete" not in pk["ops"] and not pk["repeatable"]:
                    notes.append("no completion")
                if "spawns" in mo["features"] and "spawn_npc" not in pk["ops"] and "summon" not in pk["ops"]:
                    notes.append("quest spawns missing")
                if "class change" in mo["features"]:
                    notes.append("class change missing")
                if "teleport" in mo["features"] and "teleport" not in pk["ops"]:
                    notes.append("teleport missing")
                status = "partial" if notes else "implemented"
            if "timers" in mo["features"]:
                notes.append("timers")
            notes.append("hand-ported" if qid in hand else "auto-port")
        counts[status] += 1
        rows.append((qid, name, mo["level"], kind_of(qid, mo["repeatable"]), status, "; ".join(notes)))

    extra = sorted(i for i in packs if i not in mobius)
    total = len(rows)
    with open(args.out, "w", encoding="utf-8") as f:
        f.write("# Quest status vs Mobius CT_0 Interlude\n\n")
        f.write("Generated by `tools/quest_status.py` from `dist/game/data/scripts/quests` (ids, NPC / monster ids, level\n"
                "gates and engine calls only) and `data/quests/`. Re-run after every quest batch.\n\n")
        f.write("Status: **implemented** — every Mobius talk NPC and kill target is wired, the quest starts and ends, and "
                "special mechanics the script uses (quest spawns, class change, teleport) are present. **partial** — "
                "loads and runs but something from that list is missing (see notes). **stub** — folder exists but the "
                "quest cannot be started. **missing** — no pack. `auto-port` marks the structural port of #303/#304, "
                "`hand-ported` a curated generator. The heuristic does not prove drop rates or reward amounts.\n\n")
        f.write("| Status | Count |\n|---|---|\n")
        for k in ("implemented", "partial", "stub", "missing"):
            f.write(f"| {k} | {counts[k]} |\n")
        f.write(f"| **total Mobius quests** | **{total}** |\n\n")
        if extra:
            f.write("Fineage-only packs: " + ", ".join(map(str, extra)) + " (Q256 newbie helper gift, Q998 Net smoke).\n\n")
        f.write("Related non-quest scripts: `village_master/*Change1|Change2` and `FirstClassTransferTalk` are the profession "
                "masters (`data/village-masters.json`, `scripts/gen_profession_transfers.py`); `AllianceMaster` / "
                "`ClanMaster` are the clan blocks (#590/#591).\n\n")
        f.write("| Id | Name | Level | Type | Status | Notes |\n|---|---|---|---|---|---|\n")
        for r in rows:
            f.write("| {} | {} | {} | {} | {} | {} |\n".format(*r))
    print(counts, "total", total)


if __name__ == "__main__":
    main()
