#!/usr/bin/env python3
"""Set raidbosses.json enabled flags from Mobius spawn semantics (retail on, script-only off)."""

from __future__ import annotations

import json
import sys
from pathlib import Path


def is_script_only(row: dict) -> bool:
    has_window = bool(
        row.get("respawnDelaySeconds")
        or row.get("respawnIntervalHours")
        or row.get("respawnRandomHours")
    )
    at_origin = row.get("x", 0) == 0 and row.get("y", 0) == 0 and row.get("z", 0) == 0
    return not has_window and at_origin


def main() -> int:
    path = Path(sys.argv[1] if len(sys.argv) > 1 else "data/raidbosses.json")
    doc = json.loads(path.read_text())
    enabled = 0
    for row in doc.get("bosses", []):
        on = not is_script_only(row)
        row["enabled"] = on
        if on:
            enabled += 1
    doc["source"] = (
        "L2J Mobius CT 0 Interlude raidboss_spawnlist, grandboss_data, and GrandBoss.ini "
        "(values transcribed; SQL and Java not copied). Retail rows with spawn coordinates are enabled; "
        "seven script-only bosses (Dimensional Rift Anakazel, Dr Chaos golem) stay disabled until those systems spawn them."
    )
    path.write_text(json.dumps(doc, indent=2) + "\n")
    print(f"wrote {path}: {enabled}/{len(doc['bosses'])} enabled")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())