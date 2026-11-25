#!/usr/bin/env python3
"""Issue #808 batch 2: wire kill targets for the next partial hunt packs."""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

BATCH2 = (
    105, 114, 327, 330, 337, 340, 345, 348, 351, 352, 356, 359, 370, 380, 381,
)

ROOT = Path(__file__).resolve().parent.parent


def main() -> None:
    args = [sys.executable, str(ROOT / "scripts/wire_mobius_kill_targets.py")] + [str(q) for q in BATCH2]
    subprocess.check_call(args)
    import questio

    questio.ensure_meta(reference="Mobius CT_0 partial batch2 via scripts/gen_quests_partial_batch2.py")
    print("wired", len(BATCH2), "quests")


if __name__ == "__main__":
    main()