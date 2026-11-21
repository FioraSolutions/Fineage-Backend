#!/usr/bin/env python3
"""Convert Mobius CT0 Interlude augmentation XML into Fineage data/augmentation/*.json."""

from __future__ import annotations

import json
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOBIUS_ROOT = ROOT / "ref/mobius-ct0-interlude/L2J_Mobius_CT_0_Interlude/dist/game/data"
OUT_DIR = ROOT / "data" / "augmentation"


def mobius_augment_dir() -> Path:
    env = os.environ.get("FINEAGE_MOBIUS_XML_ROOT")
    if env:
        p = Path(env) / "stats/augmentation"
        if p.is_dir():
            return p
    if (MOBIUS_ROOT / "stats/augmentation").is_dir():
        return MOBIUS_ROOT / "stats/augmentation"
    raise SystemExit(
        "Mobius augmentation data not found. Clone ref/mobius-ct0-interlude or set FINEAGE_MOBIUS_XML_ROOT."
    )


def parse_options(options_dir: Path) -> dict:
    by_id: dict[int, dict] = {}
    for path in sorted(options_dir.glob("*.xml")):
        root = ET.parse(path).getroot()
        for opt in root.findall("option"):
            oid = int(opt.get("id", "0"))
            entry: dict = {"id": oid, "name": opt.get("name", "")}
            stats = []
            skills = []
            for child in opt:
                if child.tag == "stats":
                    for node in child:
                        kind = node.tag  # add, mul, set
                        stat = node.get("stat")
                        val = node.get("val")
                        if stat and val is not None:
                            stats.append({"kind": kind, "stat": stat, "val": float(val)})
                elif child.tag == "skill":
                    skills.append(
                        {
                            "skillId": int(child.get("id", "0")),
                            "level": int(child.get("level", "1")),
                        }
                    )
            if stats:
                entry["stats"] = stats
            if skills:
                entry["skills"] = skills
            by_id[oid] = entry
    return by_id


def parse_retail_chances(path: Path) -> list:
    root = ET.parse(path).getroot()
    rows: list = []
    for weapon in root.findall("weapon"):
        wtype = weapon.get("type", "")
        for stone in weapon.findall("stone"):
            stone_id = int(stone.get("id", "0"))
            for variation in stone.findall("variation"):
                var_id = int(variation.get("id", "0"))
                for category in variation.findall("category"):
                    cat_prob = int(category.get("probability", "100"))
                    for aug in category.findall("augment"):
                        rows.append(
                            {
                                "weaponType": wtype,
                                "stoneId": stone_id,
                                "variationId": var_id,
                                "categoryChance": cat_prob,
                                "augmentId": int(aug.get("id", "0")),
                                "chance": float(aug.get("chance", "0")),
                            }
                        )
    return rows


def parse_skillmap(path: Path) -> dict:
    root = ET.parse(path).getroot()
    by_id: dict[int, dict] = {}
    for aug in root.findall("augmentation"):
        aid = int(aug.get("id", "0"))
        row = {"id": aid, "skillId": 0, "skillLevel": 0, "type": "blue"}
        for child in aug:
            if child.tag == "skillId":
                row["skillId"] = int(child.get("val", "0"))
            elif child.tag == "skillLevel":
                row["skillLevel"] = int(child.get("val", "0"))
            elif child.tag == "type":
                row["type"] = child.get("val", "blue")
        by_id[aid] = row
    return by_id


def main() -> int:
    src = mobius_augment_dir()
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    options = parse_options(src / "options")
    chances = parse_retail_chances(src / "retailchances.xml")
    skillmap = parse_skillmap(src / "augmentation_skillmap.xml")

    header = {
        "generated": True,
        "source": "Mobius CT0 Interlude stats/augmentation",
    }

    (OUT_DIR / "options.json").write_text(
        json.dumps({**header, "options": options}, separators=(",", ":")),
        encoding="utf-8",
    )
    (OUT_DIR / "retail-chances.json").write_text(
        json.dumps({**header, "chances": chances}, separators=(",", ":")),
        encoding="utf-8",
    )
    (OUT_DIR / "skillmap.json").write_text(
        json.dumps({**header, "skillmap": skillmap}, separators=(",", ":")),
        encoding="utf-8",
    )

    print(f"Wrote {len(options)} options, {len(chances)} chance rows, {len(skillmap)} skillmap entries -> {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())