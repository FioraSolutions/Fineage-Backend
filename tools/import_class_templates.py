#!/usr/bin/env python3
"""Transcribe L2J Mobius CT 0 Interlude class data into Fineage JSON.

Reads the Mobius datapack XML (not copied into this repo) and writes
data/classes.json, data/experience.json, and data/stat-bonuses.json.
Values are transcribed. This script does not copy GPL source.

Expected layout (L2J_Mobius_CT_0_Interlude):
  dist/game/data/stats/players/classList.xml
  dist/game/data/stats/players/experience.xml
  dist/game/data/stats/players/templates/**/*.xml
  dist/game/data/stats/statBonus.xml

Usage:
  python3 tools/import_class_templates.py /path/to/L2J_Mobius_CT_0_Interlude
"""

from __future__ import annotations

import json
import math
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# classList.xml parentClassId disagrees with Mobius PlayerClass and retail.
# Bladedancer (34) grows from Palus Knight (32), not Shillien Knight (33).
# Elemental Master (104) grows from Elemental Summoner (28), not Elven Wizard (26).
PARENT_CORRECTIONS = {34: 32, 104: 28}

# Race of each root class. Child classes inherit the root race.
# 0 human, 1 elf, 2 dark elf, 3 orc, 4 dwarf.
ROOT_RACE = {
    0: 0,
    10: 0,
    18: 1,
    25: 1,
    31: 2,
    38: 2,
    44: 3,
    49: 3,
    53: 4,
}

PDEF_PARTS = ("chest", "legs", "head", "feet", "gloves", "underwear", "cloak")
MDEF_PARTS = ("rear", "lear", "rfinger", "lfinger", "neck")
DEFAULT_MATK_SPD = 333
LEVELS = 80


def text_num(node: ET.Element, name: str) -> str:
    child = node.find(name)
    if child is None or child.text is None or not child.text.strip():
        raise SystemExit(f"missing <{name}> under <{node.tag}>")
    return child.text.strip()


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def find_child(node: ET.Element, name: str) -> ET.Element:
    for child in node:
        if local(child.tag) == name:
            return child
    raise SystemExit(f"missing <{name}> under <{local(node.tag)}>")


def child_text(node: ET.Element, name: str) -> str:
    return (find_child(node, name).text or "").strip()


def parse_class_list(path: Path) -> dict[int, dict]:
    root = ET.parse(path).getroot()
    classes: dict[int, dict] = {}
    for node in root:
        if local(node.tag) != "class":
            continue
        class_id = int(node.attrib["classId"])
        parent = node.attrib.get("parentClassId")
        classes[class_id] = {
            "classId": class_id,
            "name": node.attrib["name"],
            "parentClassId": -1 if parent is None else int(parent),
        }
    return classes


def parse_template(path: Path) -> dict:
    root = ET.parse(path).getroot()
    class_id = int(child_text(root, "classId"))
    static = find_child(root, "staticData")
    gains = find_child(root, "lvlUpgainData")

    def stat(name: str) -> str:
        return child_text(static, name)

    points = []
    for node in find_child(static, "creationPoints"):
        if local(node.tag) != "node":
            continue
        points.append(
            {
                "x": int(node.attrib["x"]),
                "y": int(node.attrib["y"]),
                "z": int(node.attrib["z"]),
            }
        )
    if not points:
        raise SystemExit(f"{path} has no creation points")

    pdef = find_child(static, "basePDef")
    mdef = find_child(static, "baseMDef")
    pdef_parts = {part: int(child_text(pdef, part)) for part in PDEF_PARTS}
    mdef_parts = {part: int(child_text(mdef, part)) for part in MDEF_PARTS}
    move = find_child(static, "baseMoveSpd")
    male = find_child(static, "collisionMale")
    female = find_child(static, "collisionFemale")

    hp: list[str] = []
    mp: list[str] = []
    cp: list[str] = []
    for level in gains:
        if local(level.tag) != "level":
            continue
        val = int(level.attrib["val"])
        if val != len(hp) + 1:
            raise SystemExit(f"{path} level {val} is out of order")
        hp.append(child_text(level, "hp"))
        mp.append(child_text(level, "mp"))
        cp.append(child_text(level, "cp"))
    if len(hp) < LEVELS:
        raise SystemExit(f"{path} has {len(hp)} levels, expected at least {LEVELS}")

    matk_spd = DEFAULT_MATK_SPD
    for child in static:
        if local(child.tag) == "baseMAtkSpd":
            matk_spd = int((child.text or "").strip())

    return {
        "classId": class_id,
        "str": int(stat("baseSTR")),
        "dex": int(stat("baseDEX")),
        "con": int(stat("baseCON")),
        "int": int(stat("baseINT")),
        "wit": int(stat("baseWIT")),
        "men": int(stat("baseMEN")),
        "pAtk": int(stat("basePAtk")),
        "mAtk": int(stat("baseMAtk")),
        "pDef": sum(pdef_parts.values()),
        "mDef": sum(mdef_parts.values()),
        "crit": int(stat("baseCritRate")),
        "pAtkSpd": int(stat("basePAtkSpd")),
        "mAtkSpd": matk_spd,
        "runSpeed": int(child_text(move, "run")),
        "walkSpeed": int(child_text(move, "walk")),
        "collisionMale": {
            "radius": child_text(male, "radius"),
            "height": child_text(male, "height"),
        },
        "collisionFemale": {
            "radius": child_text(female, "radius"),
            "height": child_text(female, "height"),
        },
        "spawns": points,
        "hp": hp[:LEVELS],
        "mp": mp[:LEVELS],
        "cp": cp[:LEVELS],
        "fingerNote": mdef_parts["lfinger"] != mdef_parts["rfinger"],
    }


def root_of(class_id: int, parents: dict[int, int]) -> int:
    seen = set()
    current = class_id
    while parents[current] >= 0:
        if current in seen:
            raise SystemExit(f"parent cycle at class {class_id}")
        seen.add(current)
        current = parents[current]
    return current


def parse_experience(path: Path) -> dict:
    root = ET.parse(path).getroot()
    rows = []
    for node in root:
        if local(node.tag) != "experience":
            continue
        rows.append(
            {
                "level": int(node.attrib["level"]),
                "toLevel": int(node.attrib["tolevel"]),
            }
        )
    return {
        "maxLevel": int(root.attrib["maxLevel"]),
        "maxPetLevel": int(root.attrib["maxPetLevel"]),
        "levels": rows,
    }


def parse_stat_bonus(path: Path) -> dict[str, list[str]]:
    root = ET.parse(path).getroot()
    tables: dict[str, list[str]] = {}
    for stat in root:
        name = local(stat.tag).lower()
        values: dict[int, str] = {}
        for node in stat:
            if local(node.tag) != "stat":
                continue
            values[int(node.attrib["value"])] = node.attrib["bonus"]
        if sorted(values) != list(range(100)):
            raise SystemExit(f"{name} bonus table is not values 0..99")
        tables[name] = [values[i] for i in range(100)]
    expected = {"str", "dex", "con", "int", "wit", "men"}
    if set(tables) != expected:
        raise SystemExit(f"stat bonus stats {sorted(tables)} != {sorted(expected)}")
    return tables


def dump_number_array(values: list[str]) -> str:
    return "[" + ", ".join(values) + "]"


def dump_class(row: dict) -> str:
    spawns = ", ".join(
        f'{{"x": {p["x"]}, "y": {p["y"]}, "z": {p["z"]}}}' for p in row["spawns"]
    )
    male = row["collisionMale"]
    female = row["collisionFemale"]
    accuracy = int(math.sqrt(row["dex"]) * 6 + 1)
    # Raw decimal text is written so 91.83 is not re-serialized through binary floats.
    return "\n".join(
        [
            "    {",
            f'      "classId": {row["classId"]},',
            f'      "name": {json.dumps(row["name"])},',
            f'      "parentClassId": {row["parentClassId"]},',
            f'      "race": {row["race"]},',
            f'      "str": {row["str"]},',
            f'      "dex": {row["dex"]},',
            f'      "con": {row["con"]},',
            f'      "int": {row["int"]},',
            f'      "wit": {row["wit"]},',
            f'      "men": {row["men"]},',
            f'      "pAtk": {row["pAtk"]},',
            f'      "mAtk": {row["mAtk"]},',
            f'      "pDef": {row["pDef"]},',
            f'      "mDef": {row["mDef"]},',
            f'      "accuracy": {accuracy},',
            f'      "crit": {row["crit"]},',
            f'      "pAtkSpd": {row["pAtkSpd"]},',
            f'      "mAtkSpd": {row["mAtkSpd"]},',
            f'      "runSpeed": {row["runSpeed"]},',
            f'      "walkSpeed": {row["walkSpeed"]},',
            '      "collisionMale": {"radius": '
            + male["radius"]
            + ', "height": '
            + male["height"]
            + "},",
            '      "collisionFemale": {"radius": '
            + female["radius"]
            + ', "height": '
            + female["height"]
            + "},",
            f'      "spawns": [{spawns}],',
            f'      "hp": {dump_number_array(row["hp"])},',
            f'      "mp": {dump_number_array(row["mp"])},',
            f'      "cp": {dump_number_array(row["cp"])}',
            "    }",
        ]
    )


def dump_experience(table: dict) -> str:
    rows = ",\n".join(
        f'    {{"level": {row["level"]}, "toLevel": {row["toLevel"]}}}' for row in table["levels"]
    )
    return (
        "{\n"
        '  "reference": "Experience required to be each level, transcribed from L2J Mobius CT 0 Interlude dist/game/data/stats/players/experience.xml. toLevel is the cumulative experience of that level. maxLevel is the chronicle cap.",\n'
        f'  "maxLevel": {table["maxLevel"]},\n'
        f'  "maxPetLevel": {table["maxPetLevel"]},\n'
        '  "levels": [\n'
        f"{rows}\n"
        "  ]\n"
        "}\n"
    )


def dump_bonuses(tables: dict[str, list[str]]) -> str:
    body = []
    for name in ("str", "dex", "con", "int", "wit", "men"):
        body.append(f'  "{name}": {dump_number_array(tables[name])}')
    joined = ",\n".join(body)
    return (
        "{\n"
        '  "reference": "Base-stat multipliers transcribed from L2J Mobius CT 0 Interlude dist/game/data/stats/statBonus.xml. Index is the stat value 0 through 99. CON multiplies the class HP and CP tables. MEN multiplies the MP table. Player-facing maxima truncate that product toward zero.",\n'
        f"{joined}\n"
        "}\n"
    )


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: import_class_templates.py <L2J_Mobius_CT_0_Interlude>")
    root = Path(sys.argv[1])
    stats = root / "dist" / "game" / "data" / "stats"
    players = stats / "players"
    classes = parse_class_list(players / "classList.xml")
    templates = {}
    for path in sorted((players / "templates").rglob("*.xml")):
        row = parse_template(path)
        if row["classId"] in templates:
            raise SystemExit(f"duplicate template {row['classId']}")
        templates[row["classId"]] = row
        if row["fingerNote"]:
            print(f"note: class {row['classId']} lfinger != rfinger", file=sys.stderr)

    missing = sorted(set(classes) - set(templates))
    extra = sorted(set(templates) - set(classes))
    if missing or extra:
        raise SystemExit(f"class list / templates differ missing={missing} extra={extra}")

    for class_id, parent in PARENT_CORRECTIONS.items():
        previous = classes[class_id]["parentClassId"]
        if previous == parent:
            raise SystemExit(f"correction for {class_id} is already {parent}")
        print(f"parent correction class {class_id}: classList {previous} -> {parent}", file=sys.stderr)
        classes[class_id]["parentClassId"] = parent

    parents = {class_id: row["parentClassId"] for class_id, row in classes.items()}
    for class_id, parent in parents.items():
        if parent >= 0 and parent not in classes:
            raise SystemExit(f"class {class_id} parent {parent} is missing")

    rows = []
    for class_id in sorted(classes):
        info = classes[class_id]
        template = templates[class_id]
        root_id = root_of(class_id, parents)
        if root_id not in ROOT_RACE:
            raise SystemExit(f"class {class_id} root {root_id} has no race")
        template.update(info)
        template["race"] = ROOT_RACE[root_id]
        rows.append(template)

    reference = (
        "Interlude class templates transcribed from L2J Mobius CT 0 Interlude. "
        "Names and parent ids: dist/game/data/stats/players/classList.xml. "
        "Stats, collision, creation points, and level HP/MP/CP: dist/game/data/stats/players/templates. "
        "P.Def is the sum of the seven basePDef slots. M.Def is the sum of rear, lear, rfinger, lfinger, and neck. "
        "Cast speed is the CreatureTemplate default 333 because the player XML does not set baseMAtkSpd. "
        "Accuracy is the level-1 physical accuracy sqrt(DEX)*6+1, truncated toward zero, with no weapon bonus. "
        "Parent corrections versus classList.xml: class 34 Bladedancer parent 32 (Palus Knight), "
        "class 104 Elemental Master parent 28 (Elemental Summoner). "
        "Those match Mobius PlayerClass and retail. classList.xml says 33 and 26. "
        "This file is original JSON, not a copy of the XML."
    )
    body = ",\n".join(dump_class(row) for row in rows)
    classes_json = "{\n" f'  "reference": {json.dumps(reference)},\n' '  "classes": [\n' f"{body}\n" "  ]\n}\n"

    repo = Path(__file__).resolve().parents[1]
    data = repo / "data"
    (data / "classes.json").write_text(classes_json, encoding="utf-8")
    (data / "experience.json").write_text(dump_experience(parse_experience(players / "experience.xml")), encoding="utf-8")
    (data / "stat-bonuses.json").write_text(dump_bonuses(parse_stat_bonus(stats / "statBonus.xml")), encoding="utf-8")

    bases = [row for row in rows if row["parentClassId"] < 0]
    print(f"wrote {len(rows)} classes, {len(bases)} base")
    for row in bases:
        print(
            f"base {row['classId']:3} {row['name']:16} race={row['race']} "
            f"STR {row['str']} DEX {row['dex']} CON {row['con']} INT {row['int']} WIT {row['wit']} MEN {row['men']} "
            f"patk {row['pAtk']} matk {row['mAtk']} pdef {row['pDef']} mdef {row['mDef']} "
            f"acc {int(math.sqrt(row['dex']) * 6 + 1)} crit {row['crit']} "
            f"aspd {row['pAtkSpd']}/{row['mAtkSpd']} move {row['runSpeed']}/{row['walkSpeed']} "
            f"hp1 {row['hp'][0]} mp1 {row['mp'][0]} cp1 {row['cp'][0]} "
            f"spawn {row['spawns'][0]['x']},{row['spawns'][0]['y']},{row['spawns'][0]['z']} "
            f"male {row['collisionMale']['radius']}/{row['collisionMale']['height']} "
            f"female {row['collisionFemale']['radius']}/{row['collisionFemale']['height']}"
        )


if __name__ == "__main__":
    main()
