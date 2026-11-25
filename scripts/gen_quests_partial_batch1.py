#!/usr/bin/env python3
"""Issue #808 batch 1: wire Mobius kill targets for auto-ported partial hunt/collect packs.

After: python3 scripts/gen_quests_partial_batch1.py
       python3 tools/quest_status.py --mobius .cache/l2j-mobius/L2J_Mobius_CT_0_Interlude
"""
from __future__ import annotations

import subprocess
import sys

BATCH1 = (
    25, 32, 34, 35, 36, 42, 43, 44,
    103, 117, 171, 235, 275, 432, 688,
)


def main() -> None:
    root = __import__("pathlib").Path(__file__).resolve().parent.parent
    args = [sys.executable, str(root / "scripts/wire_mobius_kill_targets.py")] + [str(q) for q in BATCH1]
    subprocess.check_call(args)
    import questio

    questio.ensure_meta(reference="Mobius CT_0 partial batch1 via scripts/gen_quests_partial_batch1.py")
    print("wired", len(BATCH1), "quests")


if __name__ == "__main__":
    main()