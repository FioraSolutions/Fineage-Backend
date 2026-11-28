#!/usr/bin/env python3
"""Data guards for data/npcs.json against the pinned Mobius CT 0 datapack.

Raid level guard: every CT 0 template whose <skillList> carries a "Raid Boss - Level N"
passive (skill names resolved from CT 0 stats/skills, not XML comments) must have
level == N in data/npcs.json, unless the id is a documented override in
tools/import_npc_templates.py (none of those are raid bosses today).

Usage: python3 tools/check_npc_guards.py <mobius>/L2J_Mobius_CT_0_Interlude [--self-test]
"""
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
RAID_LEVEL = re.compile(r"Raid Boss - Level (\d+)")


def raid_level_skills(skills_dir: Path) -> dict[int, int]:
    out: dict[int, int] = {}
    for f in sorted(skills_dir.glob("*.xml")):
        for skill in ET.parse(f).getroot().iter("skill"):
            m = RAID_LEVEL.fullmatch(skill.get("name", ""))
            if m:
                out[int(skill.get("id"))] = int(m.group(1))
    return out


def expected_raid_levels(npc_dir: Path, skills: dict[int, int]) -> dict[int, tuple[str, set[int]]]:
    out: dict[int, tuple[str, set[int]]] = {}
    for f in sorted(npc_dir.glob("*.xml")):
        for npc in ET.parse(f).getroot().findall("npc"):
            skill_list = npc.find("skillList")
            if skill_list is None:
                continue
            levels = {skills[int(s.get("id"))] for s in skill_list.findall("skill") if int(s.get("id")) in skills}
            if levels:
                out[int(npc.get("id"))] = (npc.get("name", ""), levels)
    return out


def raid_level_violations(expected: dict[int, tuple[str, set[int]]], rows: dict[int, dict]) -> list[str]:
    bad: list[str] = []
    for npc_id, (name, levels) in sorted(expected.items()):
        row = rows.get(npc_id)
        if row is None:
            bad.append(f"{npc_id} {name}: missing from data/npcs.json")
        elif len(levels) != 1:
            bad.append(f"{npc_id} {name}: several raid level skills {sorted(levels)}")
        elif row.get("level") not in levels:
            bad.append(f"{npc_id} {name}: level {row.get('level')} but skill says Raid Boss - Level {next(iter(levels))}")
    return bad


def self_test() -> None:
    expected = {25001: ("Greyclaw Kutus", {23})}
    assert not raid_level_violations(expected, {25001: {"level": 23}})
    assert raid_level_violations(expected, {25001: {"level": 25}})
    assert raid_level_violations(expected, {})
    assert raid_level_violations({1: ("x", {20, 30})}, {1: {"level": 20}})
    print("self-test ok")


def main() -> None:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--self-test" in sys.argv:
        self_test()
    if len(args) != 1:
        raise SystemExit(__doc__)
    root = Path(args[0])
    if root.name != "L2J_Mobius_CT_0_Interlude":
        raise SystemExit(f"refusing {root}: guards only run against L2J_Mobius_CT_0_Interlude")
    stats = root / "dist" / "game" / "data" / "stats"
    skills = raid_level_skills(stats / "skills")
    if not skills:
        raise SystemExit(f"no 'Raid Boss - Level N' skills under {stats / 'skills'}")
    expected = expected_raid_levels(stats / "npcs", skills)
    rows = {r["npcId"]: r for r in json.loads((REPO / "data" / "npcs.json").read_text(encoding="utf-8"))["npcs"]}
    bad = raid_level_violations(expected, rows)
    print(f"raid level guard: {len(expected)} npcs with a Raid Boss - Level N skill, {len(bad)} violations")
    if bad:
        print("\n".join(bad))
        raise SystemExit(1)


if __name__ == "__main__":
    main()
