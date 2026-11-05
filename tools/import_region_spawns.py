#!/usr/bin/env python3
"""Convert Mobius CT 0 Interlude region spawn XML into Fineage spawn JSON.

Town and village NPC files already live in data/spawns and are skipped.
Territory rows become one fixed spawn per count, spread inside the polygon.
Z is the territory minZ (this server has no geodata). Fixed x/y/z rows are copied.
Raid bosses stay on the boss schedule and are not duplicated here.

Usage:
  python3 tools/import_region_spawns.py /path/to/L2J_Mobius_CT_0_Interlude
"""

from __future__ import annotations

import json
import random
import sys
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path

# Town object ids already occupy 60000–61529. Bosses occupy 80001 upward
# (BossCatalog assigns 80001 + index). Leave that band free.
MONSTER_ID_FIRST = 63_000
BOSS_ID_FIRST = 80_001
BOSS_ID_EXCLUSIVE_END = 82_000
OBJECT_ID_EXCLUSIVE_END = 100_000

# Already transcribed by tools/import_npc_templates.py.
SKIP_RELATIVE = {
    "TalkingIsland/TalkingIslandNPCs.xml",
    "Aden/AdenNPCs.xml",
    "DarkElfTerritory/DarkElfVillageNPCs.xml",
    "Dion/DionNPCs.xml",
    "DwarvenTerritory/DwarvenVillageNPCs.xml",
    "ElvenTerritory/ElvenVillageNPCs.xml",
    "Dion/FloranVillageNPCs.xml",
    "Giran/GiranNPCs.xml",
    "Gludin/GludinVillageNPCs.xml",
    "Gludio/GludioNPCs.xml",
    "Goddard/GoddardNPCs.xml",
    "Innadril/HeineNPCs.xml",
    "Hunters/HuntersNPCs.xml",
    "OrcTerritory/OrcVillageNPCs.xml",
    "Oren/OrenNPCs.xml",
    "Rune/RuneNPCs.xml",
    "Schuttgart/SchuttgartNPCs.xml",
}

MAP_ID_OVERRIDE = {
    "TalkingIsland/TalkingIslandMonsters.xml": "ti_monsters",
    "TalkingIsland/TalkingIslandQuestMonsters.xml": "ti_quest_monsters",
    "Others/Gremlins.xml": "training_hall_gremlins",
}

FOLDER_PREFIX = {
    "TalkingIsland": "ti",
    "DarkElfTerritory": "dark_elf",
    "ElvenTerritory": "elven",
    "DwarvenTerritory": "dwarven",
    "OrcTerritory": "orc",
    "Innadril": "innadril",
    "SevenSigns": "seven_signs",
    "Catacombs": "catacombs",
    "Castles": "castles",
    "Others": "others",
}


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def snake(name: str) -> str:
    out: list[str] = []
    for index, ch in enumerate(name):
        if ch.isupper() and index > 0 and not name[index - 1].isupper():
            out.append("_")
        out.append(ch.lower())
    text = "".join(out)
    while "__" in text:
        text = text.replace("__", "_")
    return text.strip("_")


def map_id_for(relative: str) -> str:
    if relative in MAP_ID_OVERRIDE:
        return MAP_ID_OVERRIDE[relative]
    folder, file_name = relative.split("/", 1)
    stem = snake(file_name[: -len(".xml")])
    prefix = FOLDER_PREFIX.get(folder, snake(folder))
    if stem == prefix or stem.startswith(prefix + "_"):
        return stem
    return f"{prefix}_{stem}"


def signed_area(poly: list[tuple[int, int]]) -> float:
    area = 0.0
    for index, (x1, y1) in enumerate(poly):
        x2, y2 = poly[(index + 1) % len(poly)]
        area += x1 * y2 - x2 * y1
    return area / 2.0


def ensure_ccw(poly: list[tuple[int, int]]) -> list[tuple[int, int]]:
    if signed_area(poly) < 0:
        return list(reversed(poly))
    return list(poly)


def cross(a: tuple[int, int], b: tuple[int, int], c: tuple[int, int]) -> int:
    return (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0])


def point_in_tri(p: tuple[int, int], a: tuple[int, int], b: tuple[int, int], c: tuple[int, int]) -> bool:
    # Strictly inside. Points on an edge are not inside, so shared edges stay valid.
    c1 = cross(a, b, p)
    c2 = cross(b, c, p)
    c3 = cross(c, a, p)
    return (c1 > 0 and c2 > 0 and c3 > 0) or (c1 < 0 and c2 < 0 and c3 < 0)


def is_convex_poly(poly: list[tuple[int, int]]) -> bool:
    turns = [cross(poly[i - 1], poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly))]
    return all(turn > 0 for turn in turns) or all(turn < 0 for turn in turns)


def triangulate(poly: list[tuple[int, int]]) -> list[tuple[tuple[int, int], tuple[int, int], tuple[int, int]]]:
    poly = ensure_ccw(dedupe_ring(poly))
    if len(poly) < 3:
        return []
    if is_convex_poly(poly):
        return [(poly[0], poly[i], poly[i + 1]) for i in range(1, len(poly) - 1)]

    indices = list(range(len(poly)))
    tris: list[tuple[tuple[int, int], tuple[int, int], tuple[int, int]]] = []
    guard = 0
    while len(indices) > 3 and guard < len(poly) * len(poly):
        guard += 1
        count = len(indices)
        clipped = False
        for i in range(count):
            prev_i = indices[(i - 1) % count]
            cur_i = indices[i]
            next_i = indices[(i + 1) % count]
            a, b, c = poly[prev_i], poly[cur_i], poly[next_i]
            if cross(a, b, c) <= 0:
                continue
            if any(
                point_in_tri(poly[other], a, b, c)
                for other in indices
                if other not in (prev_i, cur_i, next_i)
            ):
                continue
            tris.append((a, b, c))
            del indices[i]
            clipped = True
            break
        if not clipped:
            break
    if len(indices) == 3:
        tris.append((poly[indices[0]], poly[indices[1]], poly[indices[2]]))
    return [tri for tri in tris if abs(cross(tri[0], tri[1], tri[2])) > 0]


def dedupe_ring(poly: list[tuple[int, int]]) -> list[tuple[int, int]]:
    cleaned: list[tuple[int, int]] = []
    for point in poly:
        if cleaned and cleaned[-1] == point:
            continue
        cleaned.append(point)
    if len(cleaned) > 1 and cleaned[0] == cleaned[-1]:
        cleaned.pop()
    return cleaned


def point_in_poly(x: int, y: int, poly: list[tuple[int, int]]) -> bool:
    inside = False
    j = len(poly) - 1
    for i, (xi, yi) in enumerate(poly):
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            denom = yj - yi
            if denom != 0:
                x_cross = (xj - xi) * (y - yi) / denom + xi
                if x < x_cross:
                    inside = not inside
        j = i
    return inside


def sample_point(tris: list[tuple[tuple[int, int], tuple[int, int], tuple[int, int]]], weights: list[float], rng: random.Random) -> tuple[int, int]:
    pick = rng.random() * weights[-1]
    index = 0
    while index < len(weights) - 1 and weights[index] < pick:
        index += 1
    a, b, c = tris[index]
    r1 = rng.random()
    r2 = rng.random()
    if r1 + r2 > 1:
        r1 = 1 - r1
        r2 = 1 - r2
    x = a[0] + r1 * (b[0] - a[0]) + r2 * (c[0] - a[0])
    y = a[1] + r1 * (b[1] - a[1]) + r2 * (c[1] - a[1])
    return int(round(x)), int(round(y))


def place_in_territory(
    poly: list[tuple[int, int]],
    banned: list[list[tuple[int, int]]],
    count: int,
    seed_text: str,
    occupied: list[tuple[int, int]],
) -> list[tuple[int, int]]:
    tris = triangulate(poly)
    if not tris:
        raise SystemExit(f"could not triangulate territory for {seed_text}")
    weights: list[float] = []
    total = 0.0
    for tri in tris:
        total += abs(cross(tri[0], tri[1], tri[2]))
        weights.append(total)
    points: list[tuple[int, int]] = []
    for min_dist in (160, 80, 40, 0):
        rng = random.Random(zlib.crc32(seed_text.encode()) & 0xFFFFFFFF)
        points = []
        attempts = 0
        limit = max(count * 100, 400)
        while len(points) < count and attempts < limit:
            attempts += 1
            point = sample_point(tris, weights, rng)
            if not point_in_poly(point[0], point[1], poly):
                continue
            if any(point_in_poly(point[0], point[1], ban) for ban in banned):
                continue
            if min_dist and any((point[0] - other[0]) ** 2 + (point[1] - other[1]) ** 2 < min_dist * min_dist for other in occupied):
                continue
            if min_dist and any((point[0] - other[0]) ** 2 + (point[1] - other[1]) ** 2 < min_dist * min_dist for other in points):
                continue
            points.append(point)
        if len(points) == count:
            occupied.extend(points)
            return points
    if len(points) != count:
        raise SystemExit(f"{seed_text} placed {len(points)} of {count}")
    occupied.extend(points)
    return points


def load_id_set(path: Path, key: str) -> set[int]:
    if not path.is_file():
        return set()
    data = json.loads(path.read_text(encoding="utf-8"))
    rows = data.get(key) or []
    return {int(row["npcId"]) for row in rows if "npcId" in row}


def load_used_object_ids(spawns_dir: Path) -> set[int]:
    used: set[int] = set()
    for path in spawns_dir.glob("*.json"):
        data = json.loads(path.read_text(encoding="utf-8"))
        for row in data.get("npcs") or []:
            if "objectId" in row:
                used.add(int(row["objectId"]))
    return used


class IdAllocator:
    def __init__(self, used: set[int]) -> None:
        self.used = used
        self.next_id = MONSTER_ID_FIRST

    def take(self) -> int:
        while self.next_id < OBJECT_ID_EXCLUSIVE_END:
            if BOSS_ID_FIRST <= self.next_id < BOSS_ID_EXCLUSIVE_END:
                self.next_id = BOSS_ID_EXCLUSIVE_END
                continue
            candidate = self.next_id
            self.next_id += 1
            if candidate not in self.used:
                self.used.add(candidate)
                return candidate
        raise SystemExit("NPC object id range is exhausted")


def child_nodes(spawn: ET.Element, name: str) -> list[ET.Element]:
    return [node for node in spawn if local(node.tag) == name]


def read_ring(node: ET.Element) -> list[tuple[int, int]]:
    return [(int(child.attrib["x"]), int(child.attrib["y"])) for child in node if local(child.tag) == "node"]


def convert(root: Path, repo: Path) -> None:
    spawns_dir = root / "dist" / "game" / "data" / "spawns"
    if not spawns_dir.is_dir():
        raise SystemExit(f"{root} is not an L2J_Mobius_CT_0_Interlude datapack root")

    templates = load_id_set(repo / "data" / "npcs.json", "npcs")
    names = {}
    npc_doc = json.loads((repo / "data" / "npcs.json").read_text(encoding="utf-8"))
    for template in npc_doc["npcs"]:
        names[int(template["npcId"])] = (template.get("name") or "").strip()
    bosses = load_id_set(repo / "data" / "raidbosses.json", "bosses")
    out_dir = repo / "data" / "spawns"
    allocator = IdAllocator(load_used_object_ids(out_dir))

    written: list[tuple[str, str, int]] = []
    skipped_bosses = 0
    skipped_missing: dict[int, int] = {}
    skipped_empty_name: dict[int, int] = {}
    deferred: list[str] = []

    files = sorted(spawns_dir.rglob("*.xml"))
    for path in files:
        relative = path.relative_to(spawns_dir).as_posix()
        if relative in SKIP_RELATIVE:
            continue
        map_id = map_id_for(relative)
        destination = out_dir / f"{map_id}.json"
        if destination.exists() and map_id not in MAP_ID_OVERRIDE and relative not in MAP_ID_OVERRIDE:
            # Never overwrite a hand-maintained town file if the id collides.
            existing = json.loads(destination.read_text(encoding="utf-8"))
            if existing.get("mapId") == map_id and "Territory rows" not in (existing.get("comment") or ""):
                deferred.append(f"{relative} -> {map_id} (map id already used)")
                continue

        rows: list[dict] = []
        tree = ET.parse(path).getroot()
        for spawn_index, spawn in enumerate(child_nodes(tree, "spawn")):
            zone = spawn.attrib.get("zone") or spawn.attrib.get("name") or f"spawn{spawn_index}"
            territories = child_nodes(spawn, "territory")
            banned = [read_ring(node) for node in child_nodes(spawn, "banned_territory")]
            occupied: list[tuple[int, int]] = []
            for npc_index, npc in enumerate(child_nodes(spawn, "npc")):
                npc_id = int(npc.attrib["id"])
                count = int(npc.attrib.get("count", "1"))
                if count < 1:
                    raise SystemExit(f"{relative} npc {npc_id} has count {count}")
                if npc_id in bosses:
                    skipped_bosses += count
                    continue
                if npc_id not in templates or not names.get(npc_id):
                    bucket = skipped_missing if npc_id not in templates else skipped_empty_name
                    bucket[npc_id] = bucket.get(npc_id, 0) + count
                    continue
                delay = int(npc.attrib.get("respawnDelay", "0"))
                jitter = int(npc.attrib.get("respawnRandom", "0"))
                if "x" in npc.attrib:
                    x = int(npc.attrib["x"])
                    y = int(npc.attrib["y"])
                    z = int(npc.attrib["z"])
                    heading = int(npc.attrib.get("heading", "0"))
                    for _ in range(count):
                        rows.append(spawn_row(allocator.take(), npc_id, x, y, z, heading, delay, jitter))
                    continue
                if not territories:
                    raise SystemExit(f"{relative} zone {zone} npc {npc_id} has no coordinates")
                territory = territories[0]
                poly = read_ring(territory)
                z = int(territory.attrib["minZ"])
                placed = place_in_territory(
                    poly,
                    banned,
                    count,
                    f"{relative}|{zone}|{npc_id}|{npc_index}",
                    occupied,
                )
                for x, y in placed:
                    rows.append(spawn_row(allocator.take(), npc_id, x, y, z, 0, delay, jitter))

        if not rows:
            deferred.append(relative)
            continue

        payload = {
            "mapId": map_id,
            "enabled": True,
            "comment": (
                f"Mobius CT 0 Interlude spawns/{relative}. "
                "Fixed rows keep Mobius x/y/z/heading. "
                "Territory rows are one spawn per count, placed inside the polygon at minZ "
                "(no geodata). Day and night rows are both present. "
                "Raid bosses are left to data/raidbosses.json. "
                "Name, title, type, attackable, hp, and speeds come from data/npcs.json."
            ),
            "npcs": rows,
        }
        destination.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        written.append((map_id, relative, len(rows)))

    print(f"maps {len(written)} spawns {sum(count for _, _, count in written)} next_object_id {allocator.next_id}")
    print(f"skipped_raid_boss_instances {skipped_bosses}")
    if skipped_missing:
        print("skipped_missing_templates " + ",".join(f"{npc_id}:{count}" for npc_id, count in sorted(skipped_missing.items())))
    if skipped_empty_name:
        print("skipped_empty_name " + ",".join(f"{npc_id}:{count}" for npc_id, count in sorted(skipped_empty_name.items())))
    print("deferred " + ("; ".join(deferred) if deferred else "none"))
    for map_id, relative, count in written:
        print(f"spawn {map_id} count={count} source={relative}")


def spawn_row(object_id: int, npc_id: int, x: int, y: int, z: int, heading: int, delay: int, jitter: int) -> dict:
    return {
        "objectId": object_id,
        "npcId": npc_id,
        "x": x,
        "y": y,
        "z": z,
        "heading": heading,
        "respawnDelaySeconds": delay,
        "respawnRandomSeconds": jitter,
    }


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: import_region_spawns.py /path/to/L2J_Mobius_CT_0_Interlude")
    repo = Path(__file__).resolve().parents[1]
    convert(Path(sys.argv[1]), repo)


if __name__ == "__main__":
    main()
