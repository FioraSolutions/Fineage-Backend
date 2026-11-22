#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude EnchantSkillTreeData.xml into data/skill-enchant-tree.json.

Reads the Mobius datapack (not copied into this repo) and writes enchant routes keyed by skill id/level.
Values are transcribed. This script does not copy GPL source.

Mobius reference (L2J_Mobius_CT_0_Interlude):
  dist/game/data/EnchantSkillTreeData.xml

Usage:
  python3 tools/import_skill_enchant_tree.py /path/to/L2J_Mobius_CT_0_Interlude
  python3 tools/import_skill_enchant_tree.py /path/to/dist/game/data/EnchantSkillTreeData.xml
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def num(text: str) -> int:
    value = float(text)
    if not value:
        return 0
    return int(value) if value == int(value) else int(value + 0.5)


def resolve_xml(argv: list[str]) -> Path:
    if len(argv) != 2:
        raise SystemExit("usage: import_skill_enchant_tree.py <mobius-root-or-EnchantSkillTreeData.xml>")
    raw = Path(argv[1])
    if raw.is_file():
        return raw
    candidate = raw / "dist/game/data/EnchantSkillTreeData.xml"
    if candidate.is_file():
        return candidate
    raise SystemExit(f"EnchantSkillTreeData.xml not found under {raw}")


def main() -> None:
    xml_path = resolve_xml(sys.argv)
    tree = ET.parse(xml_path)
    root = tree.getroot()
    routes: list[dict] = []
    for node in root:
        if local(node.tag) != "skill":
            continue
        attrs = node.attrib
        routes.append(
            {
                "skillId": int(attrs["id"]),
                "level": int(attrs["level"]),
                "name": attrs.get("name", ""),
                "desc": attrs.get("desc", ""),
                "baseLevel": int(attrs["baseLevel"]),
                "minSkillLevel": int(attrs["minSkillLevel"]),
                "sp": num(attrs["sp"]),
                "exp": num(attrs["exp"]),
                "rate76": int(attrs["rate76"]),
                "rate77": int(attrs["rate77"]),
                "rate78": int(attrs["rate78"]),
                "rate79": int(attrs["rate79"]),
                "rate80": int(attrs["rate80"]),
            }
        )

    out = Path(__file__).resolve().parents[1] / "data" / "skill-enchant-tree.json"
    payload = {"routes": routes}
    out.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {len(routes)} enchant routes to {out}")


if __name__ == "__main__":
    main()