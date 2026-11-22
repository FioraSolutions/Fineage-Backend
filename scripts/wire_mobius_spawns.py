#!/usr/bin/env python3
"""Wire Mobius addSpawn() into quest packs (spawn_npc / summon ops)."""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402
from quest_pack_809_helpers import mobius_java, parse_int_consts  # noqa: E402


def spawn_calls(script: str) -> list[dict]:
    src = mobius_java(script)
    consts = parse_int_consts(src)
    rows = []
    for m in re.finditer(
        r"addSpawn\s*\(\s*([A-Z0-9_]+|\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)",
        src,
    ):
        npc = int(m.group(1)) if m.group(1).isdigit() else consts.get(m.group(1))
        if not npc:
            continue
        rows.append(
            {
                "npcId": npc,
                "x": int(m.group(2)),
                "y": int(m.group(3)),
                "z": int(m.group(4)),
            }
        )
    seen_npc: set[int] = {r["npcId"] for r in rows}
    for m in re.finditer(
        r"addSpawn\s*\(\s*([A-Z0-9_]+|\d+)\s*,\s*player(?:\.getX\(\)|\s*\))",
        src,
    ):
        npc = int(m.group(1)) if m.group(1).isdigit() else consts.get(m.group(1))
        if not npc or npc in seen_npc:
            continue
        seen_npc.add(npc)
        rows.append({"npcId": npc, "atPlayer": True})
    for m in re.finditer(
        r"addSpawn\s*\(\s*([A-Z0-9_]+|\d+)\s*,\s*npc\b",
        src,
    ):
        npc = int(m.group(1)) if m.group(1).isdigit() else consts.get(m.group(1))
        if not npc or npc in seen_npc:
            continue
        seen_npc.add(npc)
        rows.append({"npcId": npc, "atPlayer": True})
    return rows


def has_spawn_op(quest: dict) -> bool:
    for d in quest.get("dialogs", []):
        for o in d.get("ops") or []:
            if o.get("op") in ("spawn_npc", "summon"):
                return True
    return False


def wire_quest(qid: int) -> str:
    folder = questio.find_quest_dir(qid)
    if not folder:
        return f"{qid}: no pack"
    script = next(d.name for d in questio.mobius_quest_root().iterdir() if d.name.startswith(f"Q{qid:05d}_"))
    quest = json.loads((folder / "quest.json").read_text(encoding="utf-8"))
    if has_spawn_op(quest):
        return f"{qid}: already has spawn"
    spawns = spawn_calls(script)
    if not spawns:
        return f"{qid}: no addSpawn"
    target = None
    for d in quest.get("dialogs", []):
        if d.get("hook") != "event":
            continue
        ops = d.get("ops") or []
        if any(o.get("op") == "set_cond" for o in ops):
            target = d
            break
    if target is None:
        for d in quest.get("dialogs", []):
            if d.get("hook") == "event" and any(o.get("op") == "start" for o in (d.get("ops") or [])):
                target = d
                break
    if target is None:
        return f"{qid}: no anchor event"
    ops = list(target.get("ops") or [])
    for s in spawns[:8]:
        row = {"op": "summon", "npcId": s["npcId"], "despawnSec": 300, "owned": True}
        if s.get("atPlayer"):
            row["atPlayer"] = True
        else:
            row["x"] = s["x"]
            row["y"] = s["y"]
            row["z"] = s["z"]
        ops.append(row)
    target["ops"] = ops
    out = questio.externalize_html(quest, folder)
    (folder / "quest.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return f"{qid}: wired {min(8, len(spawns))} spawn(s)"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("quest_ids", nargs="+", type=int)
    args = ap.parse_args()
    for qid in args.quest_ids:
        print(wire_quest(qid))


if __name__ == "__main__":
    main()