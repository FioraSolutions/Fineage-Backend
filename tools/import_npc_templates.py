#!/usr/bin/env python3
"""Transcribe L2J Mobius CT 0 Interlude NPC templates and town spawns.

Reads the Mobius datapack (not copied into this repo) and writes
data/npcs.json plus data/spawns/<town>.json. Values are transcribed.
This script does not copy GPL source.

Mobius references (L2J_Mobius_CT_0_Interlude):
  dist/game/data/stats/npcs/*.xml
    NpcData parses <status attackable> (default true) and <ai isAggressive>
    (default true). See NpcTemplate.set.
  dist/game/data/spawns/<territory>/*NPCs.xml and *VillageNPCs.xml
    SpawnData reads npc id, x, y, z, heading, respawnDelay, respawnRandom.
    Spawn.setRespawnDelay treats those numbers as seconds.

Usage:
  python3 tools/import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude
"""

from __future__ import annotations

import json
import math
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Town object ids start here. Legacy gremlin rows stay in 50000–59999.
# Item instances start at 100000. See Fineage.Game.NpcObjectIdAllocator.
TOWN_OBJECT_ID_FIRST = 60_000
TOWN_OBJECT_ID_END = 100_000

# (map id, mobius path relative to data/spawns, enabled on the current zone)
# Talking Island is enabled because the running zone is that map.
# Other towns stay disabled until multi-map init.
TOWNS: list[tuple[str, str, bool]] = [
    ("ti_village", "TalkingIsland/TalkingIslandNPCs.xml", True),
    ("aden", "Aden/AdenNPCs.xml", False),
    ("dark_elven_village", "DarkElfTerritory/DarkElfVillageNPCs.xml", False),
    ("dion", "Dion/DionNPCs.xml", False),
    ("dwarven_village", "DwarvenTerritory/DwarvenVillageNPCs.xml", False),
    ("elven_village", "ElvenTerritory/ElvenVillageNPCs.xml", False),
    ("floran", "Dion/FloranVillageNPCs.xml", False),
    ("giran", "Giran/GiranNPCs.xml", False),
    ("gludin", "Gludin/GludinVillageNPCs.xml", False),
    ("gludio", "Gludio/GludioNPCs.xml", False),
    ("goddard", "Goddard/GoddardNPCs.xml", False),
    ("heine", "Innadril/HeineNPCs.xml", False),
    ("hunters", "Hunters/HuntersNPCs.xml", False),
    ("orc_village", "OrcTerritory/OrcVillageNPCs.xml", False),
    ("oren", "Oren/OrenNPCs.xml", False),
    ("rune", "Rune/RuneNPCs.xml", False),
    ("schuttgart", "Schuttgart/SchuttgartNPCs.xml", False),
]


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def parse_bool(text: str | None, default: bool) -> bool:
    if text is None or text.strip() == "":
        return default
    value = text.strip().lower()
    if value == "true":
        return True
    if value == "false":
        return False
    raise SystemExit(f"expected true/false, got {text!r}")


def child(node: ET.Element, name: str) -> ET.Element | None:
    for item in node:
        if local(item.tag) == name:
            return item
    return None


def whole_or_float(value: float) -> int | float:
    if not math.isfinite(value):
        return 0.1
    nearest = round(value, 4)
    if nearest == math.trunc(nearest):
        return int(nearest)
    return nearest


def positive_stat(text: str | None, fallback: float) -> int | float:
    if text is None or text.strip() == "":
        return whole_or_float(fallback)
    value = float(text)
    if value <= 0:
        value = fallback
    return whole_or_float(value)


def hp_stat(text: str | None) -> int:
    """Nearest int, minimum 1. Mobius stores vitals hp as a double."""
    if text is None or text.strip() == "":
        return 1
    value = float(text)
    if not math.isfinite(value) or value <= 0:
        return 1
    rounded = int(math.floor(value + 0.5))
    if rounded > 2_147_483_647:
        return 2_147_483_647
    return max(1, rounded)


def load_templates(npc_dir: Path) -> list[dict]:
    templates: dict[int, dict] = {}
    files = sorted(npc_dir.glob("*.xml"))
    if not files:
        raise SystemExit(f"no npc xml under {npc_dir}")

    for path in files:
        root = ET.parse(path).getroot()
        for node in root:
            if local(node.tag) != "npc":
                continue
            npc_id = int(node.attrib["id"])
            if npc_id in templates:
                raise SystemExit(f"duplicate npc id {npc_id} in {path}")

            status = child(node, "status")
            ai = child(node, "ai")
            stats = child(node, "stats")
            vitals = child(stats, "vitals") if stats is not None else None
            speed = child(stats, "speed") if stats is not None else None
            walk = None
            run = None
            if speed is not None:
                for item in speed:
                    if local(item.tag) == "walk":
                        walk = item
                    elif local(item.tag) == "run":
                        run = item
            collision = child(node, "collision")
            radius = None
            height = None
            if collision is not None:
                for item in collision:
                    if local(item.tag) == "radius":
                        radius = item
                    elif local(item.tag) == "height":
                        height = item

            # NpcData: missing attackable/isAggressive become StatSet null, then
            # NpcTemplate defaults attackable=true and isAggressive=true.
            # Missing ground speed is clamped to 0.1 when the attribute is present
            # and non-positive; a missing element uses that same floor.
            attackable = parse_bool(None if status is None else status.attrib.get("attackable"), True)
            aggressive = parse_bool(None if ai is None else ai.attrib.get("isAggressive"), True)
            level = int(node.attrib.get("level", "70"))
            # Mobius allows an empty name (doors, effect points). Town spawns do not use those.
            name = (node.attrib.get("name") or "").strip()

            templates[npc_id] = {
                "npcId": npc_id,
                "name": name,
                "title": (node.attrib.get("title") or "").strip(),
                "type": (node.attrib.get("type") or "Folk").strip() or "Folk",
                "level": level,
                "collisionRadius": positive_stat(None if radius is None else radius.attrib.get("normal"), 0.1),
                "collisionHeight": positive_stat(None if height is None else height.attrib.get("normal"), 0.1),
                "attackable": attackable,
                "aggressive": aggressive,
                "hp": hp_stat(None if vitals is None else vitals.attrib.get("hp")),
                "mp": hp_stat(None if vitals is None else vitals.attrib.get("mp")),
                "runSpeed": positive_stat(None if run is None else run.attrib.get("ground"), 0.1),
                "walkSpeed": positive_stat(None if walk is None else walk.attrib.get("ground"), 0.1),
            }

    return [templates[npc_id] for npc_id in sorted(templates)]


def spawn_rows(path: Path) -> list[dict]:
    root = ET.parse(path).getroot()
    rows: list[dict] = []
    for spawn in root:
        if local(spawn.tag) != "spawn":
            continue
        for node in spawn:
            if local(node.tag) != "npc":
                continue
            if "x" not in node.attrib or "y" not in node.attrib or "z" not in node.attrib:
                raise SystemExit(f"{path} npc {node.attrib.get('id')} has no x/y/z")
            count = int(node.attrib.get("count", "1"))
            if count < 1:
                raise SystemExit(f"{path} npc {node.attrib.get('id')} has count {count}")
            delay = int(node.attrib.get("respawnDelay", "0"))
            random_delay = int(node.attrib.get("respawnRandom", "0"))
            heading = int(node.attrib.get("heading", "0"))
            npc_id = int(node.attrib["id"])
            for _ in range(count):
                rows.append(
                    {
                        "npcId": npc_id,
                        "x": int(node.attrib["x"]),
                        "y": int(node.attrib["y"]),
                        "z": int(node.attrib["z"]),
                        "heading": heading,
                        "respawnDelaySeconds": delay,
                        "respawnRandomSeconds": random_delay,
                    }
                )
    if not rows:
        raise SystemExit(f"{path} has no npc spawns")
    return rows


def write_templates(repo: Path, templates: list[dict]) -> None:
    path = repo / "data" / "npcs.json"
    lines = [
        "{",
        '  "source": "L2J Mobius CT 0 Interlude dist/game/data/stats/npcs (values transcribed, XML not copied)",',
        '  "npcs": [',
    ]
    for index, row in enumerate(templates):
        comma = "," if index + 1 < len(templates) else ""
        lines.append("    " + json.dumps(row, ensure_ascii=False, separators=(", ", ": ")) + comma)
    lines.append("  ]")
    lines.append("}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_towns(repo: Path, templates: dict[int, dict], spawns_dir: Path) -> list[tuple[str, int, bool]]:
    by_id = templates
    next_id = TOWN_OBJECT_ID_FIRST
    summary: list[tuple[str, int, bool]] = []
    for map_id, relative, enabled in TOWNS:
        source = spawns_dir / relative
        rows = spawn_rows(source)
        encoded = []
        for row in rows:
            template = by_id.get(row["npcId"])
            if template is None:
                raise SystemExit(f"{source} references missing npc {row['npcId']}")
            if next_id >= TOWN_OBJECT_ID_END:
                raise SystemExit("town object id range exhausted")
            encoded.append(
                {
                    "objectId": next_id,
                    "npcId": row["npcId"],
                    "x": row["x"],
                    "y": row["y"],
                    "z": row["z"],
                    "heading": row["heading"],
                    "respawnDelaySeconds": row["respawnDelaySeconds"],
                    "respawnRandomSeconds": row["respawnRandomSeconds"],
                }
            )
            next_id += 1

        payload = {
            "mapId": map_id,
            "enabled": enabled,
            "comment": (
                f"Mobius CT 0 Interlude {relative}. "
                "Rows reference data/npcs.json by npcId. "
                "Name, title, type, attackable, hp, and speeds come from the template."
            ),
            "npcs": encoded,
        }
        destination = repo / "data" / "spawns" / f"{map_id}.json"
        destination.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        summary.append((map_id, len(encoded), enabled))
    return summary


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude")
    root = Path(sys.argv[1])
    npc_dir = root / "dist" / "game" / "data" / "stats" / "npcs"
    spawns_dir = root / "dist" / "game" / "data" / "spawns"
    if not npc_dir.is_dir() or not spawns_dir.is_dir():
        raise SystemExit(f"{root} is not an L2J_Mobius_CT_0_Interlude datapack root")

    repo = Path(__file__).resolve().parents[1]
    templates = load_templates(npc_dir)
    write_templates(repo, templates)
    by_id = {row["npcId"]: row for row in templates}
    summary = write_towns(repo, by_id, spawns_dir)
    print(f"templates {len(templates)}")
    for map_id, count, enabled in summary:
        print(f"spawn {map_id} count={count} enabled={str(enabled).lower()}")


if __name__ == "__main__":
    main()
