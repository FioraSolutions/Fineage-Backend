#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude skill templates and class skill trees into JSON.

Reads the Mobius datapack (not copied into this repo) and writes data/skills.json
and data/skill-trees.json. Values are transcribed. This script does not copy GPL source.

Mobius references (L2J_Mobius_CT_0_Interlude):
  dist/game/data/stats/skills/*.xml
  dist/game/data/stats/players/skillTrees/**/*.xml

Usage:
  python3 tools/import_skills.py /path/to/L2J_Mobius_CT_0_Interlude
"""

from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

SKILL_DIR = Path("dist/game/data/stats/skills")
TREE_DIR = Path("dist/game/data/stats/players/skillTrees")


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def child_text(node: ET.Element, name: str) -> str | None:
    for item in node:
        if local(item.tag) == name:
            return (item.text or "").strip() or None
    return None


def parse_table(node: ET.Element, name: str) -> list[str] | None:
    for item in node:
        if local(item.tag) == "table" and item.attrib.get("name") == name:
            text = (item.text or "").strip()
            if not text:
                return None
            return text.split()
    return None


def parse_num_list(values: list[str] | None, levels: int) -> list[int]:
    if not values:
        return [0] * levels
    if len(values) == 1:
        return [num(values[0])] * levels
    if len(values) != levels:
        raise SystemExit(f"table length {len(values)} != skill levels {levels}")
    return [num(v) for v in values]


def num(text: str) -> int:
    value = float(text)
    if not value:
        return 0
    return int(value) if value == int(value) else int(value + 0.5)


def parse_skill(node: ET.Element) -> dict | None:
    if local(node.tag) != "skill":
        return None
    skill_id = int(node.attrib["id"])
    levels = int(node.attrib.get("levels", "1"))
    name = node.attrib.get("name", f"Skill {skill_id}")

    mp = parse_num_list(parse_table(node, "#mpConsume"), levels)
    if not any(mp):
        single = child_text(node, "mpConsume")
        if single and not single.startswith("#"):
            mp = [num(single)] * levels

    power = parse_num_list(parse_table(node, "#power"), levels)
    if not any(power):
        single = child_text(node, "power")
        if single and not single.startswith("#"):
            power = [num(single)] * levels

    reuse = child_text(node, "reuseDelay")
    reuse_ms = int(reuse) if reuse and reuse.isdigit() else 0
    hit = child_text(node, "hitTime")
    hit_ms = int(hit) if hit and hit.isdigit() else 0
    cast = child_text(node, "castRange")
    cast_range = int(cast) if cast and cast.isdigit() else 40

    operate = child_text(node, "operateType") or "A1"
    target = child_text(node, "targetType") or "ONE"
    passive = operate.upper().startswith("P")

    abnormal_levels = parse_num_list(parse_table(node, "#abnormalLevels"), levels)
    if not any(abnormal_levels):
        single = child_text(node, "abnormalLevel")
        if single and not single.startswith("#"):
            abnormal_levels = [num(single)] * levels
        else:
            abnormal_levels = [1] * levels

    abnormal_time = child_text(node, "abnormalTime")
    if abnormal_time and abnormal_time.isdigit():
        abnormal_time_seconds = int(abnormal_time)
    else:
        abnormal_time_seconds = 0

    is_debuff = (child_text(node, "isDebuff") or "").lower() == "true"

    level_rows = []
    for index in range(levels):
        level_rows.append(
            {
                "level": index + 1,
                "mpConsume": mp[index] if index < len(mp) else 0,
                "power": power[index] if index < len(power) else 0,
                "abnormalLevel": abnormal_levels[index] if index < len(abnormal_levels) else 1,
            }
        )

    row = {
        "skillId": skill_id,
        "name": name,
        "levels": levels,
        "passive": passive,
        "operateType": operate,
        "targetType": target,
        "castRange": cast_range,
        "hitTimeMs": hit_ms,
        "reuseDelayMs": reuse_ms,
        "levelData": level_rows,
    }
    if abnormal_time_seconds > 0:
        row["abnormalTimeSeconds"] = abnormal_time_seconds
    if is_debuff:
        row["isDebuff"] = True
    return row


def load_skill_templates(skills_root: Path) -> dict[int, dict]:
    templates: dict[int, dict] = {}
    for path in sorted(skills_root.glob("*.xml")):
        root = ET.parse(path).getroot()
        for node in root:
            row = parse_skill(node)
            if row:
                templates[row["skillId"]] = row
    return templates


def load_skill_trees(trees_root: Path) -> list[dict]:
    trees: list[dict] = []
    for path in sorted(trees_root.rglob("*.xml")):
        root = ET.parse(path).getroot()
        for node in root:
            if local(node.tag) != "skillTree":
                continue
            if "classId" not in node.attrib:
                continue
            class_id = int(node.attrib["classId"])
            entries: list[dict] = []
            for skill in node:
                if local(skill.tag) != "skill":
                    continue
                entries.append(
                    {
                        "skillId": int(skill.attrib["skillId"]),
                        "level": int(skill.attrib["skillLevel"]),
                        "getLevel": int(skill.attrib.get("getLevel", "1")),
                        "autoGet": skill.attrib.get("autoGet", "false").lower() == "true",
                        "learnedByNpc": skill.attrib.get("learnedByNpc", "false").lower() == "true",
                    }
                )
            trees.append({"classId": class_id, "skills": entries})
    return trees


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: import_skills.py <L2J_Mobius_CT_0_Interlude>")
    root = Path(sys.argv[1]).resolve()
    skills_root = root / SKILL_DIR
    trees_root = root / TREE_DIR
    if not skills_root.is_dir() or not trees_root.is_dir():
        raise SystemExit(f"{root} is not an L2J_Mobius_CT_0_Interlude datapack root")

    templates = load_skill_templates(skills_root)
    trees = load_skill_trees(trees_root)

    referenced: set[int] = set()
    for tree in trees:
        for entry in tree["skills"]:
            referenced.add(entry["skillId"])

    missing = sorted(skill for skill in referenced if skill not in templates)
    if missing:
        raise SystemExit(f"missing skill templates for ids: {missing[:20]}")

    selected = [templates[sid] for sid in sorted(referenced)]

    repo = Path(__file__).resolve().parents[1]
    data = repo / "data"
    data.mkdir(exist_ok=True)

    skills_path = data / "skills.json"
    trees_path = data / "skill-trees.json"

    skills_path.write_text(
        json.dumps(
            {
                "reference": "Interlude skill templates transcribed from Mobius CT 0 Interlude dist/game/data/stats/skills. Original JSON, not a copy of the XML.",
                "skills": selected,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    trees_path.write_text(
        json.dumps(
            {
                "reference": "Class skill trees transcribed from Mobius dist/game/data/stats/players/skillTrees. Original JSON.",
                "trees": trees,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    print(f"wrote {skills_path} ({len(selected)} skills)")
    print(f"wrote {trees_path} ({len(trees)} class trees)")


if __name__ == "__main__":
    main()
