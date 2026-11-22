#!/usr/bin/env python3
"""Write data/interlude-skill-ids.tsv from Mobius CT_0 Interlude skill XML.

Does not copy Mobius source — reads dist XML only. Regenerate after Mobius datapack updates.

Usage:
  ./scripts/fetch-mobius-interlude.sh   # optional; needs clone under .cache/l2j-mobius
  python3 tools/gen_interlude_skill_whitelist.py [path/to/L2J_Mobius_CT_0_Interlude]
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT = ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude"
SKILL_DIR = Path("dist/game/data/stats/skills")
OUT = ROOT / "data" / "interlude-skill-ids.tsv"


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def main() -> None:
    mobius = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT
    skill_root = mobius / SKILL_DIR
    if not skill_root.is_dir():
        raise SystemExit(f"Missing {skill_root} — run scripts/fetch-mobius-interlude.sh")

    rows: list[tuple[int, str, int]] = []
    for xml in sorted(skill_root.rglob("*.xml")):
        tree = ET.parse(xml)
        for node in tree.getroot():
            if local(node.tag) != "skill":
                continue
            sid = int(node.attrib["id"])
            name = node.attrib.get("name", f"Skill {sid}")
            levels = int(node.attrib.get("levels", "1"))
            rows.append((sid, name, levels))

    rows.sort(key=lambda r: r[0])
    lines = ["id\tname\tlevels", *(f"{sid}\t{name}\t{levels}" for sid, name, levels in rows)]
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {len(rows)} skills to {OUT}")


if __name__ == "__main__":
    main()