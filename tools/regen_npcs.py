#!/usr/bin/env python3
"""Regenerate data/npcs.json from the pinned Mobius CT 0 Interlude datapack.

Runs, in order, against one CT_0_Interlude checkout (see PINNED_MOBIUS_COMMIT in
tools/import_npc_templates.py):

  1. tools/import_npc_templates.py      templates only (data/spawns untouched),
                                         custom rows (50007) kept
  2. scripts/gen_npc_races.py           <race>
  3. scripts/gen_npc_weapons.py         rhand / lhand / attackType / range
  4. scripts/gen_npc_shots.py           SoulShot / SpiritShot stock + chance
  5. scripts/gen_npc_ai_skills.py       AI attack skills, min/max skill chance
  6. scripts/gen_npc_weapon_traits.py   defenceTraits from skillList passives

Each pass reads the same pinned tree. Usage:

  python3 tools/regen_npcs.py /path/to/L2J_Mobius_CT_0_Interlude [--allow-unpinned]
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]


def run(*args: str) -> None:
    print("+ " + " ".join(args), flush=True)
    subprocess.run([sys.executable, *args], cwd=REPO, check=True)


def main() -> None:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = [a for a in sys.argv[1:] if a.startswith("--")]
    if len(args) != 1 or set(flags) - {"--allow-unpinned"}:
        raise SystemExit("usage: regen_npcs.py /path/to/L2J_Mobius_CT_0_Interlude [--allow-unpinned]")
    root = Path(args[0]).resolve()
    data = root / "dist" / "game" / "data"
    if not data.is_dir():
        data = root / "data"
    npcs = data / "stats" / "npcs"
    run("tools/import_npc_templates.py", str(root), "--templates-only", *flags)
    run("scripts/gen_npc_races.py", str(npcs))
    run("scripts/gen_npc_weapons.py", str(npcs), "data/npcs.json")
    run("scripts/gen_npc_shots.py", str(npcs))
    run("scripts/gen_npc_ai_skills.py", str(data))
    run("scripts/gen_npc_weapon_traits.py", str(data))


if __name__ == "__main__":
    main()
