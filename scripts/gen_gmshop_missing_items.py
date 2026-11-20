#!/usr/bin/env python3
"""Fill Fineage data/items.json with GM-shop rows that are still missing.

Reads Mobius CT0 Interlude stats/items XML (values only) and writes Fineage
templates in the existing items.json shape. Existing rows are never rewritten.
This file is not Mobius source and does not copy Mobius code.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ITEMS_JSON = ROOT / "data" / "items.json"
BUYLISTS = ROOT / "data" / "buylists.json"
HTML_ADMIN = ROOT / "data" / "html" / "admin"
DEFAULT_MOBIUS = ROOT / "ref" / "mobius-items"

BODY_SLOTS = {
    "none", "underwear", "lear", "rear", "both_ears", "neck", "lfinger", "rfinger",
    "both_fingers", "head", "rhand", "lhand", "lrhand", "gloves", "chest", "legs",
    "fullarmor", "feet", "back", "hair", "hair2", "hairall", "alldress",
}
WEAPON_TYPES = {"sword", "blunt", "dagger", "bow", "pole", "dual", "fist", "fishingrod", "etc"}
ARMOR_TYPES = {"light", "heavy", "magic"}
CRYSTALS = {"d", "c", "b", "a", "s"}
ETC_TYPES = {"arrow", "lure"}
ALLOWED_KEYS = (
    "id", "name", "kind", "icon", "bodySlot", "weaponType", "armorType", "etcType",
    "stackable", "magic", "pDef", "crystal", "crystalCount", "enchantable", "equip",
    "scroll", "price", "weight", "sellable", "quest", "pAtk", "mAtk", "pAtkSpd",
    "critRate", "attackRange", "randomDamage", "mDef", "maxMp", "maxHp", "soulshots",
    "spiritshots", "shieldDef", "shieldRate", "shieldEvasionPenalty", "soulshot",
    "shotGrade", "spiritshot", "blessedSpiritshot",
)


def html_buy_list_ids() -> set[int]:
    ids: set[int] = set()
    files = [HTML_ADMIN / "gmshops.htm"] + list((HTML_ADMIN / "gmstore").rglob("*.htm"))
    for path in files:
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in re.finditer(r"admin_buy\s+(\d+)", text):
            ids.add(int(match.group(1)))
    return ids


def wanted_item_ids(lists: dict[int, dict], buy_ids: set[int]) -> set[int]:
    want: set[int] = set()
    for list_id in buy_ids:
        entry = lists.get(list_id)
        if not entry:
            continue
        for row in entry.get("items") or []:
            want.add(int(row["id"]))
    return want


def sets(el: ET.Element) -> dict[str, str]:
    out: dict[str, str] = {}
    for node in el.findall("set"):
        name = (node.get("name") or "").strip().lower()
        if name:
            out[name] = node.get("val") or ""
    return out


def for_stats(el: ET.Element) -> dict[str, float]:
    out: dict[str, float] = {}
    for block in el.findall("for"):
        for child in list(block):
            key = (child.get("stat") or "").strip()
            if not key:
                continue
            try:
                val = float(child.get("val") or "0")
            except ValueError:
                continue
            tag = child.tag.lower()
            if tag == "set":
                out[key] = val
            elif tag in {"add", "sub"}:
                signed = val if tag == "add" else -val
                out[key] = out.get(key, 0) + signed
    return out


def body_slot(raw: str | None) -> str | None:
    if not raw:
        return None
    token = raw.strip().lower()
    mapped = {
        "onepiece": "fullarmor",
        "rear;lear": "both_ears",
        "lear;rear": "both_ears",
        "rfinger;lfinger": "both_fingers",
        "lfinger;rfinger": "both_fingers",
    }.get(token, token)
    return mapped if mapped in BODY_SLOTS and mapped != "none" else None


def crystal(raw: str | None) -> str | None:
    if not raw:
        return None
    token = raw.strip().lower()
    if token in {"none", "ng", "no grade", "nograde", "no_grade"}:
        return "none"
    return token if token in CRYSTALS else None


def as_int(raw: str | float | None, default: int = 0) -> int:
    if raw is None or raw == "":
        return default
    try:
        return int(float(raw))
    except (TypeError, ValueError):
        return default


def scroll_fields(name: str, typ: str, grade: str | None) -> dict | None:
    typ = typ.lower()
    target = None
    fail = None
    bonus = 0
    max_enchant = 16
    if typ in {"scrl_enchant_wp", "bless_scrl_enchant_wp"} or typ == "scroll" and "enchant weapon" in name.lower():
        target = "weapon"
    elif typ in {"scrl_enchant_am", "bless_scrl_enchant_am"} or typ == "scroll" and "enchant armor" in name.lower():
        target = "armor"
    if target is None:
        return None
    lower = name.lower()
    if "crystal scroll" in lower:
        fail = "keep"
        bonus = 100
        max_enchant = 65535
    elif typ.startswith("bless_") or lower.startswith("blessed"):
        fail = "reset"
    else:
        fail = "crystallize"
    if not grade or grade == "none":
        return None
    return {"target": target, "fail": fail, "grade": grade, "maxEnchant": max_enchant, "bonusRate": bonus}


def convert(el: ET.Element) -> tuple[dict | None, str | None]:
    try:
        item_id = int(el.get("id") or 0)
    except ValueError:
        return None, "bad-id"
    if item_id <= 0:
        return None, "bad-id"
    name = (el.get("name") or "").strip() or f"item_{item_id}"
    typ = (el.get("type") or "EtcItem").strip()
    fields = sets(el)
    stats = for_stats(el)
    icon = fields.get("icon") or f"icon.item_{item_id}"
    kind = {"Weapon": "weapon", "Armor": "armor"}.get(typ, "etc")
    price = max(0, as_int(fields.get("price")))
    weight = max(0, as_int(fields.get("weight")))
    row: dict = {"id": item_id, "name": name, "kind": kind, "icon": icon, "price": price, "weight": weight}

    slot = body_slot(fields.get("bodypart"))
    if slot:
        row["bodySlot"] = slot

    grade = crystal(fields.get("crystal_type"))

    if kind == "weapon":
        weapon = (fields.get("weapon_type") or "").lower()
        if weapon == "dualfist":
            weapon = "fist"
        # Monster-only props sometimes omit weapon_type; WeaponType.Etc is the Fineage fallback.
        if not weapon:
            weapon = "etc"
        if weapon not in WEAPON_TYPES:
            return None, f"weapon-type:{weapon}"
        row["weaponType"] = weapon
        if not slot:
            return None, "weapon-missing-slot"
        if fields.get("is_magic_weapon", "").lower() == "true":
            row["magic"] = True
        if fields.get("enchant_enabled") == "true":
            row["enchantable"] = True
        if grade and grade != "none":
            row["crystal"] = grade
        if fields.get("crystal_count"):
            row["crystalCount"] = as_int(fields["crystal_count"])
        row["pAtk"] = as_int(stats.get("pAtk"))
        row["mAtk"] = as_int(stats.get("mAtk"))
        row["pAtkSpd"] = as_int(stats.get("pAtkSpd"))
        row["critRate"] = as_int(stats.get("critRate"))
        row["attackRange"] = as_int(fields.get("attack_range"), 40)
        row["randomDamage"] = as_int(fields.get("random_damage"), 10)
        row["soulshots"] = as_int(fields.get("soulshots"))
        row["spiritshots"] = as_int(fields.get("spiritshots"))
        return row, None

    if kind == "armor":
        if not slot:
            return None, "armor-missing-slot"
        armor = (fields.get("armor_type") or "").lower()
        if armor in ARMOR_TYPES:
            row["armorType"] = armor
        if fields.get("enchant_enabled") == "true":
            row["enchantable"] = True
        if grade and grade != "none":
            row["crystal"] = grade
        if fields.get("crystal_count"):
            row["crystalCount"] = as_int(fields["crystal_count"])
        if stats.get("pDef"):
            row["pDef"] = as_int(stats["pDef"])
        if stats.get("mDef"):
            row["mDef"] = as_int(stats["mDef"])
        if stats.get("maxMp"):
            row["maxMp"] = as_int(stats["maxMp"])
        if stats.get("maxHp"):
            row["maxHp"] = as_int(stats["maxHp"])
        if stats.get("sDef") or fields.get("shield_defense"):
            row["shieldDef"] = as_int(stats.get("sDef") or fields.get("shield_defense"))
        if stats.get("rShld") or fields.get("shield_defense_rate"):
            row["shieldRate"] = as_int(stats.get("rShld") or fields.get("shield_defense_rate"))
        if stats.get("rEvas"):
            # Mobius shields use <sub stat="rEvas">; for_stats stores it signed.
            row["shieldEvasionPenalty"] = abs(as_int(stats["rEvas"]))
        return row, None

    # Etc
    if fields.get("is_stackable", "").lower() == "true":
        row["stackable"] = True
    etc = (fields.get("etcitem_type") or "").lower()
    if etc in ETC_TYPES:
        row["etcType"] = etc
        if etc in {"arrow", "lure"} and "bodySlot" not in row:
            row["bodySlot"] = "lhand"
    if fields.get("is_questitem", "").lower() == "true":
        row["quest"] = True
    if fields.get("is_sellable", "").lower() == "false":
        row["sellable"] = False

    action = (fields.get("default_action") or "").upper()
    handler = (fields.get("handler") or "").lower().replace("_", "")
    if "blessedspiritshot" in handler or action in {"BLESS_SPIRITSHOT", "BLESSED_SPIRITSHOT"}:
        row["blessedSpiritshot"] = True
        row["shotGrade"] = grade or "none"
        row["stackable"] = True
    elif action == "SOULSHOT" or handler in {"soulshots", "soulshot"}:
        row["soulshot"] = True
        row["shotGrade"] = grade or "none"
        row["stackable"] = True
    elif action == "SPIRITSHOT" or handler in {"spiritshot", "spiritshots"}:
        row["spiritshot"] = True
        row["shotGrade"] = grade or "none"
        row["stackable"] = True

    scroll = scroll_fields(name, etc, grade)
    if scroll:
        row["scroll"] = scroll
        row["stackable"] = False

    return row, None


def load_mobius_index(src: Path) -> dict[int, ET.Element]:
    index: dict[int, ET.Element] = {}
    for path in sorted(src.rglob("*.xml")):
        if path.name.startswith("._"):
            continue
        try:
            root = ET.fromstring(path.read_text(encoding="utf-8-sig"))
        except ET.ParseError as exc:
            print(f"warn: skip {path.name}: {exc}", file=sys.stderr)
            continue
        for element in root.iter("item"):
            try:
                index[int(element.get("id"))] = element
            except (TypeError, ValueError):
                continue
    return index


def format_row(row: dict) -> str:
    ordered = {key: row[key] for key in ALLOWED_KEYS if key in row}
    dumped = json.dumps(ordered, indent=2, ensure_ascii=False)
    return "\n".join("    " + line if line else line for line in dumped.splitlines())


def item_spans(text: str) -> list[tuple[int, int, int]]:
    """Return (item_id, start, end) for each object in the items array (end exclusive, includes trailing comma)."""
    marker = '"items": ['
    begin = text.index(marker) + len(marker)
    end_arr = text.rindex("\n  ]")
    spans: list[tuple[int, int, int]] = []
    i = begin
    while True:
        start = text.find("{", i, end_arr)
        if start < 0:
            break
        depth = 0
        j = start
        while j < end_arr:
            ch = text[j]
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    j += 1
                    if j < end_arr and text[j] == ",":
                        j += 1
                    match = re.search(r'"id":\s*(\d+)', text[start:j])
                    if not match:
                        raise SystemExit(f"item object at {start} has no id")
                    spans.append((int(match.group(1)), start, j))
                    i = j
                    break
            j += 1
        else:
            raise SystemExit("unbalanced item object")
    return spans


def lis_ids(seq: list[int]) -> list[int]:
    """Longest increasing subsequence (n log n). Existing file has a 3-id island."""
    import bisect
    tails: list[int] = []
    tail_at: list[int] = []
    pred = [-1] * len(seq)
    for i, value in enumerate(seq):
        pos = bisect.bisect_left(tails, value)
        if pos == len(tails):
            tails.append(value)
            tail_at.append(i)
        else:
            tails[pos] = value
            tail_at[pos] = i
        pred[i] = tail_at[pos - 1] if pos else -1
    idx = tail_at[-1]
    out: list[int] = []
    while idx >= 0:
        out.append(seq[idx])
        idx = pred[idx]
    out.reverse()
    return out


def insert_rows(text: str, rows: list[dict]) -> str:
    spans = item_spans(text)
    by_id = {item_id: (start, end) for item_id, start, end in spans}
    existing = [item_id for item_id, _, _ in spans]
    spine = lis_ids(existing)
    additions = sorted((row for row in rows if row["id"] not in by_id), key=lambda row: row["id"])
    groups: dict[int | None, list[dict]] = {}
    for row in additions:
        pred = None
        for item_id in spine:
            if item_id < row["id"]:
                pred = item_id
            else:
                break
        groups.setdefault(pred, []).append(row)

    # Apply splices from the end of the file so earlier offsets stay valid.
    for pred, group in sorted(
        groups.items(),
        key=lambda kv: -1 if kv[0] is None else by_id[kv[0]][1],
        reverse=True,
    ):
        formatted = [format_row(row) for row in group]
        if pred is None:
            first_start = spans[0][1]
            block = "".join(item + ",\n" for item in formatted)
            text = text[:first_start] + block + text[first_start:]
            continue
        start, end = by_id[pred]
        had_comma = text[start:end].rstrip().endswith(",")
        if had_comma:
            # Keep the comma on pred; insert objects (each with a trailing comma) after it.
            block = "\n" + ",\n".join(formatted) + ","
            text = text[:end] + block + text[end:]
        else:
            # Pred is the last array element (no trailing comma).
            block = "," + "".join("\n" + item for item in formatted)
            text = text[:end] + block + text[end:]
    return text


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mobius", type=Path, default=DEFAULT_MOBIUS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if not args.mobius.is_dir():
        raise SystemExit(f"Mobius items dir missing: {args.mobius}")

    lists = {int(entry["id"]): entry for entry in json.loads(BUYLISTS.read_text())["lists"]}
    buy_ids = html_buy_list_ids()
    want = wanted_item_ids(lists, buy_ids)
    have = {int(item["id"]) for item in json.loads(ITEMS_JSON.read_text())["items"]}
    missing = sorted(want - have)
    print(f"html-lists={len(buy_ids)} want={len(want)} have={len(have)} missing={len(missing)}")

    index = load_mobius_index(args.mobius)
    print(f"mobius-index={len(index)}")
    added: list[dict] = []
    skipped: list[tuple[int, str]] = []
    for item_id in missing:
        element = index.get(item_id)
        if element is None:
            skipped.append((item_id, "not-in-mobius"))
            continue
        row, error = convert(element)
        if row is None:
            skipped.append((item_id, error or "convert"))
            continue
        added.append(row)

    print(f"added={len(added)} skipped={len(skipped)}")
    if skipped:
        from collections import Counter
        reasons = Counter(reason for _, reason in skipped)
        print("skip-reasons", dict(reasons))
        print("skipped-ids", [item_id for item_id, _ in skipped])

    if args.dry_run or not added:
        return 0

    text = ITEMS_JSON.read_text(encoding="utf-8")
    updated = insert_rows(text, added)
    # Validate
    parsed = json.loads(updated)
    ids = [item["id"] for item in parsed["items"]]
    if len(ids) != len(set(ids)):
        raise SystemExit("duplicate ids after insert")
    added_ids = {row["id"] for row in added}
    if added_ids - set(ids):
        raise SystemExit("an added id is missing from the rewritten file")
    # Existing objects must be byte-identical (file already has a 3-id out-of-order island).
    old_spans = {item_id: text[start:end] for item_id, start, end in item_spans(text)}
    new_spans = {item_id: updated[start:end] for item_id, start, end in item_spans(updated)}
    for item_id, chunk in old_spans.items():
        if item_id not in new_spans:
            raise SystemExit(f"existing id {item_id} disappeared")
        if new_spans[item_id].rstrip(",") != chunk.rstrip(","):
            raise SystemExit(f"existing id {item_id} was rewritten")
    ITEMS_JSON.write_text(updated if updated.endswith("\n") else updated + "\n", encoding="utf-8")
    print(f"wrote {ITEMS_JSON} items={len(ids)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
