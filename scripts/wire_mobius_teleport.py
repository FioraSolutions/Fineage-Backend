#!/usr/bin/env python3
"""Wire Mobius teleToLocation calls into matching quest event ops."""
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


def teleports_by_html(script: str) -> dict[str, tuple[int, int, int]]:
    src = mobius_java(script)
    out: dict[str, tuple[int, int, int]] = {}

    for m in re.finditer(
        r'htmltext\s*=\s*"([^"]+)"[^}]{0,400}?player\.teleToLocation\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)',
        src,
        re.S,
    ):
        key = re.sub(r"\.html?$", "", m.group(1))
        out[key] = (int(m.group(2)), int(m.group(3)), int(m.group(4)))
    for m in re.finditer(
        r'player\.teleToLocation\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)\s*;[^}]{0,400}?htmltext\s*=\s*"([^"]+)"',
        src,
        re.S,
    ):
        key = re.sub(r"\.html?$", "", m.group(4))
        out[key] = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
    return out


def teleports_by_event(script: str) -> dict[str, tuple[int, int, int]]:
    src = mobius_java(script)
    consts = parse_int_consts(src)
    out: dict[str, tuple[int, int, int]] = {}

    def coords(m: re.Match) -> tuple[int, int, int] | None:
        parts = [p.strip() for p in m.group(1).split(",")]
        vals = []
        for p in parts[:3]:
            if re.fullmatch(r"-?\d+", p):
                vals.append(int(p))
            elif p in consts:
                vals.append(consts[p])
            else:
                return None
        return tuple(vals)  # type: ignore

    for m in re.finditer(
        r'event\.equals\("([^"]+)"\)\)\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}',
        src,
        re.S,
    ):
        body = m.group(2)
        tm = re.search(r"player\.teleToLocation\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)", body)
        if tm:
            out[re.sub(r"\.html?$", "", m.group(1))] = (
                int(tm.group(1)),
                int(tm.group(2)),
                int(tm.group(3)),
            )
    for block in re.finditer(r'case\s+"([^"]+)"\s*:\s*\{([^}]*)\}', src, re.S):
        body = block.group(2)
        m = re.search(r"teleToLocation\s*\(\s*([^)]+)\)", body)
        if not m:
            continue
        c = coords(m)
        if c:
            out[re.sub(r"\.html?$", "", block.group(1))] = c
    return out


def wire_quest(qid: int) -> str:
    folder = questio.find_quest_dir(qid)
    if not folder:
        return f"{qid}: no pack"
    script = next(d.name for d in questio.mobius_quest_root().iterdir() if d.name.startswith(f"Q{qid:05d}_"))
    tele = {**teleports_by_html(script), **teleports_by_event(script)}
    if not tele:
        return f"{qid}: no teleport events"
    quest = json.loads((folder / "quest.json").read_text(encoding="utf-8"))
    added = 0
    for d in quest.get("dialogs", []):
        if d.get("hook") not in ("event", "talk"):
            continue
        ev = str(d.get("event") or "")
        hf = str(d.get("htmlFile") or "")
        key = next((k for k in tele if k == ev or k in hf or k in ev or k.split("-", 1)[-1] in hf), None)
        if not key:
            continue
        x, y, z = tele[key]
        ops = list(d.get("ops") or [])
        if any(o.get("op") == "teleport" for o in ops):
            continue
        ops.append({"op": "teleport", "x": x, "y": y, "z": z})
        d["ops"] = ops
        added += 1
    if not added:
        for key, (x, y, z) in tele.items():
            if any(key == str(d.get("event") or "") for d in quest["dialogs"]):
                continue
            npc = next(
                (d["npcId"] for d in quest["dialogs"] if d.get("npcId")),
                0,
            )
            if m := re.match(r"^(\d+)", key):
                npc = int(m.group(1))
            quest.setdefault("dialogs", []).append(
                {
                    "npcId": npc,
                    "hook": "event",
                    "when": "started",
                    "event": key,
                    "ops": [{"op": "teleport", "x": x, "y": y, "z": z}],
                    "html": "<html><body></body></html>",
                }
            )
            added += 1
    if not added:
        return f"{qid}: no dialog match"
    out = questio.externalize_html(quest, folder)
    (folder / "quest.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return f"{qid}: wired {added} teleport(s)"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("quest_ids", nargs="+", type=int)
    args = ap.parse_args()
    for qid in args.quest_ids:
        print(wire_quest(qid))


if __name__ == "__main__":
    main()