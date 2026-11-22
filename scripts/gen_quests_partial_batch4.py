#!/usr/bin/env python3
"""Issue #808 batch 4: talk gaps + remaining kill gaps."""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

TALK = (31, 119, 364, 383, 621, 622, 648)
KILL = (299, 344, 355, 365, 371, 372, 373, 422, 603, 604)

ROOT = Path(__file__).resolve().parent.parent


def main() -> None:
    if TALK:
        subprocess.check_call(
            [sys.executable, str(ROOT / "scripts/wire_mobius_talk_gaps.py")] + [str(q) for q in TALK]
        )
    if KILL:
        subprocess.check_call(
            [sys.executable, str(ROOT / "scripts/wire_mobius_kill_targets.py")] + [str(q) for q in KILL]
        )
    import questio

    questio.ensure_meta(reference="Mobius CT_0 partial batch4 via scripts/gen_quests_partial_batch4.py")
    print("talk", len(TALK), "kill", len(KILL))


if __name__ == "__main__":
    main()