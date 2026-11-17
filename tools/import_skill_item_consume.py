#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude `itemConsumeId` / `itemConsumeCount` to data/skills.json.

Mobius `Creature.checkDoCastConditions` refuses the cast with SysMsg 351 ("There are not enough necessary
items to use the skill.") when the bag holds fewer than `itemConsumeCount` of `itemConsumeId`, then
`useMagic` destroys them before the effect lands. Skills that need it: Summon Treasure Key (419, 2× Thief
Key), the Battle Symbol signets (454–460, 1× Battle Symbol 8875), Soul of the Phoenix, caster summons, …

Usage: import_skill_item_consume.py <mobius dist/game/data/stats/skills dir>
"""
from __future__ import annotations

import json
import pathlib
import re
import sys


def num(text: str) -> int:
    value = float(text)
    return 0 if not value else int(value) if value == int(value) else int(value + 0.5)


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: import_skill_item_consume.py <mobius stats/skills dir>")
    src = pathlib.Path(sys.argv[1])
    raw_xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
    bodies = {int(m.group(1)): m.group(0) for m in re.finditer(r'<skill id="(\d+)".*?</skill>', raw_xml, re.S)}

    path = pathlib.Path(__file__).resolve().parents[1] / "data" / "skills.json"
    raw = path.read_text(encoding="utf-8")
    data = json.loads(raw)
    indent = 1 if raw.startswith("{\n \"") else 2

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

    def per_level(body: str, name: str, levels: int) -> list[int]:
        values = table(body, "#" + name) or table(body, name)
        if values:
            if len(values) == 1:
                return [num(values[0])] * levels
            # Enchant tables sometimes carry extra rows past the base levels; the base rows come first.
            if len(values) < levels:
                raise SystemExit(f"{name} length {len(values)} != {levels} levels")
            return [num(v) for v in values[:levels]]
        single = scalar(body, name)
        if single:
            return [num(single)] * levels
        # A table-valued <set> (itemConsumeId="#itemConsumeId", itemConsumeCount="#itemConsumeCountOT")
        # resolves through the #-table, which per_level already tried above under its own name.
        return [0] * levels

    def extractable(body: str, levels: int) -> list[list[dict]]:
        """Mobius `capsuled_items_skill` → RestorationRandom groups, one list per skill level.

        Each entry of the #extractableItems table is one level: `id,count,chance;id,count,chance;…` — the
        roulette segments RestorationRandom walks with `100 * Rnd.nextDouble()`.
        """
        if not re.search(r'set name="capsuled_items_skill"', body):
            return []
        values = table(body, "#extractableItems") or []
        if len(values) == 1 and levels > 1:
            values = values * levels
        per_level_groups: list[list[dict]] = []
        for entry in values[:levels]:
            groups: list[dict] = []
            for segment in entry.split(";"):
                parts = [p for p in segment.split(",") if p]
                if len(parts) < 3:
                    continue
                *item_pairs, chance = parts
                items = [
                    {"id": int(item_pairs[i]), "count": int(item_pairs[i + 1])}
                    for i in range(0, len(item_pairs) - 1, 2)
                ]
                groups.append({"items": items, "chance": float(chance)})
            per_level_groups.append(groups)
        return per_level_groups

    skills = 0
    rows = 0
    for skill in data["skills"]:
        body = bodies.get(skill["skillId"], "")
        level_rows = skill.get("levelData") or []
        levels = len(level_rows)
        ids = per_level(body, "itemConsumeId", levels)
        counts = per_level(body, "itemConsumeCount", levels)
        groups = extractable(body, levels)
        if not any(ids) and not any(counts) and not groups and "itemConsumeId" not in body:
            for row in level_rows:
                row.pop("itemConsumeId", None)
                row.pop("itemConsumeCount", None)
                row.pop("extractable", None)
            continue
        for index, (row, item_id, count) in enumerate(zip(level_rows, ids, counts)):
            if item_id > 0:
                row["itemConsumeId"] = item_id
                row["itemConsumeCount"] = count
                rows += 1
            else:
                row.pop("itemConsumeId", None)
                row.pop("itemConsumeCount", None)
            if groups and index < len(groups) and groups[index]:
                row["extractable"] = groups[index]
            else:
                row.pop("extractable", None)
        skills += 1

    path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"itemConsume on {skills} skills ({rows} level rows) of {len(data['skills'])} skills")


if __name__ == "__main__":
    main()
