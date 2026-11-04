#!/usr/bin/env python3
"""Transcribe L2J Mobius CT 0 Interlude NPC templates and town spawns.

Reads the Mobius datapack (not copied into this repo) and writes
data/npcs.json plus data/spawns/<town>.json. Values are transcribed.
This script does not copy GPL source.

Mobius references (L2J_Mobius_CT_0_Interlude):
  dist/game/data/stats/npcs/*.xml
    10|    NpcData parses <status attackable> (default true) and <ai isAggressive>
    (default true). See NpcTemplate.set.
    Combat stats come from the same files (NpcData.parseStats / parseAI):
      <stats attack physical magical random critical accuracy attackSpeed range type>
      <stats defence physical magical evasion shield shieldRate>
      <stats dex> and <ai aggroRange clanHelpRange>
      <ai clanList><clan>TEXT</clan><ignoreNpcId>ID</ignoreNpcId></ai>
    `clan` and `ignoreNpcId` are siblings inside <clanList>; both are emitted,
    as separate keys (`clans`, `ignoreNpcIds`), never mixed into one list.
  dist/game/data/spawns/<territory>/*NPCs.xml and *VillageNPCs.xml
    SpawnData reads npc id, x, y, z, heading, respawnDelay, respawnRandom.
    Spawn.setRespawnDelay treats those numbers as seconds.

Combat fields are REQUIRED output. A run that emits zero `pAtk > 0` rows has
lost the combat transcription and aborts before touching data/npcs.json
(see check_combat_coverage) — regenerate with the combat block intact.

Usage:
  python3 tools/import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude
  python3 tools/import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude --check

`--check` regenerates templates in memory only (no data/ writes, no spawn tree
needed) and compares them with the committed data/npcs.json. It exits non-zero
when any combat field drifts, which is the reproducibility contract for the
zone's damage/aggro data.
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


def attribute(node: ET.Element | None, key: str) -> str | None:
    if node is None:
        return None
    value = node.attrib.get(key)
    return None if value is None or value.strip() == "" else value


def combat_float(node: ET.Element | None, key: str, digits: int, fallback: float = 0.0) -> float:
    """P.Atk/P.Def style stat: always a float, rounded for a stable JSON diff.

    data/npcs.json stores pAtk/mAtk/pDef/mDef with 3 decimals and accuracy/evasion
    with 2. Keeping them floats matters: 0 must print as 0.0, not 0. Values are
    also clamped to the 3-decimal form because the zone reads them from JSON.
    """
    text = attribute(node, key)
    if text is None:
        return round(fallback, digits)
    value = float(text)
    if not math.isfinite(value):
        return round(fallback, digits)
    return round(value, digits)


def combat_int(node: ET.Element | None, key: str, fallback: int = 0) -> int:
    text = attribute(node, key)
    if text is None:
        return fallback
    value = float(text)
    if not math.isfinite(value):
        return fallback
    return int(math.floor(value + 0.5))


def clan_list(ai: ET.Element | None) -> tuple[list[str], list[int]]:
    """<ai><clanList> holds <clan> names and <ignoreNpcId> ids as siblings."""
    clans: list[str] = []
    ignores: list[int] = []
    node = child(ai, "clanList") if ai is not None else None
    if node is None:
        return clans, ignores
    for item in node:
        text = (item.text or "").strip()
        if not text:
            continue
        tag = local(item.tag)
        if tag == "clan":
            clans.append(text)
        elif tag == "ignoreNpcId":
            ignores.append(int(text))
    return clans, ignores


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
            attack = child(stats, "attack") if stats is not None else None
            defence = child(stats, "defence") if stats is not None else None
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
            acquire = child(node, "acquire")
            exp = int(float(acquire.attrib.get("exp", "0"))) if acquire is not None else 0
            sp = int(float(acquire.attrib.get("sp", "0"))) if acquire is not None else 0
            # Mobius allows an empty name (doors, effect points). Town spawns do not use those.
            name = (node.attrib.get("name") or "").strip()
            clans, ignore_npc_ids = clan_list(ai)

            row: dict = {
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
                "exp": exp,
                "sp": sp,
                # Combat stats, key order matches the committed data/npcs.json so
                # a regeneration stays a byte-identical diff. Transcribed values.
                "pAtk": combat_float(attack, "physical", 3),
                "pDef": combat_float(defence, "physical", 3),
                "attackSpeed": combat_int(attack, "attackSpeed"),
                "attackRange": combat_int(attack, "range"),
                "accuracy": combat_float(attack, "accuracy", 2),
                "evasion": combat_float(defence, "evasion", 2),
                "critical": combat_int(attack, "critical"),
                "randomDamage": combat_int(attack, "random"),
                "dex": combat_int(stats, "dex"),
            }
            # Shields only exist when <stats defence> carries `shield`; `shieldRate`
            # defaults to 0 there (the attribute is absent for 25 of 277 NPCs).
            if attribute(defence, "shield") is not None:
                row["shieldDef"] = combat_int(defence, "shield")
                row["shieldRate"] = combat_int(defence, "shieldRate")
            row["mAtk"] = combat_float(attack, "magical", 3)
            row["mDef"] = combat_float(defence, "magical", 3)
            if attribute(ai, "aggroRange") is not None:
                row["aggroRange"] = combat_int(ai, "aggroRange")
            if attribute(ai, "clanHelpRange") is not None:
                row["clanHelpRange"] = combat_int(ai, "clanHelpRange")
            if clans:
                row["clans"] = clans
            if ignore_npc_ids:
                row["ignoreNpcIds"] = ignore_npc_ids
            templates[npc_id] = row

    return [templates[npc_id] for npc_id in sorted(templates)]


# Fields the zone reads for combat parity. They are the ones a "templates only"
# regression would silently erase, so --check compares just these.
COMBAT_FIELDS: tuple[str, ...] = (
    "pAtk",
    "mAtk",
    "pDef",
    "mDef",
    "critical",
    "accuracy",
    "randomDamage",
    "attackSpeed",
    "attackRange",
    "dex",
    "aggroRange",
    "clanHelpRange",
    "clans",
    "ignoreNpcIds",
    "evasion",
    "shieldDef",
    "shieldRate",
)


def check_combat_coverage(templates: list[dict]) -> None:
    """Refuse to overwrite data/npcs.json with combat stats stripped.

    The zone reads pAtk/pDef/aggroRange/clans from here; an importer that silently
    drops <stats attack>/<stats defence> would zero out every mob's damage. This
    guards the shape, not the values: a real datapack has thousands of rows with
    pAtk > 0, so zero means the transcription broke.
    """
    if not templates:
        raise SystemExit("no npc templates parsed")
    with_attack = sum(1 for row in templates if row["pAtk"] > 0)
    if with_attack == 0:
        raise SystemExit(
            "refusing to write npcs.json: no NPC has pAtk > 0 — "
            "combat stats (stats/attack, stats/defence) are not being read"
        )
    if not any("aggroRange" in row for row in templates):
        raise SystemExit("refusing to write npcs.json: no NPC has aggroRange (ai/aggroRange not read)")
    if not any(row.get("clans") for row in templates):
        raise SystemExit("refusing to write npcs.json: no NPC has clans (ai/clanList/clan not read)")


def compare_with_committed(templates: list[dict], committed: dict[int, dict]) -> list[str]:
    """Field-level diff of the combat block against the committed data/npcs.json.

    Floats are compared with a tolerance of 0.0005 because pAtk/mAtk/pDef/mDef are
    stored with 3 decimals while the datapack carries more; everything else must
    match exactly. Returns human-readable mismatch lines (empty means reproducible).
    """
    problems: list[str] = []
    for row in templates:
        npc_id = row["npcId"]
        old = committed.get(npc_id)
        if old is None:
            problems.append(f"npc {npc_id}: missing from data/npcs.json")
            continue
        for field in COMBAT_FIELDS:
            want = old.get(field, "<absent>")
            got = row.get(field, "<absent>")
            if isinstance(want, list) or isinstance(got, list):
                if want != got:
                    problems.append(f"npc {npc_id}: {field} {want!r} != {got!r}")
            elif isinstance(want, (int, float)) and isinstance(got, (int, float)):
                if abs(float(want) - float(got)) > 0.0005:
                    problems.append(f"npc {npc_id}: {field} {want!r} != {got!r}")
            elif want != got:
                problems.append(f"npc {npc_id}: {field} {want!r} != {got!r}")
    for npc_id in sorted(set(committed) - {row["npcId"] for row in templates}):
        problems.append(f"npc {npc_id}: present in data/npcs.json but not in the datapack")
    return problems


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


def datapack_dir(root: Path, relative: str) -> Path:
    """Locate a datapack folder in either the release or the sparse-clone layout.

    A full L2J_Mobius checkout keeps files under `dist/game/data/...`; the local
    ref/ sparse clone keeps the same tree rooted at `data/...`.
    """
    for candidate in (root / "dist" / "game" / "data" / relative, root / "data" / relative):
        if candidate.is_dir():
            return candidate
    return root / "dist" / "game" / "data" / relative


def main() -> None:
    argv = sys.argv[1:]
    check_only = "--check" in argv
    if check_only:
        argv = [a for a in argv if a != "--check"]
    if len(argv) != 1:
        raise SystemExit(
            "usage: import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude [--check]"
        )
    root = Path(argv[0])
    npc_dir = datapack_dir(root, "stats/npcs")
    spawns_dir = datapack_dir(root, "spawns")
    if not npc_dir.is_dir():
        raise SystemExit(f"{root} has no stats/npcs (looked for dist/game/data and data)")
    if not check_only and not spawns_dir.is_dir():
        raise SystemExit(f"{root} is not an L2J_Mobius_CT_0_Interlude datapack root")

    repo = Path(__file__).resolve().parents[1]
    templates = load_templates(npc_dir)
    check_combat_coverage(templates)

    if check_only:
        path = repo / "data" / "npcs.json"
        committed = {row["npcId"]: row for row in json.loads(path.read_text(encoding="utf-8"))["npcs"]}
        problems = compare_with_committed(templates, committed)
        with_attack = sum(1 for row in templates if row["pAtk"] > 0)
        print(f"templates {len(templates)} pAtk>0 rows {with_attack}")
        if problems:
            print(f"COMBAT DRIFT {len(problems)}")
            for line in problems:
                print("  " + line)
            raise SystemExit(1)
        print("combat fields reproducible: 0 mismatches")
        return

    write_templates(repo, templates)
    by_id = {row["npcId"]: row for row in templates}
    summary = write_towns(repo, by_id, spawns_dir)
    print(f"templates {len(templates)}")
    for map_id, count, enabled in summary:
        print(f"spawn {map_id} count={count} enabled={str(enabled).lower()}")


if __name__ == "__main__":
    main()
