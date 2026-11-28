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
    `aggroRange` is always emitted with the resolved Mobius value. NpcData sets
    it from `<ai aggroRange>` and NpcTemplate falls back to StatSet 0 when the
    attribute (or the whole `<ai>` node) is absent, so `0` is an explicit,
    non-proactive retail value rather than missing metadata.
  dist/game/data/spawns/<territory>/*NPCs.xml and *VillageNPCs.xml
    SpawnData reads npc id, x, y, z, heading, respawnDelay, respawnRandom.
    Spawn.setRespawnDelay treats those numbers as seconds.

Combat fields are REQUIRED output. A run that emits zero `pAtk > 0` rows has
lost the combat transcription and aborts before touching data/npcs.json
(see check_combat_coverage) — regenerate with the combat block intact.

Usage:
  python3 tools/import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude
  python3 tools/import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude --check
  python3 tools/import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude --write-towns

Source pinning: the datapack must be the CT 0 Interlude chronicle at
PINNED_MOBIUS_COMMIT (or a later commit whose stats/npcs tree is identical).
Any other chronicle (CT_2.4_Epilogue, CT_2.6_HighFive, ...) is refused, because a
Nov 2026 regeneration silently picked up Epilogue/HighFive NPC levels/HP/exp.
`--allow-unpinned` skips only the commit check (never the chronicle check).

Templates only by default: data/spawns/*.json is NOT rewritten unless
`--write-towns` is given (town files carry hand-added service rows).
Rows listed in CUSTOM_NPC_IDS are not in the Mobius retail datapack; they are
copied verbatim from the committed data/npcs.json, never dropped.

The full NPC pipeline (templates + race/weapon/shot/AI/trait passes) is
tools/regen_npcs.py.

`--check` regenerates templates in memory only (no data/ writes, no spawn tree
needed) and compares them with the committed data/npcs.json. It exits non-zero
when any combat field drifts, which is the reproducibility contract for the
zone's damage/aggro data.
"""

from __future__ import annotations

import json
import math
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Mobius CT 0 Interlude commit the committed data/npcs.json is transcribed from
# (gitlab.com/MobiusDevelopment/L2J_Mobius, master 2026-11-16).
PINNED_MOBIUS_COMMIT = "e4b90cdf6095576aa86bfe3d6886395df1fc7672"
CHRONICLE_DIR = "L2J_Mobius_CT_0_Interlude"
NPC_XML_RELATIVE = f"{CHRONICLE_DIR}/dist/game/data/stats/npcs"

# Fineage/Mobius custom NPCs that are not in the retail stats/npcs/*.xml files.
# They are preserved verbatim from the committed data/npcs.json.
CUSTOM_NPC_IDS: dict[int, str] = {
    50007: "Andromeda, Wedding Manager (Mobius custom/custom.xml, ported in f4d127fc)",
}

# Documented per-id deviations from the pinned CT 0 XML. Only for rows where the CT 0
# datapack is clearly a placeholder; each entry is listed with its sources in
# docs/npc-data-overrides.md. Never add a row here without that doc entry.
_TINY_BABY_SOURCE = (
    "CT0 XML is a shared placeholder (level 99, hp 253165, mp 9999, P.Atk 4500, P.Def 3000) while its own "
    "exp/sp are the level 65-69 rewards; Interlude databases list the values below "
    "(lineage2scarlet.com/npc/list/<id>, l2sanctum.net/wiki/interlude/monsters/<name>)"
)
NPC_OVERRIDES: dict[int, dict] = {
    16019: {"level": 67, "hp": 2295.48154, "mp": 1251, "hpRegen": 7.5, "mpRegen": 2.7, "pAtk": 624.93199,
            "pDef": 281.55251, "mAtk": 426.74754, "mDef": 206.02791, "randomDamage": 10, "accuracy": 4.75, "men": 25},
    16020: {"level": 67, "hp": 2295.48154, "mp": 1251, "hpRegen": 7.5, "mpRegen": 2.7, "pAtk": 624.93199,
            "pDef": 281.55251, "mAtk": 426.74754, "mDef": 206.02791, "randomDamage": 10, "accuracy": 4.75, "men": 25},
    16021: {"level": 69, "hp": 2395.45533, "mp": 1314, "hpRegen": 7.5, "mpRegen": 2.7, "pAtk": 667.50361,
            "pDef": 291.15063, "mAtk": 455.81844, "mDef": 213.05139, "randomDamage": 10, "accuracy": 4.75, "men": 25},
    16022: {"level": 69, "hp": 2395.45533, "mp": 1314, "hpRegen": 7.5, "mpRegen": 2.7, "pAtk": 667.50361,
            "pDef": 291.15063, "mAtk": 455.81844, "mDef": 213.05139, "randomDamage": 10, "accuracy": 4.75, "men": 25},
    16023: {"level": 65, "hp": 2193.43142, "mp": 1188.8, "hpRegen": 7.5, "mpRegen": 2.7, "pAtk": 582.81759,
            "pDef": 271.89033, "mAtk": 397.98887, "mDef": 198.95754, "randomDamage": 10, "accuracy": 4.75, "men": 25},
}
NPC_OVERRIDE_SOURCES: dict[int, str] = {npc_id: _TINY_BABY_SOURCE for npc_id in range(16019, 16024)}

# Mobius NpcData always reads str/con/int/men from <stats>; Fineage's NpcCatalog
# defaults (40/43/21/10) apply when the key is absent, so only other values are written.
ATTRIBUTE_DEFAULTS: tuple[tuple[str, int], ...] = (("str", 40), ("con", 43), ("int", 21), ("men", 10))

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


def raw_stat(text: str | None, minimum: float = 0.0) -> int | float:
    """Full XML precision (Mobius StatSet keeps the double; the zone folds it like
    NPC_STD_CALCULATOR, truncating P.Atk/P.Def/M.Atk/M.Def to int at fold time)."""
    if text is None or text.strip() == "":
        return whole_or_float_exact(minimum)
    value = float(text)
    if not math.isfinite(value) or value < minimum:
        value = minimum
    return whole_or_float_exact(value)


def whole_or_float_exact(value: float) -> int | float:
    if value == math.trunc(value):
        return int(value)
    return value


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
                "hp": raw_stat(None if vitals is None else vitals.attrib.get("hp"), 1.0),
                "mp": raw_stat(None if vitals is None else vitals.attrib.get("mp"), 0.0),
                # Mobius NpcData <vitals hpRegen/mpRegen>: NPCStatus.doRegeneration adds
                # calcStat(REGENERATE_HP_RATE, max(1, baseHpReg)) every 3000 ms, so a damaged
                # mob heals back instead of staying wounded forever.
                "hpRegen": combat_float(vitals, "hpRegen", 3),
                "mpRegen": combat_float(vitals, "mpRegen", 3),
                "runSpeed": positive_stat(None if run is None else run.attrib.get("ground"), 0.1),
                "walkSpeed": positive_stat(None if walk is None else walk.attrib.get("ground"), 0.1),
                "exp": exp,
                "sp": sp,
                # Combat stats, key order matches the committed data/npcs.json so
                # a regeneration stays a byte-identical diff. Transcribed values.
                "pAtk": float(raw_stat(attribute(attack, "physical"))),
                "pDef": float(raw_stat(attribute(defence, "physical"))),
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
            row["mAtk"] = float(raw_stat(attribute(attack, "magical")))
            row["mDef"] = float(raw_stat(attribute(defence, "magical")))
            for key, default in ATTRIBUTE_DEFAULTS:
                value = combat_int(stats, key, default)
                if value != default:
                    row[key] = value
            # Always emit the resolved Mobius value (missing <ai>/aggroRange -> 0).
            # This keeps explicit non-proactive mobs distinguishable from rows the
            # importer failed to populate; the zone validation rejects the latter.
            row["aggroRange"] = combat_int(ai, "aggroRange")
            if attribute(ai, "clanHelpRange") is not None:
                row["clanHelpRange"] = combat_int(ai, "clanHelpRange")
            if clans:
                row["clans"] = clans
            if ignore_npc_ids:
                row["ignoreNpcIds"] = ignore_npc_ids
            # Mobius <parameters> on EffectPoint signets: how long the signet NPC stays and how often it
            # re-applies its aura. SummonNpc reads despawn_time; the signet AI reads skill_delay.
            parameters = child(node, "parameters")
            if parameters is not None:
                for item in parameters:
                    if local(item.tag) != "param":
                        continue
                    pname = item.attrib.get("name")
                    if pname in ("despawn_time", "skill_delay"):
                        value = int(float(item.attrib.get("value", "0")))
                        if value > 0:
                            row["despawnTimeSeconds" if pname == "despawn_time" else "skillDelaySeconds"] = value
            templates[npc_id] = row

    return [templates[npc_id] for npc_id in sorted(templates)]


# Fields --check compares against the committed data/npcs.json. Identity, level,
# vitals and rewards are included so a wrong-chronicle regeneration (Epilogue /
# HighFive levels, HP, exp) fails the check, not only a stripped combat block.
COMBAT_FIELDS: tuple[str, ...] = (
    "name",
    "title",
    "type",
    "level",
    "hp",
    "mp",
    "exp",
    "sp",
    "attackable",
    "aggressive",
    "collisionRadius",
    "collisionHeight",
    "runSpeed",
    "walkSpeed",
    "str",
    "con",
    "int",
    "men",
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
    "hpRegen",
    "mpRegen",
)


def apply_overrides(templates: list[dict]) -> list[dict]:
    """Apply NPC_OVERRIDES (documented in docs/npc-data-overrides.md) on top of the CT 0 rows."""
    by_id = {row["npcId"]: row for row in templates}
    if set(NPC_OVERRIDES) != set(NPC_OVERRIDE_SOURCES):
        raise SystemExit("every NPC_OVERRIDES row needs an NPC_OVERRIDE_SOURCES entry")
    doc = Path(__file__).resolve().parents[1] / "docs" / "npc-data-overrides.md"
    doc_text = doc.read_text(encoding="utf-8") if doc.is_file() else ""
    undocumented = [i for i in NPC_OVERRIDES if f"| {i} |" not in doc_text]
    if undocumented:
        raise SystemExit(f"NPC_OVERRIDES {undocumented} have no row in {doc}")
    for npc_id, fields in NPC_OVERRIDES.items():
        if npc_id not in by_id:
            raise SystemExit(f"override for npc {npc_id} but it is not in the datapack")
        by_id[npc_id].update(fields)
    return templates


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
    missing_aggro = [row["npcId"] for row in templates if row["aggressive"] and "aggroRange" not in row]
    if missing_aggro:
        raise SystemExit(
            "refusing to write npcs.json: aggressive NPCs without aggroRange: "
            + ", ".join(str(npc_id) for npc_id in missing_aggro[:10])
        )
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
    for npc_id in sorted(set(committed) - {row["npcId"] for row in templates} - set(CUSTOM_NPC_IDS)):
        problems.append(f"npc {npc_id}: present in data/npcs.json but not in the datapack")
    for npc_id in sorted(set(CUSTOM_NPC_IDS) - set(committed)):
        problems.append(f"npc {npc_id}: custom row ({CUSTOM_NPC_IDS[npc_id]}) missing from data/npcs.json")
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


SOURCE_TEXT = (
    f"L2J Mobius CT 0 Interlude dist/game/data/stats/npcs @ {PINNED_MOBIUS_COMMIT[:12]} "
    "(values transcribed, XML not copied)"
)


def with_custom_rows(repo: Path, templates: list[dict]) -> list[dict]:
    """Append CUSTOM_NPC_IDS rows from the committed file (sorted by npcId)."""
    path = repo / "data" / "npcs.json"
    committed = {row["npcId"]: row for row in json.loads(path.read_text(encoding="utf-8"))["npcs"]}
    by_id = {row["npcId"]: row for row in templates}
    for npc_id, why in CUSTOM_NPC_IDS.items():
        if npc_id in by_id:
            raise SystemExit(f"custom npc {npc_id} ({why}) now exists in the datapack; drop it from CUSTOM_NPC_IDS")
        if npc_id not in committed:
            raise SystemExit(f"custom npc {npc_id} ({why}) missing from committed data/npcs.json")
        by_id[npc_id] = committed[npc_id]
    return [by_id[npc_id] for npc_id in sorted(by_id)]


def verify_source(root: Path, npc_dir: Path, allow_unpinned: bool) -> str:
    """Refuse a non-CT0 chronicle; require the pinned commit's stats/npcs tree."""
    resolved = root.resolve()
    if CHRONICLE_DIR not in resolved.parts and resolved.name != CHRONICLE_DIR:
        raise SystemExit(
            f"refusing {root}: not an {CHRONICLE_DIR} datapack root "
            "(Epilogue/HighFive/other chronicles must never feed data/npcs.json)"
        )
    if CHRONICLE_DIR not in npc_dir.resolve().parts:
        raise SystemExit(f"refusing {npc_dir}: stats/npcs is not under {CHRONICLE_DIR}")

    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", "-C", str(npc_dir), *args], capture_output=True, text=True)

    head = git("rev-parse", "HEAD")
    if head.returncode != 0:
        if allow_unpinned:
            return "unpinned (not a git checkout, --allow-unpinned)"
        raise SystemExit(
            f"{npc_dir} is not a git checkout; cannot prove it is Mobius {PINNED_MOBIUS_COMMIT[:12]} "
            "(clone the pinned commit or pass --allow-unpinned)"
        )
    sha = head.stdout.strip()
    if sha == PINNED_MOBIUS_COMMIT:
        return f"pinned {sha[:12]}"
    top = git("rev-parse", "--show-toplevel").stdout.strip()
    rel = npc_dir.resolve().relative_to(Path(top).resolve()).as_posix() if top else NPC_XML_RELATIVE
    same = subprocess.run(
        ["git", "-C", top or str(npc_dir), "diff", "--quiet", PINNED_MOBIUS_COMMIT, sha, "--", rel],
        capture_output=True,
        text=True,
    )
    if same.returncode == 0:
        return f"{sha[:12]} (stats/npcs identical to pinned {PINNED_MOBIUS_COMMIT[:12]})"
    if allow_unpinned:
        return f"unpinned {sha[:12]} (--allow-unpinned)"
    raise SystemExit(
        f"Mobius checkout is at {sha[:12]}, and its stats/npcs differs from pinned "
        f"{PINNED_MOBIUS_COMMIT[:12]} (or the pinned commit is not fetched). Update "
        "PINNED_MOBIUS_COMMIT deliberately, or pass --allow-unpinned."
    )


def write_templates(repo: Path, templates: list[dict]) -> None:
    path = repo / "data" / "npcs.json"
    lines = [
        "{",
        "  \"source\": " + json.dumps(SOURCE_TEXT) + ",",
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
    flags = {a for a in argv if a.startswith("--")}
    unknown = flags - {"--check", "--write-towns", "--templates-only", "--allow-unpinned"}
    args = [a for a in argv if not a.startswith("--")]
    if unknown or len(args) != 1:
        raise SystemExit(
            "usage: import_npc_templates.py /path/to/L2J_Mobius_CT_0_Interlude "
            "[--check] [--write-towns] [--allow-unpinned]"
        )
    check_only = "--check" in flags
    write_towns_too = "--write-towns" in flags
    if write_towns_too and "--templates-only" in flags:
        raise SystemExit("--write-towns and --templates-only are exclusive")
    root = Path(args[0])
    npc_dir = datapack_dir(root, "stats/npcs")
    spawns_dir = datapack_dir(root, "spawns")
    if not npc_dir.is_dir():
        raise SystemExit(f"{root} has no stats/npcs (looked for dist/game/data and data)")
    source = verify_source(root, npc_dir, "--allow-unpinned" in flags)
    print(f"mobius source: {source}")
    if write_towns_too and not spawns_dir.is_dir():
        raise SystemExit(f"{root} has no spawns tree (needed by --write-towns)")

    repo = Path(__file__).resolve().parents[1]
    templates = apply_overrides(load_templates(npc_dir))
    check_combat_coverage(templates)
    print(f"documented overrides: {sorted(NPC_OVERRIDES)} (docs/npc-data-overrides.md)")

    if check_only:
        path = repo / "data" / "npcs.json"
        committed = {row["npcId"]: row for row in json.loads(path.read_text(encoding="utf-8"))["npcs"]}
        problems = compare_with_committed(templates, committed)
        with_attack = sum(1 for row in templates if row["pAtk"] > 0)
        print(f"templates {len(templates)} pAtk>0 rows {with_attack}")
        if problems:
            print(f"DRIFT {len(problems)}")
            for line in problems[:200]:
                print("  " + line)
            if len(problems) > 200:
                print(f"  ... {len(problems) - 200} more")
            raise SystemExit(1)
        print("npc template fields reproducible: 0 mismatches")
        return

    rows = with_custom_rows(repo, templates)
    write_templates(repo, rows)
    print(f"templates {len(rows)} ({len(CUSTOM_NPC_IDS)} custom kept)")
    if not write_towns_too:
        print("templates only: data/spawns untouched (pass --write-towns to regenerate town files)")
        return
    by_id = {row["npcId"]: row for row in rows}
    summary = write_towns(repo, by_id, spawns_dir)
    for map_id, count, enabled in summary:
        print(f"spawn {map_id} count={count} enabled={str(enabled).lower()}")


if __name__ == "__main__":
    main()
