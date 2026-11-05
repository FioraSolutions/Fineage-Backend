#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude gatekeeper lists into data/teleports.json.

Reads the Mobius datapack (not copied into this repo) and writes original JSON.
This script does not copy GPL source.

Mobius reference (L2J_Mobius_CT_0_Interlude):
  dist/game/data/teleporters/**/*.xml
  java/.../data/xml/TeleporterData.java (list name defaults to the type)

Usage:
  python3 tools/import_teleports.py /path/to/L2J_Mobius_CT_0_Interlude
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ADENA = 57


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def castles(text: str | None) -> list[int]:
    raw = (text or "").strip()
    if not raw:
        return []
    return [int(part) for part in raw.split(";") if part.strip()]


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: import_teleports.py /path/to/L2J_Mobius_CT_0_Interlude")
    root = Path(sys.argv[1]) / "dist/game/data/teleporters"
    if not root.is_dir():
        raise SystemExit(f"missing {root}")

    keepers: dict[int, dict[str, dict]] = {}
    for path in sorted(root.rglob("*.xml")):
        for npc in ET.parse(path).getroot():
            if local(npc.tag) != "npc":
                continue
            npc_id = int(npc.attrib["id"])
            lists = keepers.setdefault(npc_id, {})
            for child in npc:
                if local(child.tag) != "teleport":
                    continue
                kind = child.attrib.get("type", "NORMAL")
                name = child.attrib.get("name", kind)
                locations = []
                for index, loc in enumerate(child):
                    if local(loc.tag) != "location":
                        continue
                    row = {
                        "id": index,
                        "name": loc.attrib.get("name", ""),
                        "x": int(loc.attrib["x"]),
                        "y": int(loc.attrib["y"]),
                        "z": int(loc.attrib["z"]),
                        "feeCount": int(loc.attrib.get("feeCount", "0")),
                    }
                    fee_id = loc.attrib.get("feeId")
                    if fee_id and int(fee_id) != ADENA:
                        row["feeItemId"] = int(fee_id)
                    castle_ids = castles(loc.attrib.get("castleId"))
                    if castle_ids:
                        row["castleIds"] = castle_ids
                    locations.append(row)
                lists[name] = {"name": name, "type": kind, "locations": locations}

    doc = {
        "reference": (
            "Teleport coordinates and fees transcribed from L2J Mobius CT 0 Interlude "
            "dist/game/data/teleporters. Original JSON, not a copy of the XML. "
            "List index is the bypass location id (0-based). Omitted feeItemId is adena 57. "
            "Noblesse lists are data only; the server does not run them."
        ),
        "freeTeleportLevel": 40,
        "gatekeepers": [
            {"npcId": npc_id, "lists": [keepers[npc_id][name] for name in sorted(keepers[npc_id])]}
            for npc_id in sorted(keepers)
        ],
    }
    out = Path(__file__).resolve().parents[1] / "data" / "teleports.json"
    out.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out} ({len(doc['gatekeepers'])} gatekeepers)")


if __name__ == "__main__":
    main()
