#!/usr/bin/env python3
"""Sync Fineage skill definition and combat catalogs from Mobius CT_0 Interlude XML.

Transcribes values only (not GPL source). Updates existing rows; does not invent new
skill-combat entries (those need a damage/effect path first).

Usage:
  sync_skill_defs_from_mobius.py --family-root 0
  sync_skill_defs_from_mobius.py --class-ids 0,1,2
  sync_skill_defs_from_mobius.py --all
  sync_skill_defs_from_mobius.py --snapshot-only --family-root 0
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SKILLS = Path("/workspace/mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills")
SKILLS_JSON = ROOT / "data/skills.json"
COMBAT_JSON = ROOT / "data/skill-combat.json"
CLASSES = ROOT / "data/classes.json"
TREES = ROOT / "data/skill-trees.json"
COMBAT_SNAP = ROOT / "tests/Fineage.Game.Tests/Fixtures/skill-combat-snapshot.json"
DEF_SNAP = ROOT / "tests/Fineage.Game.Tests/Fixtures/skill-definition-snapshot.json"
RETAIL_OVERRIDES = ROOT / "tests/Fineage.Game.Tests/Fixtures/skill-retail-overrides.json"


def descendants(classes: dict[int, dict], root: int) -> list[int]:
    out = {root}
    changed = True
    while changed:
        changed = False
        for cid, row in classes.items():
            if cid not in out and row.get("parentClassId") in out:
                out.add(cid)
                changed = True
    return sorted(out)


def family_skill_ids(class_ids: set[int]) -> set[int]:
    ids: set[int] = set()
    for tree in json.loads(TREES.read_text())["trees"]:
        if tree["classId"] in class_ids:
            for row in tree["skills"]:
                ids.add(row["skillId"])
    return ids


def none_as_empty(text: str) -> str:
    text = (text or "").strip()
    return "" if text.upper() == "NONE" else text


def clamp_non_negative(vals):
    if not vals:
        return vals
    return [0 if isinstance(v, (int, float)) and v < 0 else v for v in vals]


def parse_num(text: str):
    try:
        v = float(text)
    except (TypeError, ValueError):
        return None
    if v == int(v):
        return int(v)
    return v


def skill_tables(node: ET.Element) -> dict[str, list[str]]:
    tables: dict[str, list[str]] = {}
    for tbl in node.findall("table"):
        name = tbl.get("name") or ""
        vals = (tbl.text or "").split()
        tables[name] = vals
        if name.startswith("#"):
            tables[name[1:]] = vals
        else:
            tables["#" + name] = vals
    return tables


def direct(node: ET.Element, tag: str) -> ET.Element | None:
    for child in list(node):
        if child.tag.split("}")[-1] == tag:
            return child
    return None


def resolve(node: ET.Element, tag: str, tables: dict[str, list[str]], levels: int, fallback_table: str | None = None):
    """Return a per-level numeric list, or None if Mobius does not publish the field."""
    el = direct(node, tag)
    text = (el.text or "").strip() if el is not None else ""
    if text.startswith("#"):
        vals = tables.get(text) or tables.get(text.lstrip("#"))
        if not vals:
            return None
        return _fit(vals, levels)
    if text:
        n = parse_num(text)
        if n is None:
            return None
        return [n] * levels
    if fallback_table:
        vals = tables.get(fallback_table) or tables.get(fallback_table.lstrip("#"))
        if vals:
            return _fit(vals, levels)
    return None


def _fit(vals: list[str], levels: int):
    nums = [parse_num(v) for v in vals]
    if any(n is None for n in nums):
        return None
    if len(nums) >= levels:
        return nums[:levels]
    # do not invent extra ranks from a short table
    return nums + [nums[-1]] * (levels - len(nums)) if nums else None


def text_of(node: ET.Element, tag: str) -> str:
    el = direct(node, tag)
    return (el.text or "").strip() if el is not None else ""


def bool_of(node: ET.Element, tag: str) -> bool:
    return text_of(node, tag).lower() == "true"


def parse_using(node: ET.Element) -> list[str]:
    kinds: list[str] = []
    seen: set[str] = set()
    for cond in node.iter():
        if cond.tag.split("}")[-1] != "using" or not cond.get("kind"):
            continue
        for raw in cond.get("kind", "").split(","):
            kind = raw.strip().upper()
            if kind and kind not in seen:
                seen.add(kind)
                kinds.append(kind)
    return kinds


def parse_mobius_skills(src: Path) -> dict[int, dict]:
    skills: dict[int, dict] = {}
    for path in sorted(src.glob("*.xml")):
        root = ET.parse(path).getroot()
        for node in root.iter("skill"):
            sid = int(node.get("id"))
            levels = int(node.get("levels", "1"))
            tables = skill_tables(node)
            skills[sid] = {
                "skillId": sid,
                "name": node.get("name") or "",
                "levels": levels,
                "operateType": text_of(node, "operateType"),
                "targetType": text_of(node, "targetType"),
                "isMagic": parse_num(text_of(node, "isMagic")) or 0,
                "isDebuff": bool_of(node, "isDebuff"),
                "overHit": bool_of(node, "overHit"),
                "abnormalType": none_as_empty(text_of(node, "abnormalType")),
                "abnormalVisualEffect": none_as_empty(text_of(node, "abnormalVisualEffect")),
                "trait": text_of(node, "trait"),
                "basicProperty": text_of(node, "basicProperty"),
                "activateRate": parse_num(text_of(node, "activateRate")),
                "lvlBonusRate": parse_num(text_of(node, "lvlBonusRate")),
                "castRange": resolve(node, "castRange", tables, levels),
                "effectRange": resolve(node, "effectRange", tables, levels),
                "hitTime": resolve(node, "hitTime", tables, levels, "#hitTime"),
                "coolTime": resolve(node, "coolTime", tables, levels, "#coolTime"),
                "reuseDelay": resolve(node, "reuseDelay", tables, levels, "#reuseDelay"),
                "mpConsume": resolve(node, "mpConsume", tables, levels, "#mpConsume"),
                "mpInitialConsume": resolve(node, "mpInitialConsume", tables, levels, "#mpInitialConsume"),
                "power": resolve(node, "power", tables, levels, "#power"),
                "magicLevel": clamp_non_negative(resolve(node, "magicLevel", tables, levels, "#magicLevel")),
                "abnormalLevel": resolve(node, "abnormalLevel", tables, levels, "#abnormalLevel"),
                "abnormalTime": resolve(node, "abnormalTime", tables, levels, "#abnormalTime"),
                "effectPoint": resolve(node, "effectPoint", tables, levels, "#effectPoints"),
                "itemConsumeId": resolve(node, "itemConsumeId", tables, levels),
                "itemConsumeCount": resolve(node, "itemConsumeCount", tables, levels),
                "usingKinds": parse_using(node),
                "baseCritRate": parse_num(text_of(node, "baseCritRate")) or 0,
                "hasCastRange": direct(node, "castRange") is not None or "#castRange" in tables or "castRange" in tables,
                "hasEffectRange": direct(node, "effectRange") is not None or "#effectRange" in tables or "effectRange" in tables,
                "hasCoolTime": direct(node, "coolTime") is not None,
            }
    return skills


def first_or_scalar(vals, default=0):
    if not vals:
        return default
    if len(set(vals)) == 1:
        return vals[0]
    return vals[0]


def as_int_list(vals) -> list[int]:
    return [int(v) if not isinstance(v, int) and float(v) == int(v) else int(v) for v in vals]


def as_float_list(vals) -> list[float]:
    return [float(v) for v in vals]


def zeros(n: int) -> list[int]:
    return [0] * n


def apply_combat(row: dict, ms: dict) -> list[str]:
    """Patch an existing skill-combat.json row. Preserve kind / blow / lethal fields."""
    changed = []
    levels = ms["levels"]
    if row.get("name") != ms["name"]:
        row["name"] = ms["name"]
        changed.append("name")
    if row.get("levels") != levels:
        # keep catalog length in sync only when Mobius has more/fewer ranks and we have tables
        row["levels"] = levels
        changed.append("levels")

    if ms["power"] is not None:
        want = as_float_list(ms["power"])
        if row.get("power") != want:
            row["power"] = want
            changed.append("power")
    if ms["mpConsume"] is not None:
        want = as_int_list(ms["mpConsume"])
        if row.get("mpConsume") != want:
            row["mpConsume"] = want
            changed.append("mpConsume")
    want_init = as_int_list(ms["mpInitialConsume"]) if ms["mpInitialConsume"] is not None else zeros(levels)
    if row.get("mpInitialConsume") != want_init:
        row["mpInitialConsume"] = want_init
        changed.append("mpInitialConsume")

    if ms["hitTime"] is not None:
        hit = int(first_or_scalar(ms["hitTime"]))
        if row.get("hitTimeMs") != hit:
            row["hitTimeMs"] = hit
            changed.append("hitTimeMs")
    cool = int(first_or_scalar(ms["coolTime"])) if ms["coolTime"] is not None else 0
    if row.get("coolTimeMs") != cool:
        row["coolTimeMs"] = cool
        changed.append("coolTimeMs")
    if ms["reuseDelay"] is not None:
        reuse = int(first_or_scalar(ms["reuseDelay"]))
        if row.get("reuseDelayMs") != reuse:
            row["reuseDelayMs"] = reuse
            changed.append("reuseDelayMs")

    # Only overwrite castRange when Mobius publishes one. Leave Fineage -1 (AoE/self sentinel) alone
    # when the XML omits the field.
    if ms["hasCastRange"] and ms["castRange"] is not None:
        cr = int(first_or_scalar(ms["castRange"]))
        if row.get("castRange") != cr:
            row["castRange"] = cr
            changed.append("castRange")
    if ms["hasEffectRange"] and ms["effectRange"] is not None:
        er = int(first_or_scalar(ms["effectRange"]))
        if row.get("effectRange") != er:
            row["effectRange"] = er
            changed.append("effectRange")

    if bool(row.get("overHit")) != bool(ms["overHit"]):
        row["overHit"] = bool(ms["overHit"])
        changed.append("overHit")
    if bool(row.get("magic")) != (ms["isMagic"] == 1):
        row["magic"] = ms["isMagic"] == 1
        changed.append("magic")

    if row.get("usingKinds") or ms["usingKinds"]:
        if list(row.get("usingKinds") or []) != list(ms["usingKinds"]):
            if ms["usingKinds"]:
                row["usingKinds"] = list(ms["usingKinds"])
            else:
                row.pop("usingKinds", None)
            changed.append("usingKinds")

    if ms["magicLevel"] is not None:
        want = as_int_list(ms["magicLevel"])
        if row.get("magicLevel") != want:
            row["magicLevel"] = want
            changed.append("magicLevel")

    return changed


def apply_definition(row: dict, ms: dict) -> list[str]:
    changed = []
    levels = ms["levels"]
    if row.get("name") != ms["name"]:
        row["name"] = ms["name"]
        changed.append("name")
    if row.get("levels") != levels:
        row["levels"] = levels
        changed.append("levels")
    if ms["operateType"] and row.get("operateType") != ms["operateType"]:
        row["operateType"] = ms["operateType"]
        changed.append("operateType")
    want_passive = (ms["operateType"] or "").upper() == "P"
    if bool(row.get("passive")) != want_passive:
        row["passive"] = want_passive
        changed.append("passive")
    if ms["targetType"] and row.get("targetType") != ms["targetType"]:
        row["targetType"] = ms["targetType"]
        changed.append("targetType")

    if ms["hasCastRange"] and ms["castRange"] is not None:
        cr = int(first_or_scalar(ms["castRange"]))
        if (row.get("castRange") or 0) != cr:
            row["castRange"] = cr
            changed.append("castRange")
    if ms["hasEffectRange"] and ms["effectRange"] is not None:
        er = int(first_or_scalar(ms["effectRange"]))
        if (row.get("effectRange") or 0) != er:
            row["effectRange"] = er
            changed.append("effectRange")
        if er == 0:
            row.pop("effectRange", None)

    if ms["hitTime"] is not None:
        hit = int(first_or_scalar(ms["hitTime"]))
        if (row.get("hitTimeMs") or 0) != hit:
            row["hitTimeMs"] = hit
            changed.append("hitTimeMs")
    if ms["reuseDelay"] is not None:
        if len(set(ms["reuseDelay"])) == 1:
            reuse = int(ms["reuseDelay"][0])
            if (row.get("reuseDelayMs") or 0) != reuse:
                row["reuseDelayMs"] = reuse
                changed.append("reuseDelayMs")
            # clear stale per-level overrides when the table is flat
            for lv in row.get("levelData") or []:
                if "reuseDelayMs" in lv:
                    lv.pop("reuseDelayMs", None)
                    changed.append("reuseDelayMs.level")
        else:
            # keep template as first; store per-level on levelData
            if (row.get("reuseDelayMs") or 0) != int(ms["reuseDelay"][0]):
                row["reuseDelayMs"] = int(ms["reuseDelay"][0])
                changed.append("reuseDelayMs")

    magic = int(ms["isMagic"] or 0)
    if (row.get("magicType") or 0) != magic:
        if magic:
            row["magicType"] = magic
        else:
            row.pop("magicType", None)
        changed.append("magicType")

    if bool(row.get("isDebuff")) != bool(ms["isDebuff"]):
        if ms["isDebuff"]:
            row["isDebuff"] = True
        else:
            row.pop("isDebuff", None)
        changed.append("isDebuff")

    if (row.get("abnormalType") or "") != (ms["abnormalType"] or ""):
        if ms["abnormalType"]:
            row["abnormalType"] = ms["abnormalType"]
        else:
            row.pop("abnormalType", None)
        changed.append("abnormalType")

    ave = ms["abnormalVisualEffect"]
    if ave and ave != "NONE":
        if (row.get("abnormalVisualEffect") or "") != ave:
            row["abnormalVisualEffect"] = ave
            changed.append("abnormalVisualEffect")
    elif row.get("abnormalVisualEffect"):
        # Mobius NONE/absent: leave Fineage STEALTH and friends alone only if Mobius omitted the field.
        if ave == "NONE":
            row.pop("abnormalVisualEffect", None)
            changed.append("abnormalVisualEffect")

    if (row.get("trait") or "") != (ms["trait"] or ""):
        if ms["trait"]:
            row["trait"] = ms["trait"]
        else:
            row.pop("trait", None)
        changed.append("trait")
    if (row.get("basicProperty") or "") != (ms["basicProperty"] or ""):
        if ms["basicProperty"]:
            row["basicProperty"] = ms["basicProperty"]
        else:
            row.pop("basicProperty", None)
        changed.append("basicProperty")
    if ms["activateRate"] is not None and row.get("activateRate") != ms["activateRate"]:
        row["activateRate"] = int(ms["activateRate"])
        changed.append("activateRate")
    if ms["lvlBonusRate"] is not None and (row.get("lvlBonusRate") or 1) != ms["lvlBonusRate"]:
        row["lvlBonusRate"] = int(ms["lvlBonusRate"])
        changed.append("lvlBonusRate")

    if ms["abnormalTime"] is not None and len(set(ms["abnormalTime"])) == 1:
        want = int(ms["abnormalTime"][0] or 0)
        if (row.get("abnormalTimeSeconds") or 0) != want:
            if want:
                row["abnormalTimeSeconds"] = want
            else:
                row.pop("abnormalTimeSeconds", None)
            changed.append("abnormalTimeSeconds")

    if list(row.get("usingKinds") or []) != list(ms["usingKinds"]):
        if ms["usingKinds"]:
            row["usingKinds"] = list(ms["usingKinds"])
        else:
            row.pop("usingKinds", None)
        changed.append("usingKinds")

    if ms["effectPoint"] is not None:
        want = as_int_list(ms["effectPoint"])
        if row.get("effectPoint") != want:
            row["effectPoint"] = want
            changed.append("effectPoint")

    # per-level tables
    ldata = row.get("levelData")
    if not isinstance(ldata, list) or len(ldata) < levels:
        # do not rebuild the whole array here; only patch existing ranks
        ldata = ldata or []
    for i in range(min(levels, len(ldata) if ldata else 0)):
        lv = ldata[i]
        if lv.get("level") != i + 1:
            continue
        for field, key in (
            ("mpConsume", "mpConsume"),
            ("power", "power"),
            ("magicLevel", "magicLevel"),
            ("mpInitialConsume", "mpInitialConsume"),
        ):
            table = ms[key]
            if table is None:
                continue
            want = table[i] if i < len(table) else table[-1]
            want = int(want)
            got = int(lv.get(field) or 0)
            if got != want:
                if want or field != "mpInitialConsume":
                    lv[field] = want
                else:
                    lv.pop(field, None)
                changed.append(f"level.{field}")
        if ms["reuseDelay"] is not None and len(set(ms["reuseDelay"])) > 1:
            want = int(ms["reuseDelay"][i] if i < len(ms["reuseDelay"]) else ms["reuseDelay"][-1])
            if lv.get("reuseDelayMs") != want:
                lv["reuseDelayMs"] = want
                changed.append("level.reuseDelayMs")
        if ms["itemConsumeId"] is not None:
            want = int(ms["itemConsumeId"][i] if i < len(ms["itemConsumeId"]) else ms["itemConsumeId"][-1])
            if int(lv.get("itemConsumeId") or 0) != want:
                if want:
                    lv["itemConsumeId"] = want
                    if ms["itemConsumeCount"] is not None:
                        lv["itemConsumeCount"] = int(ms["itemConsumeCount"][i] if i < len(ms["itemConsumeCount"]) else 1)
                else:
                    lv.pop("itemConsumeId", None)
                    lv.pop("itemConsumeCount", None)
                changed.append("level.itemConsume")

    return changed


def first_int(vals, default=0):
    if not vals:
        return default
    return int(vals[0] if len(set(vals)) > 1 else vals[0])


def combat_snapshot_row(row: dict, ms: dict) -> dict:
    """Mobius-derived expectations for an existing combat catalog row."""
    levels = ms["levels"]
    snap = {
        "skillId": row["skillId"],
        "levels": levels,
        "kind": row["kind"],
        "magic": ms["isMagic"] == 1,
        "power": as_float_list(ms["power"]) if ms["power"] is not None else [0.0] * levels,
        "mpConsume": as_int_list(ms["mpConsume"]) if ms["mpConsume"] is not None else [0] * levels,
        "mpInitialConsume": as_int_list(ms["mpInitialConsume"]) if ms["mpInitialConsume"] is not None else [0] * levels,
        "hitTimeMs": int(first_or_scalar(ms["hitTime"])) if ms["hitTime"] is not None else row["hitTimeMs"],
        "coolTimeMs": int(first_or_scalar(ms["coolTime"])) if ms["coolTime"] is not None else 0,
        "reuseDelayMs": int(first_or_scalar(ms["reuseDelay"])) if ms["reuseDelay"] is not None else row["reuseDelayMs"],
        "overHit": bool(ms["overHit"]),
        "usingKinds": list(ms["usingKinds"]),
        "magicLevel": as_int_list(ms["magicLevel"]) if ms["magicLevel"] is not None else [],
    }
    # Unpublished Mobius castRange must not force Fineage AoE sentinels (-1) to 0.
    if ms["hasCastRange"] and ms["castRange"] is not None:
        snap["castRange"] = int(first_or_scalar(ms["castRange"]))
    if ms["hasEffectRange"] and ms["effectRange"] is not None:
        snap["effectRange"] = int(first_or_scalar(ms["effectRange"]))
    return snap


def definition_snapshot_row(ms: dict) -> dict:
    ave = ms["abnormalVisualEffect"]
    if ave == "NONE":
        ave = ""
    snap = {
        "skillId": ms["skillId"],
        "operateType": ms["operateType"] or "",
        "targetType": ms["targetType"] or "",
        "hitTimeMs": int(first_or_scalar(ms["hitTime"])) if ms["hitTime"] is not None else None,
        "reuseDelayMs": None if ms["reuseDelay"] is None else (
            as_int_list(ms["reuseDelay"]) if len(set(ms["reuseDelay"])) > 1 else int(ms["reuseDelay"][0])
        ),
        "magicType": int(ms["isMagic"] or 0),
        "isDebuff": bool(ms["isDebuff"]),
        "abnormalType": ms["abnormalType"] or "",
        "abnormalVisualEffect": ave,
        "trait": ms["trait"] or "",
        "basicProperty": ms["basicProperty"] or "",
        "activateRate": int(ms["activateRate"]) if ms["activateRate"] is not None else None,
        "lvlBonusRate": int(ms["lvlBonusRate"]) if ms["lvlBonusRate"] is not None else None,
        "abnormalTimeSeconds": int(ms["abnormalTime"][0]) if ms["abnormalTime"] is not None and len(set(ms["abnormalTime"])) == 1 else None,
        "usingKinds": list(ms["usingKinds"]),
        "effectPoint": as_int_list(ms["effectPoint"]) if ms["effectPoint"] is not None else [],
        "mpConsume": as_int_list(ms["mpConsume"]) if ms["mpConsume"] is not None else [],
        "power": as_int_list(ms["power"]) if ms["power"] is not None else [],
        "magicLevel": as_int_list(ms["magicLevel"]) if ms["magicLevel"] is not None else [],
        "mpInitialConsume": as_int_list(ms["mpInitialConsume"]) if ms["mpInitialConsume"] is not None else [],
    }
    if ms["hasCastRange"] and ms["castRange"] is not None:
        snap["castRange"] = int(first_or_scalar(ms["castRange"]))
    if ms["hasEffectRange"] and ms["effectRange"] is not None:
        snap["effectRange"] = int(first_or_scalar(ms["effectRange"]))
    return snap



def apply_retail_overrides(skills_by: dict[int, dict], def_map: dict | None = None) -> int:
    """Documented retail-client locks that differ from Mobius XML (e.g. skillgrp hit times)."""
    if not RETAIL_OVERRIDES.exists():
        return 0
    data = json.loads(RETAIL_OVERRIDES.read_text())
    rows = data.get("skills") if isinstance(data, dict) else data
    applied = 0
    for row in rows or []:
        sid = int(row["skillId"])
        skill = skills_by.get(sid)
        if skill is None:
            continue
        for key, value in row.items():
            if key in {"skillId", "reason"}:
                continue
            skill[key] = value
            if def_map is not None and sid in def_map:
                def_map[sid][key] = value
        applied += 1
    return applied


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mobius", type=Path, default=DEFAULT_SKILLS)
    ap.add_argument("--family-root", type=int, action="append", default=[])
    ap.add_argument("--class-ids", type=str, default="")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--snapshot-only", action="store_true")
    args = ap.parse_args()

    classes = {c["classId"]: c for c in json.loads(CLASSES.read_text())["classes"]}
    selected: set[int] = set()
    if args.all:
        selected = set(classes)
    if args.class_ids:
        selected.update(int(x) for x in args.class_ids.split(",") if x.strip())
    for root in args.family_root:
        selected.update(descendants(classes, root))
    if not selected:
        print("select --family-root, --class-ids, or --all", file=sys.stderr)
        return 2

    skill_ids = family_skill_ids(selected)
    mobius = parse_mobius_skills(args.mobius)
    missing = sorted(sid for sid in skill_ids if sid not in mobius)
    if missing:
        print(f"Mobius XML missing skill ids {missing[:20]}", file=sys.stderr)
        return 2

    skills_doc = json.loads(SKILLS_JSON.read_text())
    combat_doc = json.loads(COMBAT_JSON.read_text())
    skills_by = {s["skillId"]: s for s in skills_doc["skills"]}
    combat_by = {s["skillId"]: s for s in combat_doc["skills"]}

    def_changed = combat_changed = 0
    if not args.snapshot_only:
        for sid in sorted(skill_ids):
            ms = mobius[sid]
            if sid in skills_by:
                ch = apply_definition(skills_by[sid], ms)
                if ch:
                    def_changed += 1
            if sid in combat_by:
                ch = apply_combat(combat_by[sid], ms)
                if ch:
                    combat_changed += 1
        apply_retail_overrides(skills_by)
        SKILLS_JSON.write_text(json.dumps(skills_doc, indent=2, ensure_ascii=False) + "\n")
        COMBAT_JSON.write_text(json.dumps(combat_doc, indent=1, ensure_ascii=False) + "\n")

    # snapshots: merge selected family into existing files
    def_snap = json.loads(DEF_SNAP.read_text()) if DEF_SNAP.exists() else {"skills": []}
    combat_snap = json.loads(COMBAT_SNAP.read_text()) if COMBAT_SNAP.exists() else {"skills": []}
    if isinstance(def_snap, list):
        def_snap = {"skills": def_snap}
    if isinstance(combat_snap, list):
        combat_snap = {"skills": combat_snap}
    def_map = {s["skillId"]: s for s in def_snap.get("skills", [])}
    combat_map = {s["skillId"]: s for s in combat_snap.get("skills", [])}

    for sid in sorted(skill_ids):
        if sid in skills_by:
            def_map[sid] = definition_snapshot_row(mobius[sid])
        if sid in combat_by:
            combat_map[sid] = combat_snapshot_row(combat_by[sid], mobius[sid])
    apply_retail_overrides(skills_by, def_map)

    def_snap["skills"] = [def_map[k] for k in sorted(def_map)]
    combat_snap["skills"] = [combat_map[k] for k in sorted(combat_map)]
    DEF_SNAP.parent.mkdir(parents=True, exist_ok=True)
    DEF_SNAP.write_text(json.dumps(def_snap, separators=(",", ":"), ensure_ascii=True) + "\n")
    COMBAT_SNAP.write_text(json.dumps(combat_snap, separators=(",", ":"), ensure_ascii=True) + "\n")

    print(f"skills={len(skill_ids)} definitions_updated={def_changed} combat_updated={combat_changed} "
          f"def_snap={len(def_snap['skills'])} combat_snap={len(combat_snap['skills'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
