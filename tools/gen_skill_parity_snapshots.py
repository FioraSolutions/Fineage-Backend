#!/usr/bin/env python3
"""Add Mobius CT0 Interlude rows to the skill parity snapshots.

The snapshots in tests/Fineage.Game.Tests/Fixtures are read straight from the Mobius skill XML
(including #reuseDelay / #abnormalLevels tables). They are independent of import_skills.py, so
the parity tests check the importer instead of echoing it.

--add-missing keeps every existing row and appends rows for skills.json ids the snapshot lacks.
A skills.json id with no Mobius XML template is an error.
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests" / "Fineage.Game.Tests" / "Fixtures"


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def child(node: ET.Element, name: str) -> str | None:
    for c in node:
        if local(c.tag) == name:
            return (c.text or "").strip()
    return None


def table(node: ET.Element, name: str) -> list[str] | None:
    for c in node:
        if local(c.tag) == "table" and c.attrib.get("name") == name:
            return (c.text or "").split()
    return None


def per_level(node: ET.Element, name: str, levels: int) -> list[int]:
    raw = child(node, name)
    if raw is None or raw == "":
        return [0] * levels
    if raw.startswith("#"):
        values = table(node, raw)
        if values is None:
            raise SystemExit(f"skill {node.attrib['id']}: {name} references missing table {raw}")
        nums = [int(float(v)) for v in values]
        return [nums[min(i, len(nums) - 1)] for i in range(levels)]
    return [int(float(raw))] * levels


def load_xml(skills_root: Path) -> dict[int, tuple[list[int], str, list[int]]]:
    rows: dict[int, tuple[list[int], str, list[int]]] = {}
    for path in sorted(skills_root.glob("*.xml")):
        for node in ET.parse(path).getroot():
            if local(node.tag) != "skill":
                continue
            levels = int(node.attrib.get("levels", "1"))
            reuse = per_level(node, "reuseDelay", levels)
            abnormal_type = (child(node, "abnormalType") or "").strip()
            abnormal_levels = per_level(node, "abnormalLevel", levels)
            rows[int(node.attrib["id"])] = (reuse, abnormal_type, abnormal_levels)
    return rows


def dump(path: Path, data: dict) -> None:
    ordered = {k: data[k] for k in sorted(data, key=int)}
    path.write_text(json.dumps(ordered, separators=(",", ":")), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mobius-skills", required=True, help="Mobius data/stats/skills directory")
    ap.add_argument("--skills-json", default=str(ROOT / "data" / "skills.json"))
    ap.add_argument("--add-missing", action="store_true", required=True)
    args = ap.parse_args()

    xml = load_xml(Path(args.mobius_skills))
    skills = json.loads(Path(args.skills_json).read_text(encoding="utf-8"))
    ids = sorted({int(s["skillId"]) for s in (skills["skills"] if isinstance(skills, dict) else skills)})

    reuse_path = FIXTURES / "skill-reuse-snapshot.json"
    abnormal_path = FIXTURES / "skill-abnormal-snapshot.json"
    reuse = json.loads(reuse_path.read_text(encoding="utf-8"))
    abnormal = json.loads(abnormal_path.read_text(encoding="utf-8"))

    added = []
    missing_xml = []
    for sid in ids:
        key = str(sid)
        if key in reuse and key in abnormal:
            continue
        if sid not in xml:
            missing_xml.append(sid)
            continue
        r, t, lv = xml[sid]
        reuse.setdefault(key, r)
        abnormal.setdefault(key, {"type": t, "levels": lv})
        added.append(sid)

    dump(reuse_path, reuse)
    dump(abnormal_path, abnormal)
    print(f"added={len(added)} {added}")
    if missing_xml:
        print(f"skills.json ids with no Mobius XML template (not added): {missing_xml}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
