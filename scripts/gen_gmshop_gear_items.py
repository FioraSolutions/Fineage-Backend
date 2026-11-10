#!/usr/bin/env python3
"""Import Mobius CT0 weapon/armor rows needed by GM-shop buylists into data/items.json.

Default lists: 9998 (S weapons + SA, Arcana Mace) and 9956 (S sets: Imperial / Draconic / Major Arcana).
Existing items.json rows are never overwritten.
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
    MOBIUS_ITEMS = pathlib.Path("/Users/nikitarakitin/Projects/Fineage/ref/mobius-ct0-interlude/dist/game/data/stats/items")

LIST_IDS = [int(x) for x in sys.argv[1:]] or [9998, 9956]


def body_slot(raw: str | None) -> str | None:
    if not raw:
        return None
    t = raw.strip().lower()
    return {
        "onepiece": "fullarmor",
        "rear;lear": "both_ears",
        "lear;rear": "both_ears",
        "rfinger;lfinger": "both_fingers",
        "lfinger;rfinger": "both_fingers",
    }.get(t, t)


def sets(el: ET.Element) -> dict[str, str]:
    return {s.get("name", "").lower(): (s.get("val") or "") for s in el.findall("set")}


def for_stats(el: ET.Element) -> dict[str, float]:
    out: dict[str, float] = {}
    for node in el.findall("for"):
        for child in list(node):
            tag = child.tag.lower()
            if tag == "set":
                key = (child.get("stat") or "").strip()
                if key:
                    try:
                        out[key] = float(child.get("val") or "0")
                    except ValueError:
                        pass
            elif tag == "add":
                key = (child.get("stat") or "").strip()
                if key in ("pDef", "mDef", "maxMp", "maxHp"):
                    try:
                        out[key] = out.get(key, 0) + float(child.get("val") or "0")
                    except ValueError:
                        pass
    return out


def crystal(raw: str | None) -> str | None:
    if not raw:
        return None
    t = raw.strip().lower()
    return t if t in {"d", "c", "b", "a", "s"} else None


def convert(el: ET.Element) -> dict | None:
    iid = int(el.get("id") or 0)
    name = el.get("name") or f"item_{iid}"
    typ = (el.get("type") or "EtcItem").strip()
    s = sets(el)
    st = for_stats(el)
    icon = s.get("icon") or f"icon.item_{iid}"
    kind = {"Weapon": "weapon", "Armor": "armor"}.get(typ)
    if kind is None:
        return None

    row: dict = {
        "id": iid,
        "name": name,
        "kind": kind,
        "icon": icon,
        "price": int(float(s.get("price") or 0)),
        "weight": int(float(s.get("weight") or 0)),
    }
    slot = body_slot(s.get("bodypart"))
    if slot:
        row["bodySlot"] = slot
    if s.get("enchant_enabled") == "true":
        row["enchantable"] = True
    cr = crystal(s.get("crystal_type"))
    if cr:
        row["crystal"] = cr
    if s.get("crystal_count"):
        row["crystalCount"] = int(float(s["crystal_count"]))

    if kind == "weapon":
        wt = (s.get("weapon_type") or "").lower()
        # Fineage WeaponType has Fist, not DualFist.
        if wt == "dualfist":
            wt = "fist"
        if wt:
            row["weaponType"] = wt
        if s.get("is_magic_weapon") == "true":
            row["magicWeapon"] = True
        row["pAtk"] = int(st.get("pAtk", 0))
        row["mAtk"] = int(st.get("mAtk", 0))
        row["pAtkSpd"] = int(st.get("pAtkSpd", 0))
        row["critRate"] = int(st.get("critRate", 0))
        row["attackRange"] = int(float(s.get("attack_range") or 40))
        row["randomDamage"] = int(float(s.get("random_damage") or 10))
        row["soulshots"] = int(float(s.get("soulshots") or 0))
        row["spiritshots"] = int(float(s.get("spiritshots") or 0))
    else:
        at = (s.get("armor_type") or "").lower()
        if at and at not in {"none", "none"}:
            row["armorType"] = at
        if st.get("pDef"):
            row["pDef"] = int(st["pDef"])
        if st.get("mDef"):
            row["mDef"] = int(st["mDef"])
        if s.get("shield_defense"):
            row["shieldDef"] = int(float(s["shield_defense"]))
        if s.get("shield_defense_rate"):
            row["shieldRate"] = int(float(s["shield_defense_rate"]))

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
    print(f"added={len(added)} skipped={skipped[:20]}{'...' if len(skipped) > 20 else ''}")
    for r in added:
        if r["id"] in (6579, 6608, 6609, 6610, 6379, 6382, 6380, 6381, 6383):
            print("key", r["id"], r["name"], r.get("weaponType") or r.get("bodySlot"))


if __name__ == "__main__":
    main()
