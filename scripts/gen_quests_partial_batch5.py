#!/usr/bin/env python3
"""Issue #808 batch 5: generic event rebind, completion, remaining gaps."""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

COMPLETION = (22, 37, 38, 118, 123)
TELEPORT = (420, 620)
SPAWN = (115, 171, 242, 275, 334, 337, 340, 348, 503, 609, 615, 651, 652, 653)
KILL = (22, 111, 125, 162, 335, 337, 372, 619, 627, 633, 634, 638, 639, 647, 662, 663)
TALK = (299, 334, 344, 355, 365, 371, 372, 373, 402, 422, 505, 603, 604)


def run(script: str, *args: int) -> None:
    if not args:
        return
    subprocess.check_call([sys.executable, str(ROOT / "scripts" / script)] + [str(a) for a in args])


def main() -> None:
    subprocess.check_call([sys.executable, str(ROOT / "scripts/fix_quest_event_npc_binding.py")])
    run("wire_mobius_completion.py", *COMPLETION)
    run("wire_mobius_kill_targets.py", *KILL)
    run("wire_mobius_talk_gaps.py", *TALK)
    run("wire_mobius_teleport.py", *TELEPORT)
    run("wire_mobius_spawns.py", *SPAWN)
    import questio

    questio.ensure_meta(reference="Mobius CT_0 partial batch5 via scripts/gen_quests_partial_batch5.py")
    print("batch5 done")


if __name__ == "__main__":
    main()