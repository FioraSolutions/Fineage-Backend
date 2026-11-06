#!/usr/bin/env python3
"""Pin data/spawns/elven_starting.json to the Mobius CT 0 Interlude spawn rows.

Mobius keeps the whole outdoor world in `dist/game/data/spawns/InterludeSpawns.xml`; there is
no per-territory `ElvenTerritory/ElvenStarting.xml` in the Interlude datapack. The earlier
`tools/import_region_spawns.py` pass re-sampled the elven territory polygons instead of copying
those rows, so the map lost 33 mobs (Dryad 40/48, Kaboo Orc Fighter Leader 62/67, ...) and every
coordinate drifted away from retail.

This script copies the fixed Mobius rows verbatim for the elven forest monster set, so the map
stands exactly the mob count and positions the original server does.

    python3 scripts/gen_elven_spawns.py [--check]

Source order: ref/mobius-ct0-interlude/... (vendored snapshot), else $MOBIUS_SRC, else the
L2J_Mobius_CT_0_Interlude checkout under ~/Downloads. `--check` exits 1 on drift (CI-safe).
"""

from __future__ import annotations

import json
import os
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
SPAWNS = ROOT / "data" / "spawns"
MAP_ID = "elven_starting"
SPAWN_XML_REL = pathlib.Path("dist") / "game" / "data" / "spawns" / "InterludeSpawns.xml"
VENDORED_REL = pathlib.Path("ref") / "mobius-ct0-interlude" / "data" / "spawns" / "InterludeSpawns.xml"

# The elven forest monster set: the mobs that live in the Elven Village / Mother Tree box in
# Mobius. Row counts below are asserted, so a silent drop in the datapack fails loudly.
ELVEN_MONSTERS = {
    20013: 48,  # Dryad
    20019: 48,  # Dryad Elder
    20036: 40,  # Lirein
    20044: 28,  # Lirein Elder
    20308: 30,  # Hook Spider
    20325: 39,  # Goblin Raider
    20336: 30,  # Green Dryad
    20460: 24,  # Crimson Spider
    20466: 52,  # Pincer Spider
    20468: 22,  # Kaboo Orc
    20469: 32,  # Kaboo Orc Archer
    20470: 42,  # Kaboo Orc Soldier
    20471: 65,  # Kaboo Orc Warrior
    20472: 67,  # Kaboo Orc Warrior Captain
    20473: 51,  # Kaboo Orc Warrior Lieutenant
    20509: 81,  # Spore Fungus
    20525: 56,  # Gray Wolf
    20530: 11,  # Young Red Keltir
    20534: 42,  # Red Keltir
    20537: 37,  # Elder Red Keltir
}

COMMENT = (
    "Mobius CT 0 Interlude spawns/InterludeSpawns.xml, fixed rows copied verbatim for the elven "
    "forest monster set (x/y/z/heading/respawnDelay). Row counts match the original server, so the "
    "map stands the retail mob count around Elven Village. Regenerate with "
    "scripts/gen_elven_spawns.py; do not hand-edit."
)

OBJECT_ID_FIRST = 60_000
OBJECT_ID_END = 100_000
BOSS_ID_FIRST = 80_001
BOSS_ID_END = 82_000

# 60_000 upwards belongs to the town files (ti_village starts at 60_000). New monster ids are
# taken from the old region band so this map never claims a town id.
NEW_ID_FIRST = 63_000


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def spawn_xml() -> pathlib.Path:
    candidates = [ROOT / VENDORED_REL]
    env = os.environ.get("MOBIUS_SRC")
    if env:
        candidates.append(pathlib.Path(env) / SPAWN_XML_REL)
    candidates.append(pathlib.Path.home() / "Downloads" / "L2J_Mobius_CT_0_Interlude_src" / SPAWN_XML_REL)
    for path in candidates:
        if path.is_file():
            return path
    raise SystemExit(
        "InterludeSpawns.xml not found. Expected one of:\n  "
        + "\n  ".join(str(path) for path in candidates)
        + "\nSet MOBIUS_SRC=/path/to/L2J_Mobius_CT_0_Interlude_src or vendor the file under ref/."
    )


def mobius_rows(path: pathlib.Path) -> list[dict]:
    """Every elven-forest row, in datapack order (Mobius keeps them sorted by x)."""
    root = ET.parse(path).getroot()
    rows: list[dict] = []
    for spawn in root:
        if local(spawn.tag) != "spawn":
            continue
        for node in spawn:
            if local(node.tag) != "npc":
                continue
            npc_id = int(node.attrib["id"])
            if npc_id not in ELVEN_MONSTERS:
                continue
            rows.append(
                {
                    "npcId": npc_id,
                    "x": int(node.attrib["x"]),
                    "y": int(node.attrib["y"]),
                    "z": int(node.attrib["z"]),
                    "heading": int(node.attrib.get("heading", "0")),
                    "respawnDelaySeconds": int(node.attrib.get("respawnDelay", "0")),
                    "respawnRandomSeconds": int(node.attrib.get("respawnRandom", "0")),
                }
            )

    found: dict[int, int] = {}
    for row in rows:
        found[row["npcId"]] = found.get(row["npcId"], 0) + 1
    if found != ELVEN_MONSTERS:
        wanted = ", ".join(f"{npc_id}:{count}" for npc_id, count in sorted(ELVEN_MONSTERS.items()))
        got = ", ".join(f"{npc_id}:{count}" for npc_id, count in sorted(found.items()))
        raise SystemExit(f"{path} elven rows drift from the pinned counts.\n  want {wanted}\n  got  {got}")
    return rows


def used_object_ids(skip: pathlib.Path) -> set[int]:
    used: set[int] = set()
    for path in SPAWNS.glob("*.json"):
        if path == skip:
            continue
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        for row in payload.get("npcs") or []:
            object_id = row.get("objectId")
            if isinstance(object_id, int) and object_id > 0:
                used.add(object_id)
    return used


class Allocator:
    """Hands out object ids, reusing the row's previous id when it is still free."""

    def __init__(self, used: set[int], start: int = OBJECT_ID_FIRST) -> None:
        self._used = used
        self._next = max(start, OBJECT_ID_FIRST)

    def keep(self, object_id: int | None) -> int | None:
        if object_id is None or object_id in self._used:
            return None
        if not OBJECT_ID_FIRST <= object_id < OBJECT_ID_END:
            return None
        self._used.add(object_id)
        return object_id

    def take(self) -> int:
        while self._next < OBJECT_ID_END:
            if BOSS_ID_FIRST <= self._next < BOSS_ID_END:
                self._next = BOSS_ID_END
                continue
            candidate = self._next
            self._next += 1
            if candidate not in self._used:
                self._used.add(candidate)
                return candidate
        raise SystemExit("NPC object id range is exhausted")

    def reuse(self, spare: list[int]) -> int | None:
        """Consume one id from rows that no longer exist, so the map keeps its id block."""
        while spare:
            candidate = spare.pop(0)
            if candidate not in self._used:
                self._used.add(candidate)
                return candidate
        return None


def build(path: pathlib.Path) -> tuple[dict, list[str]]:
    """Return the payload plus a human-readable change list against what is on disk."""
    wanted = mobius_rows(spawn_xml())
    current = json.loads(path.read_text(encoding="utf-8")) if path.is_file() else {}
    old_rows = current.get("npcs") or []

    # Keep an object id attached to the exact spawn row it already describes, so a regeneration is
    # a no-op once the file matches Mobius and a client's cached object ids stay valid. Ids of rows
    # that Mobius no longer has become a free pool for the rows this pass adds.
    pool: dict[tuple[int, int, int, int], list[int]] = {}
    for row in old_rows:
        key = (row.get("npcId"), row.get("x"), row.get("y"), row.get("z"))
        pool.setdefault(key, []).append(row["objectId"])

    allocator = Allocator(used_object_ids(path), start=NEW_ID_FIRST)
    assigned: list[tuple[dict, int | None]] = []
    for row in wanted:
        key = (row["npcId"], row["x"], row["y"], row["z"])
        candidates = pool.get(key) or []
        assigned.append((row, allocator.keep(candidates.pop(0)) if candidates else None))

    spare = sorted(object_id for ids in pool.values() for object_id in ids)
    rows: list[dict] = []
    for row, object_id in assigned:
        if object_id is None:
            object_id = allocator.reuse(spare) or allocator.take()
        rows.append({"objectId": object_id, **row})

    rows.sort(key=lambda row: row["objectId"])
    payload = {"mapId": MAP_ID, "enabled": True, "comment": COMMENT, "npcs": rows}

    changes: list[str] = []
    old_keys = {(row.get("npcId"), row.get("x"), row.get("y"), row.get("z")) for row in old_rows}
    new_keys = {(row["npcId"], row["x"], row["y"], row["z"]) for row in rows}
    added = len(new_keys - old_keys)
    removed = len(old_keys - new_keys)
    if added or removed:
        changes.append(f"rows {len(old_rows)} -> {len(rows)} (+{added} / -{removed})")
    if current.get("comment") != COMMENT:
        changes.append("comment refreshed")
    return payload, changes


def main() -> None:
    check_only = "--check" in sys.argv[1:]
    path = SPAWNS / f"{MAP_ID}.json"
    payload, changes = build(path)
    text = json.dumps(payload, indent=2) + "\n"
    current = path.read_text(encoding="utf-8") if path.is_file() else ""

    if check_only:
        if text != current:
            print(f"{path.relative_to(ROOT)} drifts from Mobius InterludeSpawns.xml:")
            for change in changes:
                print("  " + change)
            raise SystemExit(1)
        print(f"{path.relative_to(ROOT)} matches Mobius: {len(payload['npcs'])} rows")
        return

    if text != current:
        path.write_text(text, encoding="utf-8")
    for change in changes:
        print(change)
    print(f"{MAP_ID} npcs={len(payload['npcs'])} from {spawn_xml().name}")


if __name__ == "__main__":
    main()
