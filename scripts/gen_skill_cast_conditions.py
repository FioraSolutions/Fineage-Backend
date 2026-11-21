#!/usr/bin/env python3
"""Import Mobius CT_0_Interlude skill <conditions> trees into data/skills.json as `castCondition`.

Transcribes and/or/not, using, player, target, and game nodes (Mobius DocumentBase.parseCondition).
Skips per-skill custom msgId blocks outside <conditions> (item func conds stay in stat-effect import).

Usage: gen_skill_cast_conditions.py <mobius dist/game/data/stats/skills dir>
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKILLS_JSON = ROOT / "data" / "skills.json"


def tag(el: ET.Element) -> str:
    return el.tag.split("}")[-1]


def parse_bool(raw: str | None) -> bool:
    return (raw or "").strip().lower() in ("true", "1", "yes")


def parse_int(raw: str | None) -> int | None:
    if raw is None or not str(raw).strip():
        return None
    try:
        return int(str(raw).strip(), 0)
    except ValueError:
        return None


def player_node(el: ET.Element) -> dict | None:
    attrs = {k.lower(): v for k, v in el.attrib.items()}
    if not attrs:
        return None
    out: dict = {"op": "player"}
    for key, val in attrs.items():
        if key in ("cansummon", "cansummonsiegegolem", "cancreatebase", "canescape", "canresurrect",
                   "cansweep", "cantakecastle", "callpc", "siegezone", "isridingstrider",
                   "flying", "chaotic", "resting", "moving", "running", "standing", "behind", "front"):
            out[key] = parse_bool(val)
        elif key in ("charges", "invsize", "weight", "hp", "mp", "level", "sex", "clanhall"):
            n = parse_int(val)
            if n is not None:
                out[key] = n
        elif key == "levelrange":
            parts = [p.strip() for p in val.split(";") if p.strip()]
            if len(parts) == 2:
                lo, hi = parse_int(parts[0]), parse_int(parts[1])
                if lo is not None and hi is not None:
                    out["levelRange"] = [lo, hi]
        elif key == "insidezoneid":
            n = parse_int(val)
            if n is not None:
                out["insideZoneId"] = n
        elif key == "races":
            out["races"] = [r.strip().upper() for r in val.split(",") if r.strip()]
        elif key == "elementseeds":
            n = parse_int(val)
            if n is not None:
                out["elementSeeds"] = n
        elif key == "elementseed":
            parts = [p.strip() for p in val.split(",") if p.strip()]
            if parts:
                out["elementSeed"] = parts[0].upper()
                if len(parts) > 1:
                    n = parse_int(parts[1])
                    if n is not None:
                        out["elementSeedCount"] = n
        elif key == "active_effect_id_lvl":
            parts = [p.strip() for p in val.split(",") if p.strip()]
            if parts:
                sid = parse_int(parts[0])
                if sid is not None:
                    out["activeEffectId"] = sid
                    if len(parts) > 1:
                        lv = parse_int(parts[1])
                        if lv is not None:
                            out["activeEffectLevel"] = lv
        elif key == "active_skill_id_lvl":
            parts = [p.strip() for p in val.split(",") if p.strip()]
            sid = parse_int(parts[0] if parts else None)
            if sid is not None:
                out["activeSkillId"] = sid
                if len(parts) > 1:
                    lv = parse_int(parts[1])
                    if lv is not None:
                        out["activeSkillLevel"] = lv
        else:
            out[key] = val
    return out if len(out) > 1 else None


def target_node(el: ET.Element) -> dict | None:
    attrs = {k.lower(): v for k, v in el.attrib.items()}
    if not attrs:
        return None
    out: dict = {"op": "target"}
    for key, val in attrs.items():
        if key == "race":
            out["race"] = val.strip().upper()
        elif key == "npcid":
            out["npcIds"] = [int(x.strip()) for x in val.split(";") if x.strip().isdigit()]
        elif key == "npctype":
            out["npcType"] = val.strip()
        elif key == "mypartyexceptme":
            out["myPartyExceptMe"] = parse_bool(val)
        elif key == "active_skill_id":
            n = parse_int(val)
            if n is not None:
                out["activeSkillId"] = n
        else:
            out[key] = val
    return out if len(out) > 1 else None


def using_node(el: ET.Element) -> dict | None:
    kind = el.get("kind")
    slot = el.get("slot")
    if kind:
        kinds = [k.strip().upper() for k in kind.split(",") if k.strip()]
        return {"op": "using", "kinds": kinds}
    if slot:
        slots = [s.strip().lower() for s in slot.split(",") if s.strip()]
        return {"op": "using", "slots": slots}
    return None


def game_node(el: ET.Element) -> dict | None:
    attrs = {k.lower(): v for k, v in el.attrib.items()}
    if not attrs:
        return None
    out: dict = {"op": "game"}
    for key, val in attrs.items():
        if key == "time":
            out["time"] = val.strip().lower()
        elif key == "chance":
            n = parse_int(val)
            if n is not None:
                out["chance"] = n
        else:
            out[key] = val
    return out if len(out) > 1 else None


def parse_condition(el: ET.Element | None) -> dict | None:
    if el is None:
        return None
    t = tag(el)
    if t == "and":
        kids = [parse_condition(ch) for ch in el if tag(ch) in ("and", "or", "not", "player", "target", "using", "game")]
        kids = [k for k in kids if k]
        return {"op": "and", "items": kids} if kids else None
    if t == "or":
        kids = [parse_condition(ch) for ch in el if tag(ch) in ("and", "or", "not", "player", "target", "using", "game")]
        kids = [k for k in kids if k]
        return {"op": "or", "items": kids} if kids else None
    if t == "not":
        for ch in el:
            if tag(ch) in ("and", "or", "not", "player", "target", "using", "game"):
                inner = parse_condition(ch)
                return {"op": "not", "item": inner} if inner else None
        return None
    if t == "player":
        return player_node(el)
    if t == "target":
        return target_node(el)
    if t == "using":
        return using_node(el)
    if t == "game":
        return game_node(el)
    return None


def main() -> None:
    src = Path(sys.argv[1])
    raw = SKILLS_JSON.read_text()
    data = json.loads(raw)
    indent = 1 if raw.startswith("{\n \"") else 2

    bodies: dict[int, ET.Element] = {}
    for path in sorted(src.glob("*.xml")):
        root = ET.parse(path).getroot()
        for node in root.findall("skill"):
            sid = int(node.get("id"))
            bodies[sid] = node

    n = 0
    for row in data.get("skills", []):
        row.pop("castCondition", None)
        sid = row.get("skillId")
        if sid not in bodies:
            continue
        skill = bodies[sid]
        cond_el = None
        for child in skill:
            if tag(child) == "conditions":
                cond_el = child
                break
        if cond_el is None:
            continue
        # Mobius uses one root child under conditions (often AND with children).
        roots = [ch for ch in cond_el if tag(ch) in ("and", "or", "not", "player", "target", "using", "game")]
        if len(roots) == 1:
            tree = parse_condition(roots[0])
        else:
            parts = [parse_condition(ch) for ch in roots]
            parts = [p for p in parts if p]
            tree = {"op": "and", "items": parts} if parts else None
        if tree:
            row["castCondition"] = tree
            n += 1

    SKILLS_JSON.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
    print(f"castCondition on {n} skills in {SKILLS_JSON.name}")


if __name__ == "__main__":
    main()