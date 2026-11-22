#!/usr/bin/env python3
"""Add Mobius CT_0_Interlude weapon gates to data/skills.json as `usingKinds`.

Plain and compound `<cond msgId="113">` / `<conditions>` trees — any `<using kind=".."/>` node counts.
"""
import json
import re
import sys
import pathlib
import xml.etree.ElementTree as ET

from mobius_condition_parse import conditions_tree_from_skill, tag


def collect_using_kinds(node) -> list[str]:
    if not node:
        return []
    op = node.get("op")
    if op == "using":
        return list(node.get("kinds") or [])
    if op in ("and", "or"):
        out: list[str] = []
        for item in node.get("items") or []:
            out.extend(collect_using_kinds(item))
        return out
    if op == "not":
        return collect_using_kinds(node.get("item"))
    return []


def msgid_113_kinds(body: str) -> list[str]:
    for block in re.findall(r'<cond[^>]*msgId="113"[^>]*>(.*?)</cond>', body, re.S):
        kinds: list[str] = []
        for m in re.finditer(r'<using\s+kind="([A-Z_ ,]+)"', block):
            kinds.extend(k.strip() for k in m.group(1).split(",") if k.strip())
        if kinds:
            return kinds
        m = re.fullmatch(r'\s*<using kind="([A-Z_ ,]+)"\s*/>\s*', block)
        if m:
            return [k.strip() for k in m.group(1).split(",") if k.strip()]
    return []


def flatten_kinds(body: str) -> list[str]:
    kinds: list[str] = []
    kinds.extend(msgid_113_kinds(body))
    for m in re.finditer(r'<using\s+kind="([A-Z_ ,]+)"', body):
        kinds.extend(k.strip() for k in m.group(1).split(",") if k.strip())
    try:
        skill_el = ET.fromstring(body)
    except ET.ParseError:
        skill_el = None
    if skill_el is not None and tag(skill_el) == "skill":
        tree = conditions_tree_from_skill(skill_el)
        kinds.extend(collect_using_kinds(tree))
    seen: set[str] = set()
    out: list[str] = []
    for k in kinds:
        ku = k.upper()
        if ku not in seen:
            seen.add(ku)
            out.append(ku)
    return out


def main() -> None:
    src = pathlib.Path(sys.argv[1])
    xml = re.sub(r"<!--.*?-->", "", "".join(p.read_text() for p in sorted(src.glob("*.xml"))), flags=re.S)
    bodies = {
        int(m.group(1)): m.group(0)
        for m in re.finditer(r'<skill id="(\d+)".*?</skill>', xml, re.S)
    }

    data_path = pathlib.Path(__file__).resolve().parent.parent / "data" / "skills.json"
    raw = data_path.read_text()
    data = json.loads(raw)
    indent = 1 if raw.startswith("{\n \"") else 2

    before = sum(1 for s in data["skills"] if s.get("usingKinds"))
    n = 0
    for s in data["skills"]:
        s.pop("usingKinds", None)
        body = bodies.get(s["skillId"])
        if not body:
            continue
        kinds = flatten_kinds(body)
        if kinds:
            s["usingKinds"] = kinds
            n += 1

    data_path.write_text(json.dumps(data, indent=indent, ensure_ascii=False) + "\n")
    print(f"usingKinds on {n} of {len(data['skills'])} skills (was {before})")


if __name__ == "__main__":
    main()