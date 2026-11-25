#!/usr/bin/env python3
"""Issue #808 batch 3: wire kill targets (larger hunt / raid packs)."""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

BATCH3 = (
    384, 420, 618, 623, 625, 641, 642, 649, 650, 654, 659, 660, 661,
)

ROOT = Path(__file__).resolve().parent.parent


def main() -> None:
    args = [sys.executable, str(ROOT / "scripts/wire_mobius_kill_targets.py")] + [str(q) for q in BATCH3]
    subprocess.check_call(args)
    import questio

    questio.ensure_meta(reference="Mobius CT_0 partial batch3 via scripts/gen_quests_partial_batch3.py")
    print("wired", len(BATCH3), "quests")


if __name__ == "__main__":
    main()