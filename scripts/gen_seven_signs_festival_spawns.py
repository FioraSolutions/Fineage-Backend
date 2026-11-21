#!/usr/bin/env python3
"""Extract festival monster waves from Mobius SevenSignsFestival.java into JSON."""
from __future__ import annotations

import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
JAVA = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/mechanics/sevensigns/SevenSignsFestival.java"
OUT = ROOT / "data/seven-signs-festival-spawns.json"

ARRAY_NAMES = [
    ("dawn", "primary", "FESTIVAL_DAWN_PRIMARY_SPAWNS"),
    ("dusk", "primary", "FESTIVAL_DUSK_PRIMARY_SPAWNS"),
    ("dawn", "secondary", "FESTIVAL_DAWN_SECONDARY_SPAWNS"),
    ("dusk", "secondary", "FESTIVAL_DUSK_SECONDARY_SPAWNS"),
    ("dawn", "chest", "FESTIVAL_DAWN_CHEST_SPAWNS"),
    ("dusk", "chest", "FESTIVAL_DUSK_CHEST_SPAWNS"),
]

ROW_RE = re.compile(r"\{\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)(?:\s*,\s*(-?\d+))?(?:\s*,\s*(-?\d+))?\s*\}")


def brace_block(text: str, start: int) -> tuple[str, int]:
    depth = 0
    i = start
    while i < len(text):
        ch = text[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[start : i + 1], i + 1
        i += 1
    raise SystemExit(f"Unbalanced braces at {start}")


def parse_tier_rows(block: str) -> list[list[dict]]:
    """Split a tier[][][] Java initializer into 5 tiers of spawn rows."""
    inner = block.strip()
    if inner.startswith("{"):
        inner = inner[1:].strip()
    if inner.endswith("}"):
        inner = inner[:-1].strip()
    tiers: list[list[dict]] = []
    i = 0
    tier_index = 0
    while tier_index < 5:
        while i < len(inner) and inner[i] in " \t\n\r,":
            i += 1
        if i >= len(inner):
            break
        tier_block, i = brace_block(inner, i)
        rows = []
        for m in ROW_RE.finditer(tier_block):
            x, y, z = int(m.group(1)), int(m.group(2)), int(m.group(3))
            npc_id = int(m.group(5) if m.group(5) else m.group(4) if m.group(4) and int(m.group(4)) > 1000 else -1)
            if npc_id < 0:
                continue
            rows.append({"x": x, "y": y, "z": z, "npcId": npc_id})
        tiers.append(rows)
        tier_index += 1
    if len(tiers) != 5:
        raise SystemExit(f"Expected 5 tiers, got {len(tiers)}")
    return tiers


def main() -> None:
    text = JAVA.read_text(encoding="utf-8", errors="replace")
    doc: dict = {"version": 1, "waves": {}}
    for cabal, wave, name in ARRAY_NAMES:
        marker = f"{name} ="
        pos = text.find(marker)
        if pos < 0:
            raise SystemExit(f"Missing {name}")
        brace_start = text.find("{", pos)
        block, _ = brace_block(text, brace_start)
        doc["waves"].setdefault(cabal, {})[wave] = parse_tier_rows(block)
    OUT.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()