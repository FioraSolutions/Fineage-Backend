#!/usr/bin/env python3
"""Import Mobius CT0 EtcItem rows needed by GM-shop buylists into data/items.json.

Default list: 9938 (Shots). Existing items.json rows are never overwritten.
"""
from __future__ import annotations

import json
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
ITEMS_JSON = ROOT / "data" / "items.json"
BUYLISTS = ROOT / "data" / "buylists.json"
MOBIUS_ITEMS = ROOT / "ref" / "mobius-ct0-interlude" / "dist" / "game" / "data" / "stats" / "items"
if not MOBIUS_ITEMS.is_dir():
    MOBIUS_ITEMS = pathlib.Path(
        "/Users/nikitarakitin/Projects/Fineage/ref/mobius-ct0-interlude/dist/game/data/stats/items"
    )

LIST_IDS = [int(x) for x in sys.argv[1:]] or [9938]


def sets(el: ET.Element) -> dict[str, str]:
    return {s.get("name", "").lower(): (s.get("val") or "") for s in el.findall("set")}


def crystal(raw: str | None) -> str | None:
    if not raw:
        return None
    t = raw.strip().lower()
    if t in {"none", "ng", "no grade", "nograde"}:
        return "none"
    return t if t in {"d", "c", "b", "a", "s", "none"} else None


def convert(el: ET.Element) -> dict | None:
    iid = int(el.get("id") or 0)
    name = el.get("name") or f"item_{iid}"
    typ = (el.get("type") or "").strip()
    if typ != "EtcItem":
        return None

    s = sets(el)
    row: dict = {
        "id": iid,
        "name": name,
        "kind": "etc",
        "icon": s.get("icon") or f"icon.item_{iid}",
        "stackable": (s.get("is_stackable") or "").lower() == "true",
        "price": int(float(s.get("price") or 0)),
        "weight": int(float(s.get("weight") or 0)),
    }

    action = (s.get("default_action") or "").upper()
    handler = (s.get("handler") or "").lower().replace("_", "")
    grade = crystal(s.get("crystal_type"))

    if "blessedspiritshot" in handler or action in ("BLESS_SPIRITSHOT", "BLESSED_SPIRITSHOT"):
        row["blessedSpiritshot"] = True
        if grade:
            row["shotGrade"] = grade
    elif action == "SOULSHOT" or handler in ("soulshots", "soulshot"):
        row["soulshot"] = True
        if grade:
            row["shotGrade"] = grade
    elif action == "SPIRITSHOT" or handler in ("spiritshot", "spiritshots"):
        row["spiritshot"] = True
        if grade:
            row["shotGrade"] = grade

    return row


def load_mobius_index(src: pathlib.Path) -> dict[int, ET.Element]:
    index: dict[int, ET.Element] = {}
    for f in sorted(src.glob("*.xml")):
        root = ET.parse(f).getroot()
        for e in root.iter("item"):
            try:
                index[int(e.get("id"))] = e
            except (TypeError, ValueError):
                continue
    return index


def main() -> None:
    if not MOBIUS_ITEMS.is_dir():
        raise SystemExit(f"Mobius items dir missing: {MOBIUS_ITEMS}")

    buy = {x["id"]: x for x in json.loads(BUYLISTS.read_text())["lists"]}
    want: set[int] = set()
    for lid in LIST_IDS:
        lst = buy.get(lid)
        if not lst:
            print(f"warn: buylist {lid} missing")
            continue
        want |= {int(r["id"]) for r in lst.get("items") or []}

    data = json.loads(ITEMS_JSON.read_text())
    have = {int(i["id"]) for i in data["items"]}
    missing = sorted(want - have)
    print(f"want={len(want)} missing={len(missing)} lists={LIST_IDS}")

    index = load_mobius_index(MOBIUS_ITEMS)
    added = []
    skipped = []
    for iid in missing:
        el = index.get(iid)
        if el is None:
            skipped.append((iid, "not-in-mobius"))
            continue
        row = convert(el)
        if row is None:
            skipped.append((iid, el.get("type")))
            continue
        added.append(row)

    data["items"].extend(added)
    data["items"].sort(key=lambda i: int(i["id"]))
    ITEMS_JSON.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    print(f"added={len(added)} skipped={skipped[:30]}{'...' if len(skipped) > 30 else ''}")
    for r in added[:12]:
        print(" +", r["id"], r["name"], r.get("soulshot") and "soulshot" or r.get("spiritshot") and "spirit" or "")


if __name__ == "__main__":
    main()
