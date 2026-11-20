#!/usr/bin/env python3
"""Copy Mobius CT_0 Interlude class skill-tree rows into data/skill-trees.json.

Updates levelUpSp, requiredItems, and preReqSkills for the selected class IDs
(matched by classId + skillId + level). Also writes the Mobius-derived snapshot
used by SkillTreeParityTests.

This transcribes values from the Mobius datapack; it does not copy GPL source.

Usage:
  sync_skill_trees_from_mobius.py --family-root 0
  sync_skill_trees_from_mobius.py --class-ids 0,1,2
  sync_skill_trees_from_mobius.py --all
  sync_skill_trees_from_mobius.py --snapshot-only --family-root 0
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MOBIUS = Path("/workspace/mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/players/skillTrees")
SNAP = ROOT / "tests/Fineage.Game.Tests/Fixtures/skill-tree-snapshot.json"
TREES = ROOT / "data/skill-trees.json"
CLASSES = ROOT / "data/classes.json"
ITEMS = ROOT / "data/items.json"


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


def parse_mobius(src: Path) -> dict[int, dict[tuple[int, int], dict]]:
    trees: dict[int, dict[tuple[int, int], dict]] = {}
    for path in src.rglob("*.xml"):
        root = ET.parse(path).getroot()
        for tree in root.iter("skillTree"):
            if tree.get("type") and tree.get("type") != "classSkillTree":
                continue
            if tree.get("classId") is None:
                continue
            cid = int(tree.get("classId"))
            bucket = trees.setdefault(cid, {})
            for skill in tree.iter("skill"):
                sid = int(skill.get("skillId"))
                level = int(skill.get("skillLevel", skill.get("level", "1")))
                items = []
                prereqs = []
                for child in skill:
                    tag = child.tag.split("}")[-1]
                    if tag == "item":
                        items.append({
                            "itemId": int(child.get("id")),
                            "count": int(child.get("count", "1")),
                        })
                    elif tag == "preRequisiteSkill":
                        prereqs.append({
                            "skillId": int(child.get("id")),
                            "level": int(child.get("lvl", "1")),
                        })
                bucket[(sid, level)] = {
                    "skillId": sid,
                    "level": level,
                    "getLevel": int(skill.get("getLevel", "1")),
                    "autoGet": (skill.get("autoGet") or "false").lower() == "true",
                    "learnedByNpc": (skill.get("learnedByNpc") or "false").lower() == "true",
                    "levelUpSp": int(skill.get("levelUpSp") or 0),
                    "requiredItems": items,
                    "preReqSkills": prereqs,
                    "hasSpAttr": skill.get("levelUpSp") is not None,
                }
    return trees


def snapshot_row(ms: dict) -> dict:
    row = {
        "skillId": ms["skillId"],
        "level": ms["level"],
        "getLevel": ms["getLevel"],
        "autoGet": ms["autoGet"],
        "learnedByNpc": ms["learnedByNpc"],
        "levelUpSp": ms["levelUpSp"] if ms.get("hasSpAttr") else 0,
    }
    if ms["requiredItems"]:
        row["requiredItems"] = ms["requiredItems"]
    if ms["preReqSkills"]:
        row["preReqSkills"] = ms["preReqSkills"]
    return row


def apply_row(row: dict, ms: dict) -> None:
    row["getLevel"] = ms["getLevel"]
    row["autoGet"] = ms["autoGet"]
    row["learnedByNpc"] = ms["learnedByNpc"]
    if ms.get("hasSpAttr"):
        row["levelUpSp"] = ms["levelUpSp"]
    else:
        row.pop("levelUpSp", None)
    if ms["requiredItems"]:
        row["requiredItems"] = ms["requiredItems"]
    else:
        row.pop("requiredItems", None)
    if ms["preReqSkills"]:
        row["preReqSkills"] = ms["preReqSkills"]
    else:
        row.pop("preReqSkills", None)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mobius", type=Path, default=DEFAULT_MOBIUS)
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

    mobius = parse_mobius(args.mobius)
    missing_classes = sorted(cid for cid in selected if cid not in mobius)
    if missing_classes:
        print(f"no Mobius class skill tree for classIds {missing_classes}", file=sys.stderr)
        return 2

    data = json.loads(TREES.read_text())
    item_ids = {item["id"] for item in json.loads(ITEMS.read_text())["items"]}

    sp_set = items_set = prereq_set = 0
    missing_items: list[str] = []
    if not args.snapshot_only:
        by_class = {t["classId"]: t for t in data["trees"]}
        for cid in sorted(selected):
            tree = by_class.get(cid)
            if tree is None:
                print(f"Fineage has no tree for class {cid}", file=sys.stderr)
                return 2
            mby = mobius[cid]
            fby = {(s["skillId"], s["level"]): s for s in tree["skills"]}
            only_m = sorted(set(mby) - set(fby))
            only_f = sorted(set(fby) - set(mby))
            if only_m or only_f:
                print(f"class {cid} membership differs mobius-only={only_m[:8]} fine-only={only_f[:8]}",
                      file=sys.stderr)
                return 2
            for key, ms in mby.items():
                apply_row(fby[key], ms)
                if ms.get("hasSpAttr"):
                    sp_set += 1
                if ms["requiredItems"]:
                    items_set += 1
                    for it in ms["requiredItems"]:
                        if it["itemId"] not in item_ids:
                            missing_items.append(
                                f"class {cid} skill {key[0]} lv{key[1]} item {it['itemId']}")
                if ms["preReqSkills"]:
                    prereq_set += 1
        if missing_items:
            print("required items missing from data/items.json:", file=sys.stderr)
            for line in missing_items:
                print(" ", line, file=sys.stderr)
            return 2
        TREES.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")

    # Merge selected classes into the snapshot (leave other families untouched).
    snap = {}
    if SNAP.exists():
        snap = json.loads(SNAP.read_text())
    for cid in sorted(selected):
        rows = [snapshot_row(mobius[cid][k]) for k in sorted(mobius[cid])]
        snap[str(cid)] = rows
    # stable key order
    snap = {k: snap[k] for k in sorted(snap, key=lambda x: int(x))}
    SNAP.parent.mkdir(parents=True, exist_ok=True)
    SNAP.write_text(json.dumps(snap, separators=(",", ":"), ensure_ascii=True) + "\n")

    print(f"classes={len(selected)} sp_rows={sp_set} requiredItems_rows={items_set} "
          f"preReq_rows={prereq_set} snapshot={SNAP} ({sum(len(v) for v in snap.values())} rows)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
