#!/usr/bin/env python3
"""Audit Mobius CT_0 skill <conditions> vs Fineage SkillCastConditionEvaluator coverage.

Reads all dist/game/data/stats/skills/*.xml, counts each condition attribute/op, and marks whether
the C# evaluator implements it (see IMPLEMENTED_* sets — keep in sync with SkillCastConditionEvaluator.cs).

Usage:
  python3 tools/audit_skill_cast_conditions.py [mobius_skills_dir]

Prints a markdown table to stdout and writes docs/skill-cast-condition-audit.json.
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Keep in sync with SkillCastConditionEvaluator.cs (+ castCondition import in gen_skill_cast_conditions.py).
IMPLEMENTED_OPS = frozenset({"and", "or", "not", "using", "player", "target", "game"})
IMPLEMENTED_PLAYER = frozenset({
    "invsize", "weight", "hp", "mp", "cp", "charges", "level", "levelrange", "sex", "races",
    "elementseeds", "elementseed", "elementseedcount", "activeeffectid", "activeeffectlevel",
    "activeskillid", "activeskilllevel", "insidezoneid", "cansweep", "cansummon", "cansummonsiegegolem",
    "cancreatebase", "cantakecastle", "canescape", "canresurrect", "callpc", "siegezone", "isridingstrider",
    "flying", "resting", "moving", "running", "standing", "behind", "front", "chaotic", "clanhall",
})
IMPLEMENTED_TARGET = frozenset({
    "race", "npcids", "npcid", "npctype", "mypartyexceptme", "activeskillid", "active_skill_id",
    "level", "invsize", "weight", "aggro", "playable",
})
IMPLEMENTED_GAME = frozenset({"time", "chance"})
IMPLEMENTED_USING = frozenset({"kind", "slot"})

# Mobius XML attribute → JSON/evaluator key (gen_skill_cast_conditions.py).
PLAYER_XML_ALIASES = {
    "insidezoneid": "insidezoneid",
    "active_effect_id_lvl": "activeeffectid",  # paired activeeffectlevel
    "active_skill_id_lvl": "activeskillid",
    "active_effect_id": "activeeffectid",
    "active_skill_id": "activeskillid",
}
TARGET_XML_ALIASES = {
    "npcid": "npcids",
    "active_skill_id": "activeskillid",
}


def normalize_player_attr(raw: str) -> str:
    k = raw.lower()
    return PLAYER_XML_ALIASES.get(k, k)


def normalize_target_attr(raw: str) -> str:
    k = raw.lower()
    return TARGET_XML_ALIASES.get(k, k)


def tag(el: ET.Element) -> str:
    return el.tag.split("}")[-1]


def walk_conditions(root: ET.Element, counts: defaultdict, skill_ids: defaultdict):
    for skill in root.findall("skill"):
        sid = int(skill.get("id", 0))
        cond_el = None
        for child in skill:
            if tag(child) == "conditions":
                cond_el = child
                break
        if cond_el is None:
            continue

        def visit(el: ET.Element):
            t = tag(el).lower()
            counts[f"op:{t}"] += 1
            skill_ids[f"op:{t}"].add(sid)
            if t in ("and", "or"):
                for ch in el:
                    if ch.tag is not None:
                        visit(ch)
            elif t == "not":
                for ch in el:
                    if ch.tag is not None:
                        visit(ch)
                        break
            elif t == "using":
                for k in el.attrib:
                    lk = k.lower()
                    counts[f"using.{lk}"] += 1
                    skill_ids[f"using.{lk}"].add(sid)
            elif t == "player":
                for k in el.attrib:
                    lk = normalize_player_attr(k)
                    counts[f"player.{lk}"] += 1
                    skill_ids[f"player.{lk}"].add(sid)
            elif t == "target":
                for k in el.attrib:
                    lk = normalize_target_attr(k)
                    counts[f"target.{lk}"] += 1
                    skill_ids[f"target.{lk}"].add(sid)
            elif t == "game":
                for k in el.attrib:
                    lk = k.lower()
                    counts[f"game.{lk}"] += 1
                    skill_ids[f"game.{lk}"].add(sid)

        for ch in cond_el:
            if tag(ch) in ("and", "or", "not", "player", "target", "using", "game"):
                visit(ch)


def implemented(kind: str, key: str) -> bool:
    if kind == "op":
        return key in IMPLEMENTED_OPS
    if kind == "player":
        return key in IMPLEMENTED_PLAYER
    if kind == "target":
        return key in IMPLEMENTED_TARGET
    if kind == "game":
        return key in IMPLEMENTED_GAME
    if kind == "using":
        return key in IMPLEMENTED_USING
    return False


def main() -> None:
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else (
        ROOT / ".cache/l2j-mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills"
    )
    if not src.is_dir():
        print(f"Missing skills dir: {src}", file=sys.stderr)
        sys.exit(1)

    counts: defaultdict[str, int] = defaultdict(int)
    skill_ids: defaultdict[str, set[int]] = defaultdict(set)

    for path in sorted(src.glob("*.xml")):
        walk_conditions(ET.parse(path).getroot(), counts, skill_ids)

    rows = []
    for full_key, n in sorted(counts.items(), key=lambda x: (-x[1], x[0])):
        if full_key.startswith("op:"):
            kind, attr = "op", full_key[3:]
        elif "." in full_key:
            kind, attr = full_key.split(".", 1)
        else:
            kind, attr = full_key, ""
        impl = implemented(kind, attr) if attr or kind == "op" else False
        rows.append({
            "conditionType": full_key,
            "skillCount": n,
            "implemented": impl,
            "sampleSkillIds": sorted(skill_ids[full_key])[:12],
        })

    out_path = ROOT / "docs" / "skill-cast-condition-audit.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps({"source": str(src), "rows": rows}, indent=2) + "\n")

    print("| Condition type | Skills using | Implemented |")
    print("|---|---:|---:|")
    for r in rows:
        yn = "Y" if r["implemented"] else "**N**"
        print(f"| `{r['conditionType']}` | {r['skillCount']} | {yn} |")
    print(f"\nWrote {out_path.relative_to(ROOT)} ({len(rows)} rows).")


if __name__ == "__main__":
    main()