#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude `mpInitialConsume` to every levelData row of data/skills.json.

Mobius takes `mpInitialConsume` at cast start (Creature.useMagic → reduceMp(initmpcons)) on top of `mpConsume`
taken at launch. Toggles that carry only `mpInitialConsume` (Vicious Stance, Shield Fortress, Soul Cry …) pay it
once and then drain MP per effect tick, so the zone cannot model them correctly without this column.

Usage: gen_skill_mp_initial.py <mobius skills xml dir>
"""
import json
import pathlib
import re
import sys

src = pathlib.Path(sys.argv[1])
xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)}

path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skills.json"
raw = path.read_text()
data = json.loads(raw)
indent = 1 if raw.startswith("{\n \"") else 2


def num(text: str) -> int:
    value = float(text)
    return 0 if not value else int(value) if value == int(value) else int(value + 0.5)


def table(body: str, name: str) -> list[str] | None:
    m = re.search(r'<table name="%s">(.*?)</table>' % re.escape(name), body, re.S)
    if not m:
        return None
    values = m.group(1).split()
    return values or None


def scalar(body: str, name: str) -> str | None:
    m = re.search(r'<set name="%s" val="([^"]+)"' % re.escape(name), body)
    if not m:
        return None
    value = m.group(1).strip()
    return None if value.startswith("#") else value


def per_level(body: str, levels: int) -> list[int]:
    values = table(body, "#mpInitialConsume")
    if values:
        if len(values) == 1:
            return [num(values[0])] * levels
        if len(values) != levels:
            raise SystemExit(f"#mpInitialConsume length {len(values)} != {levels} levels")
        return [num(v) for v in values]
    single = scalar(body, "mpInitialConsume")
    if single:
        return [num(single)] * levels
    return [0] * levels


rows = 0
skills = 0
for skill in data["skills"]:
    levels = skill.get("levelData") or []
    values = per_level(bodies.get(skill["skillId"], ""), len(levels))
    if not any(values):
        for row in levels:
            row.pop("mpInitialConsume", None)
        continue
    skills += 1
    for row, value in zip(levels, values):
        row["mpInitialConsume"] = value
        rows += 1

path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
print(f"mpInitialConsume on {skills} skills ({rows} level rows) of {len(data['skills'])} skills")
