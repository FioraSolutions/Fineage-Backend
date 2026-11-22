#!/usr/bin/env python3
"""Extract Mobius MercTicketManager item/npc/limit tables into data/merc_tickets.json."""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/managers/MercTicketManager.java"
OUT = ROOT / "data" / "merc_tickets.json"

CASTLES = ["Gludio", "Dion", "Giran", "Oren", "Aden", "Innadril", "Goddard", "Rune", "Schuttgart"]
GUARDIAN_TYPES = 52


def parse_int_array(name: str, text: str) -> list[int]:
    m = re.search(rf"{re.escape(name)}\s*=\s*\{{(.*?)\}};", text, re.S)
    if not m:
        raise SystemExit(f"missing array {name}")
    return [int(x) for x in re.findall(r"\d+", m.group(1))]


def main() -> None:
    text = SRC.read_text()
    item_ids = parse_int_array("ITEM_IDS", text)
    npc_ids = parse_int_array("NPC_IDS", text)
    max_type = parse_int_array("MAX_MERC_PER_TYPE", text)
    max_castle = parse_int_array("MERCS_MAX_PER_CASTLE", text)
    if len(item_ids) != len(npc_ids) or len(item_ids) != len(max_type):
        raise SystemExit("array length mismatch")
    rows = []
    for i, item_id in enumerate(item_ids):
        castle = i // GUARDIAN_TYPES + 1
        rows.append({
            "itemId": item_id,
            "npcId": npc_ids[i],
            "castleId": castle,
            "maxPerType": max_type[i],
            "respawnSeconds": 0,
        })
    dawn_min = 6175  # first dawn ticket block in Mobius tables
    out = {
        "reference": "Mobius CT_0 MercTicketManager.java",
        "maxPerCastle": {str(i + 1): max_castle[i] for i in range(len(max_castle))},
        "dawnTicketMinItemId": dawn_min,
        "tickets": rows,
    }
    OUT.write_text(json.dumps(out, indent=1) + "\n")
    print(f"wrote {len(rows)} tickets to {OUT}")


if __name__ == "__main__":
    main()