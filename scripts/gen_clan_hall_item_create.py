#!/usr/bin/env python3
"""Emit data/clan-hall-item-create.json from Mobius clan hall buylists (list id = hallId + level × 100000).

Requires Mobius CT_0 buylist XML (clanhalls_buylists) on disk; pass --mobius-data PATH.
When XML is unavailable, the committed JSON in data/ remains the catalog source.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "data" / "clan-hall-item-create.json"
HALLS = ROOT / "data" / "clan-halls.json"
SCALE = 100_000
MANAGER_NPC = 35384


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mobius-data", type=Path, help="Mobius dist/game/data root with buylists/")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if args.mobius_data is None:
        print("No --mobius-data path; nothing generated (use committed data/clan-hall-item-create.json).", file=sys.stderr)
        return 0
    buy_root = args.mobius_data / "buylists"
    if not buy_root.is_dir():
        print(f"Missing buylists under {buy_root}", file=sys.stderr)
        return 1
    halls = json.loads(HALLS.read_text(encoding="utf-8")).get("halls", [])
    lists = []
    for hall in halls:
        hall_id = int(hall["id"])
        for level in (1, 2, 3):
            list_id = hall_id + level * SCALE
            xml = buy_root / f"{list_id:07d}.xml"
            if not xml.is_file():
                continue
            items = _parse_buylist_xml(xml.read_text(encoding="utf-8", errors="replace"))
            if not items:
                continue
            entries = []
            for i, (item_id, price) in enumerate(items, start=1):
                entries.append({
                    "id": i,
                    "ingredients": [{"id": 57, "count": price}],
                    "products": [{"id": item_id, "count": 1}],
                })
            lists.append({
                "id": list_id,
                "applyTaxes": False,
                "maintainEnchantment": False,
                "npcs": [MANAGER_NPC],
                "entries": entries,
            })
    doc = {
        "source": f"Imported from {buy_root}",
        "lists": lists,
    }
    text = json.dumps(doc, indent=1) + "\n"
    if args.dry_run:
        print(text)
        return 0
    OUT.write_text(text, encoding="utf-8")
    print(f"wrote {OUT} ({len(lists)} lists)")
    return 0


def _parse_buylist_xml(text: str) -> list[tuple[int, int]]:
    """Minimal Mobius buylist parse: item id and adena price pairs."""
    import re

    rows: list[tuple[int, int]] = []
    for m in re.finditer(r'item id="(\d+)"[^>]*price="(\d+)"', text):
        rows.append((int(m.group(1)), int(m.group(2))))
    return rows


if __name__ == "__main__":
    raise SystemExit(main())