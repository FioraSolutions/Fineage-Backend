#!/usr/bin/env python3
"""Insert missing skill-stat-effects / skill-passives rows from Mobius CT_0 Interlude XML.

Does not rewrite existing catalog entries (float formatting and trait extras stay put).
Enchant (#ench*) funcs are skipped. Transcribes values only (not GPL source).

Usage:
  sync_skill_stat_effects_from_mobius.py
  sync_skill_stat_effects_from_mobius.py --family-root 0
  sync_skill_stat_effects_from_mobius.py --dry-run
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SKILLS = Path("/workspace/mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills")
STAT_JSON = ROOT / "data/skill-stat-effects.json"
PASSIVE_JSON = ROOT / "data/skill-passives.json"
CLASSES = ROOT / "data/classes.json"
TREES = ROOT / "data/skill-trees.json"

# Must stay in lock-step with SkillStatEffectCatalog.StatNames.
STAT_NAMES = {
    "pAtk", "pDef", "mAtk", "mDef", "pAtkSpd", "mAtkSpd", "accCombat", "rEvas", "critRate",
    "runSpd", "regHp", "regMp", "weightLimit", "weightPenalty", "mCritRate", "mCritPower",
    "critDmg", "critDmgAdd", "blowRate", "pSkillEvas", "critRatePos", "critDmgPos",
    "maxHp", "maxMp", "maxCp", "healEffect", "pReuse", "mReuse", "dReuse", "sDef", "rShld",
    "reflectDam", "reflectSkillPhysic", "reflectSkillMagic", "vengeancePdam", "vengeanceMdam",
    "absorbDam", "physicalMpConsumeRate", "magicalMpConsumeRate", "danceMpConsumeRate",
    "shieldDefAngle", "skillCritical", "skillCriticalProbability", "atkCountMax", "pAtkRange",
    "breath", "fall", "cancelVuln", "debuffVuln", "holyPower", "darkRes",
}


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


def load_classes() -> dict[int, dict]:
    raw = json.loads(CLASSES.read_text())
    rows = raw["classes"] if isinstance(raw, dict) and "classes" in raw else raw
    return {int(c["classId"]): c for c in rows}


def family_skill_ids(root: int) -> set[int]:
    wanted = set(descendants(load_classes(), root))
    trees = json.loads(TREES.read_text())
    ids: set[int] = set()
    for tree in trees["trees"]:
        if int(tree.get("classId", -1)) not in wanted:
            continue
        for row in tree.get("skills") or []:
            if "skillId" in row:
                ids.add(int(row["skillId"]))
    return ids


def parse_table_values(text: str) -> list[float]:
    vals = []
    for x in text.split():
        try:
            vals.append(float(x))
        except ValueError:
            continue
    return vals


def tables_of(skill: ET.Element) -> dict[str, list[float]]:
    out = {}
    for t in skill.findall("table"):
        name = t.get("name") or ""
        out[name] = parse_table_values(t.text or "")
    return out


def resolve(raw: str | None, tables: dict[str, list[float]], levels: int) -> list[float] | None:
    if raw is None:
        return None
    raw = raw.strip()
    if not raw:
        return None
    if raw.startswith("#"):
        vals = tables.get(raw)
        if vals is None:
            # some tables stored without hash
            vals = tables.get(raw[1:])
        if vals is None:
            return None
        if len(vals) < levels:
            return vals + [vals[-1]] * (levels - len(vals)) if vals else None
        return vals[:levels]
    try:
        v = float(raw)
    except ValueError:
        return None
    return [v] * levels


def is_enchant_ref(raw: str | None) -> bool:
    return bool(raw) and "ench" in raw.lower()


def func_meta(el: ET.Element) -> tuple[str | None, list[str], int | None, dict[str, bool]]:
    """Return (value-ref-or-text, using kinds, hpBelow, player flags)."""
    using: list[str] = []
    hp = None
    flags: dict[str, bool] = {}
    raw = (el.text or "").strip() or None

    def take_player(p: ET.Element) -> None:
        nonlocal hp
        if p.get("hp") is not None:
            hp = int(float(p.get("hp")))
        for key in ("resting", "moving", "standing"):
            if p.get(key) is not None:
                flags[key] = p.get(key, "").strip().lower() == "true"

    for ch in list(el):
        tag = ch.tag.split("}")[-1]
        if tag == "value":
            raw = (ch.text or "").strip() or raw
            for gch in list(ch):
                gt = gch.tag.split("}")[-1]
                if gt == "using" and gch.get("kind"):
                    using.extend(k.upper() for k in gch.get("kind", "").split())
                if gt == "player":
                    take_player(gch)
        elif tag == "using" and ch.get("kind"):
            using.extend(k.upper() for k in ch.get("kind", "").split())
        elif tag == "player":
            take_player(ch)
    return raw, using, hp, flags


def parse_skill(skill: ET.Element) -> dict | None:
    sid = int(skill.get("id"))
    name = skill.get("name") or ""
    levels = int(skill.get("levels") or 1)
    op_type = ""
    for c in skill:
        if c.tag.split("}")[-1] == "operateType":
            op_type = (c.text or "").strip()
    tables = tables_of(skill)
    effects: list[dict] = []
    seen_stat_ops: set[tuple] = set()
    root_effects = skill.find("effects")
    if root_effects is None:
        return None
    for effect in root_effects.findall("effect"):
        for el in effect.iter():
            tag = el.tag.split("}")[-1]
            if tag not in ("add", "mul", "sub", "set"):
                continue
            stat = el.get("stat") or ""
            if stat not in STAT_NAMES:
                continue
            raw, using, hp, flags = func_meta(el)
            if is_enchant_ref(raw):
                continue
            values = resolve(raw, tables, levels)
            if values is None:
                continue
            # Skip obvious enchant tables even if resolved via a non-hash name
            if raw and is_enchant_ref(raw):
                continue
            key = (stat, tag, tuple(values), tuple(using), hp, tuple(sorted(flags.items())))
            if key in seen_stat_ops:
                continue
            seen_stat_ops.add(key)
            row: dict = {"stat": stat, "op": tag, "values": values}
            if using:
                row["using"] = using
            if hp is not None:
                row["hpBelow"] = hp
            row.update(flags)
            effects.append(row)
    if not effects:
        return None
    return {
        "skillId": sid,
        "name": name,
        "levels": levels,
        "operateType": op_type,
        "effects": effects,
    }


def load_xml_skills(skills_dir: Path) -> dict[int, dict]:
    out: dict[int, dict] = {}
    for fp in sorted(skills_dir.glob("*.xml")):
        try:
            root = ET.parse(fp).getroot()
        except ET.ParseError as e:
            print(f"warn: skip {fp}: {e}", file=sys.stderr)
            continue
        for skill in root.iter("skill"):
            parsed = parse_skill(skill)
            if parsed:
                out[parsed["skillId"]] = parsed
    return out


def existing_ids(path: Path) -> set[int]:
    data = json.loads(path.read_text())
    return {int(s["skillId"]) for s in data["skills"]}


def format_skill_object(skill: dict) -> str:
    payload = {
        "skillId": skill["skillId"],
        "name": skill["name"],
        "levels": skill["levels"],
        "effects": skill["effects"],
    }
    dumped = json.dumps(payload, indent=1, ensure_ascii=False)
    # Array items in an indent=1 file are indented two spaces.
    return "\n".join("  " + line for line in dumped.splitlines())


def insert_skills(path: Path, new_skills: list[dict], dry_run: bool) -> int:
    if not new_skills:
        return 0
    text = path.read_text()
    data = json.loads(text)
    existing = [int(s["skillId"]) for s in data["skills"]]
    # Map id -> index of `"skillId": N` object start in text via regex walk is brittle.
    # Insert by rewriting only the skills array brackets using id-ordered splice of formatted objects.
    # Locate each existing object start/end by finding `"skillId": <id>` at the object level.
    # Safer: split on the skills array.
    m = re.search(r'\n "skills": \[\n', text)
    if not m:
        raise SystemExit(f"{path} has unexpected skills array header")
    start = m.end()
    end = text.rstrip().rfind("\n ]")
    if end < start:
        raise SystemExit(f"{path} has unexpected skills array footer")
    # The file ends with ` ]\n}\n` typically
    footer_at = text.rfind("\n ]")
    body = text[start:footer_at]
    # Existing objects start with `  {\n` and are separated by `,\n`
    # We'll rebuild body from existing chunks + new ones.
    # Split existing objects: they are "  { ... }" joined by ",\n"
    objects: list[tuple[int, str]] = []
    # Parse with a small scanner: each object starts at '  {' 
    i = 0
    n = len(body)
    while i < n:
        while i < n and body[i] in " \n\t,":
            i += 1
        if i >= n:
            break
        if body[i] != "{":
            raise SystemExit(f"unexpected token at {i}: {body[i:i+40]!r}")
        depth = 0
        j = i
        in_str = False
        esc = False
        while j < n:
            ch = body[j]
            if in_str:
                if esc:
                    esc = False
                elif ch == "\\":
                    esc = True
                elif ch == '"':
                    in_str = False
            else:
                if ch == '"':
                    in_str = True
                elif ch == "{":
                    depth += 1
                elif ch == "}":
                    depth -= 1
                    if depth == 0:
                        j += 1
                        break
            j += 1
        chunk = body[i:j]
        mid = re.search(r'"skillId":\s*(\d+)', chunk)
        if not mid:
            raise SystemExit(f"no skillId in chunk {chunk[:80]!r}")
        # Preserve the catalog's two-space object indent.
        if not chunk.startswith("  "):
            chunk = "  " + chunk
        objects.append((int(mid.group(1)), chunk))
        i = j

    have = {sid for sid, _ in objects}
    for sk in new_skills:
        if sk["skillId"] in have:
            continue
        objects.append((sk["skillId"], format_skill_object(sk)))
        have.add(sk["skillId"])
    objects.sort(key=lambda x: x[0])
    new_body = ",\n".join(chunk for _, chunk in objects)
    new_text = text[:start] + new_body + text[footer_at:]
    if not dry_run:
        path.write_text(new_text if new_text.endswith("\n") else new_text + "\n")
    return len(new_skills)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skills-dir", type=Path, default=DEFAULT_SKILLS)
    ap.add_argument("--family-root", type=int)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    if not args.skills_dir.is_dir():
        print(f"skills dir missing: {args.skills_dir}", file=sys.stderr)
        return 2
    parsed = load_xml_skills(args.skills_dir)
    wanted = family_skill_ids(args.family_root) if args.family_root is not None else None
    existing_stat = existing_ids(STAT_JSON)
    existing_pas = existing_ids(PASSIVE_JSON)
    add_stat: list[dict] = []
    add_pas: list[dict] = []
    for sid, sk in sorted(parsed.items()):
        if wanted is not None and sid not in wanted:
            continue
        op = (sk.get("operateType") or "").upper()
        if op == "P":
            if sid not in existing_pas:
                add_pas.append(sk)
        else:
            if sid not in existing_stat:
                add_stat.append(sk)
    print(f"parsed {len(parsed)} skills with supported funcs")
    print(f"insert passives: {len(add_pas)} ids={ [s['skillId'] for s in add_pas][:30] }{'...' if len(add_pas)>30 else ''}")
    print(f"insert stat-effects: {len(add_stat)} ids={ [s['skillId'] for s in add_stat][:30] }{'...' if len(add_stat)>30 else ''}")
    if args.dry_run:
        for s in add_pas + add_stat:
            print(f"  {s['operateType'] or '?':2} {s['skillId']:5} {s['name']}: {s['effects']}")
        return 0
    n1 = insert_skills(PASSIVE_JSON, add_pas, dry_run=False)
    n2 = insert_skills(STAT_JSON, add_stat, dry_run=False)
    print(f"wrote {n1} passives -> {PASSIVE_JSON}")
    print(f"wrote {n2} stat-effects -> {STAT_JSON}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
