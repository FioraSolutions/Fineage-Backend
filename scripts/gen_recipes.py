#!/usr/bin/env python3
"""Build data/recipes.json from Mobius CT0 Recipes.xml.

Usage: gen_recipes.py [path/to/Recipes.xml]
Default: ref/mobius-ct0-interlude/dist/game/data/Recipes.xml
"""
from __future__ import annotations

import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_XML = ROOT / "ref/mobius-ct0-interlude/dist/game/data/Recipes.xml"
OUT = ROOT / "data/recipes.json"


def main() -> None:
    src = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_XML
    if not src.is_file():
        raise SystemExit(f"Recipes.xml not found: {src}")

    root = ET.parse(src).getroot()
    recipes = []
    for item in root.findall("item"):
        rid = int(item.attrib["id"])
        recipe_item_id = int(item.attrib["recipeId"])
        name = item.attrib.get("name", "")
        level = int(item.attrib.get("craftLevel", "1"))
        kind = item.attrib.get("type", "dwarven").lower()
        success = int(item.attrib.get("successRate", "100"))
        ingredients = [
            {"itemId": int(ing.attrib["id"]), "count": int(ing.attrib["count"])}
            for ing in item.findall("ingredient")
        ]
        prod = item.find("production")
        if prod is None:
            continue
        production = {"itemId": int(prod.attrib["id"]), "count": int(prod.attrib.get("count", "1"))}
        mp = 0
        for use in item.findall("statUse"):
            if use.attrib.get("name", "").upper() == "MP":
                mp = int(use.attrib.get("value", "0"))
        recipes.append(
            {
                "id": rid,
                "recipeItemId": recipe_item_id,
                "name": name,
                "level": level,
                "dwarven": kind == "dwarven",
                "successRate": success,
                "mpConsume": mp,
                "ingredients": ingredients,
                "production": production,
            }
        )

    recipes.sort(key=lambda r: r["id"])
    OUT.write_text(
        json.dumps(
            {
                "reference": "Mobius CT0 Interlude data/Recipes.xml via scripts/gen_recipes.py",
                "recipes": recipes,
            },
            indent=2,
            ensure_ascii=False,
        )
        + "\n"
    )
    print(f"wrote {len(recipes)} recipes → {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
