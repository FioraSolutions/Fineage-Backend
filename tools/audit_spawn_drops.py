#!/usr/bin/env python3
"""Report attackable Monster/RaidBoss npc ids in enabled spawn maps missing from data/drops.

Matches DropSpawnCoverageTests: enabled spawns + enabled raidbosses rows, Monster/RaidBoss types only
(Folk/Guard/Merchant service NPCs are out of scope — same filter as gen_drops interest in combat loot).

When a Mobius stats/npcs checkout is available, ids with no <dropLists> in upstream XML are listed
separately (expected catalog gaps).

Usage:
  python3 tools/audit_spawn_drops.py [--mobius PATH] [--write-exclusions PATH] [--json]

Exit 1 when any npc id that should have a Fineage droplist (Mobius has dropLists) is missing.
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def resolve_mobius_npcs(arg: str | None) -> Path | None:
    if arg:
        p = Path(arg)
        return p if p.is_dir() else None
    for rel in (
        "ref/mobius-ct0-interlude/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/npcs",
        "ref/mobius-ct0-interlude/dist/game/data/stats/npcs",
        "ref/mobius-ct0-interlude/data/stats/npcs",
    ):
        p = ROOT / rel
        if p.is_dir():
            return p
    return None


def load_spawn_npc_ids() -> set[int]:
    ids: set[int] = set()
    for path in sorted((ROOT / "data/spawns").glob("*.json")):
        doc = json.loads(path.read_text())
        if not doc.get("enabled", True):
            continue
        for row in doc.get("npcs", []):
            ids.add(int(row["npcId"]))
    bosses = ROOT / "data/raidbosses.json"
    if bosses.is_file():
        for row in json.loads(bosses.read_text()).get("bosses", []):
            if row.get("enabled", True):
                ids.add(int(row["npcId"]))
    return ids


def load_templates() -> dict[int, dict]:
    return {int(n["npcId"]): n for n in json.loads((ROOT / "data/npcs.json").read_text())["npcs"]}


def load_drop_npc_ids() -> set[int]:
    out: set[int] = set()
    for path in (ROOT / "data/drops").glob("*.json"):
        if path.name.startswith("spawn-droplist-"):
            continue
        out.add(int(json.loads(path.read_text())["npcId"]))
    return out


def requires_catalog(template: dict) -> bool:
    return template.get("type") in ("Monster", "RaidBoss") and bool(template.get("attackable"))


def mobius_drop_shape(npcs_dir: Path) -> tuple[set[int], set[int]]:
    """Returns (has_non_empty_drop_lists, no_drop_lists_element_or_empty)."""
    has_drops: set[int] = set()
    no_lists: set[int] = set()
    for xml in npcs_dir.glob("*.xml"):
        root = ET.parse(xml).getroot()
        for npc in root.iter("npc"):
            nid = int(npc.get("id"))
            dl = npc.find("dropLists")
            if dl is None:
                no_lists.add(nid)
                continue
            drop = dl.find("drop")
            spoil = dl.find("spoil")
            items = []
            if drop is not None:
                for ch in drop:
                    if ch.tag in ("group", "item"):
                        items.append(ch)
            if spoil is not None:
                items.extend(spoil.findall("item"))
            if items:
                has_drops.add(nid)
            else:
                no_lists.add(nid)
    return has_drops, no_lists


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mobius", help="Mobius stats/npcs directory")
    parser.add_argument(
        "--write-exclusions",
        metavar="PATH",
        help="Write spawn-droplist-exclusions.json (npc ids with no Mobius dropLists)",
    )
    parser.add_argument("--json", action="store_true", help="Emit machine-readable report on stdout")
    args = parser.parse_args()

    spawn_ids = load_spawn_npc_ids()
    templates = load_templates()
    drops = load_drop_npc_ids()

    required = [
        nid
        for nid in sorted(spawn_ids)
        if (t := templates.get(nid)) and requires_catalog(t) and nid not in drops
    ]

    mobius_dir = resolve_mobius_npcs(args.mobius)
    exclusions: list[int] = []
    if mobius_dir:
        has_mobius, no_lists = mobius_drop_shape(mobius_dir)
        exclusions = sorted(nid for nid in required if nid in no_lists)
        holes = sorted(nid for nid in required if nid in has_mobius)
    else:
        holes = required

    if args.write_exclusions:
        out_path = Path(args.write_exclusions)
        payload = {
            "source": (
                "Npc ids in the default world spawn set (enabled maps + enabled raid bosses) "
                "with no Mobius stats/npcs <dropLists> content. DropSpawnCoverageTests skips these."
            ),
            "reference": "Regenerate with tools/audit_spawn_drops.py --write-exclusions when the spawn set changes.",
            "npcIds": exclusions,
        }
        out_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n")

    if args.json:
        print(
            json.dumps(
                {
                    "spawnNpcIds": len(spawn_ids),
                    "requiredMissingCatalog": required,
                    "mobiusDroplistHoles": holes,
                    "noMobiusDropLists": exclusions if mobius_dir else None,
                    "mobiusNpcDir": str(mobius_dir) if mobius_dir else None,
                },
                indent=2,
            )
        )
    else:
        print(f"spawn npc ids (enabled): {len(spawn_ids)}")
        print(f"missing Monster/RaidBoss catalog rows: {len(required)}")
        if mobius_dir:
            print(f"expected gaps (no Mobius dropLists): {len(exclusions)}")
            print(f"Mobius droplist holes (need import): {len(holes)}")
            for nid in holes:
                print(f"  HOLE {nid} {templates.get(nid, {}).get('name', '')}")
        else:
            print("Mobius npc dir not found; pass --mobius or clone ref/ (see docs/MOBIUS_XML_REF.md)")
            for nid in required[:40]:
                print(f"  missing {nid} {templates.get(nid, {}).get('name', '')}")
            if len(required) > 40:
                print(f"  … and {len(required) - 40} more")

    return 1 if holes else 0


if __name__ == "__main__":
    sys.exit(main())