#!/usr/bin/env python3
"""Keep the Elven Village newbie-guide NPCs pinned to their Mobius spawn rows.

Mobius CT 0 Interlude places the elven newbie guides in the global
`dist/game/data/spawns/InterludeSpawns.xml`, not in a per-territory NPC file, so a
regeneration of `data/spawns/elven_village.json` can silently drop them. This script
re-reads the vendored Mobius XML and makes the rows in the elven map that the local Net
set loads authoritative:

  30370 Nerupa        (Q160 Nerupa's Request, elven newbie guide)
  30400 Newbie Helper (support magic / newbie gift)

Idempotent: existing rows keep their objectId and are corrected in place when the x/y/z,
heading or respawn drifted; missing rows are inserted with a free town-band objectId.
Coordinates are never hand-picked: they come from the Mobius XML.

Source order: ref/mobius-ct0-interlude/... (vendored snapshot), else $MOBIUS_SRC, else the
L2J_Mobius_CT_0_Interlude checkout under ~/Downloads.

Usage:
  python3 scripts/gen_elven_newbie_guides.py [--check]
  # --check exits 1 when data/spawns/elven_village.json does not match Mobius (CI-safe)
"""

from __future__ import annotations

import json
import math
import os
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
SPAWNS = ROOT / "data" / "spawns"
MAP_ID = "elven_village"
SPAWN_XML_REL = pathlib.Path("dist") / "game" / "data" / "spawns" / "InterludeSpawns.xml"
VENDORED_REL = pathlib.Path("ref") / "mobius-ct0-interlude" / "data" / "spawns" / "InterludeSpawns.xml"

# npcId -> short reason, only for the comment/messages.
GUIDES = {
    30370: "Nerupa (Q160 Nerupa's Request, elven newbie guide)",
    30400: "Newbie Helper (support magic + newbie gift)",
}

TOWN_ID_FIRST = 60_000
TOWN_ID_END = 100_000


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


def mobius_rows(path: pathlib.Path) -> dict[int, dict]:
    """Rows for the guide NPCs, in file order, straight from the Mobius datapack."""
    root = ET.parse(path).getroot()
    found: dict[int, list[dict]] = {}
    for spawn in root:
        if local(spawn.tag) != "spawn":
            continue
        for node in spawn:
            if local(node.tag) != "npc":
                continue
            npc_id = int(node.attrib["id"])
            if npc_id not in GUIDES:
                continue
            found.setdefault(npc_id, []).append(
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
    missing = [npc_id for npc_id in GUIDES if npc_id not in found]
    if missing:
        raise SystemExit(f"{path} has no spawn row for npc {missing}")
    duplicated = {npc_id: len(rows) for npc_id, rows in found.items() if len(rows) > 1}
    if duplicated:
        raise SystemExit(f"{path} spawns the newbie guides more than once: {duplicated}")
    return {npc_id: rows[0] for npc_id, rows in found.items()}


def used_object_ids() -> set[int]:
    used: set[int] = set()
    for path in SPAWNS.glob("*.json"):
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        for row in payload.get("npcs") or []:
            object_id = row.get("objectId")
            if isinstance(object_id, int) and object_id > 0:
                used.add(object_id)
    return used


def next_object_id(used: set[int]) -> int:
    candidate = max([TOWN_ID_FIRST, *[value + 1 for value in used if TOWN_ID_FIRST <= value < TOWN_ID_END]] or [TOWN_ID_FIRST])
    while candidate in used:
        candidate += 1
    if candidate >= TOWN_ID_END:
        raise SystemExit("town object id range exhausted")
    return candidate


def within_sight(left: dict, right: dict, metres: int = 900) -> bool:
    return math.dist((left["x"], left["y"], left["z"]), (right["x"], right["y"], right["z"])) <= metres


def main() -> None:
    check_only = "--check" in sys.argv[1:]
    source = spawn_xml()
    wanted = mobius_rows(source)

    path = SPAWNS / f"{MAP_ID}.json"
    payload = json.loads(path.read_text(encoding="utf-8"))
    rows: list[dict] = payload["npcs"]
    by_npc = {row["npcId"]: row for row in rows if row["npcId"] in GUIDES}
    used = used_object_ids()

    changes: list[str] = []
    for npc_id in sorted(GUIDES):
        want = wanted[npc_id]
        have = by_npc.get(npc_id)
        if have is None:
            object_id = next_object_id(used)
            used.add(object_id)
            row = {"objectId": object_id, **want}
            rows.append(row)
            changes.append(f"insert npc {npc_id} object {object_id} at {want['x']},{want['y']},{want['z']}")
            continue

        drift = {
            key: (have.get(key), want[key])
            for key in ("x", "y", "z", "heading", "respawnDelaySeconds", "respawnRandomSeconds")
            if have.get(key) != want[key]
        }
        if drift:
            have.update(drift_pairs(want))
            changes.append(f"fix npc {npc_id} object {have['objectId']}: " + ", ".join(f"{k} {a}->{b}" for k, (a, b) in drift.items()))

    rows.sort(key=lambda row: row["objectId"])
    payload["npcs"] = rows

    nerupa = next(row for row in rows if row["npcId"] == 30370)
    helper = next(row for row in rows if row["npcId"] == 30400)
    if not within_sight(nerupa, helper):
        raise SystemExit(
            f"Nerupa {nerupa['x']},{nerupa['y']},{nerupa['z']} and the Newbie Helper "
            f"{helper['x']},{helper['y']},{helper['z']} are not within sight in Mobius data; "
            "the local elven set would hide one of them."
        )

    text = json.dumps(payload, indent=2) + "\n"
    current = path.read_text(encoding="utf-8")

    if check_only:
        if text != current:
            print(f"{path.relative_to(ROOT)} drifts from {source.name}:")
            for change in changes:
                print("  " + change)
            raise SystemExit(1)
        print(f"{path.relative_to(ROOT)} matches {source.name}: Nerupa {nerupa['x']},{nerupa['y']},{nerupa['z']} heading {nerupa['heading']}")
        return

    if text != current:
        path.write_text(text, encoding="utf-8")
    for change in changes:
        print(change)
    print(
        f"{MAP_ID} npcs={len(rows)} | Nerupa 30370 object {nerupa['objectId']} "
        f"({nerupa['x']}, {nerupa['y']}, {nerupa['z']}) heading {nerupa['heading']} respawn {nerupa['respawnDelaySeconds']}s "
        f"| Newbie Helper 30400 ({helper['x']}, {helper['y']}, {helper['z']})"
    )


def drift_pairs(want: dict) -> dict:
    return {key: want[key] for key in ("x", "y", "z", "heading", "respawnDelaySeconds", "respawnRandomSeconds")}


if __name__ == "__main__":
    main()
