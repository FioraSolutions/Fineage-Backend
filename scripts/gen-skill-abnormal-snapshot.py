#!/usr/bin/env python3
"""Regenerate tests/Fineage.Game.Tests/Fixtures/skill-abnormal-snapshot.json from Mobius
CT_0 Interlude skill XML abnormalType / abnormalLevel (including # tables)."""
from pathlib import Path
import json, xml.etree.ElementTree as ET, sys

def parse_mobius(skill_dir: Path):
    out = {}
    for path in sorted(skill_dir.glob("*.xml")):
        root = ET.parse(path).getroot()
        for skill in root.iter("skill"):
            sid = int(skill.get("id"))
            levels = int(skill.get("levels") or "1")
            tables = {}
            typ = ""
            abn_text = None
            for child in list(skill):
                if child.tag == "table":
                    tables[child.get("name") or ""] = (child.text or "").split()
                elif child.tag == "abnormalType":
                    typ = (child.text or "").strip()
                    if typ.upper() == "NONE":
                        typ = ""
                elif child.tag == "abnormalLevel":
                    abn_text = (child.text or "").strip()
            lvls = []
            for i in range(levels):
                if not abn_text:
                    lvls.append(0)
                elif abn_text.startswith("#"):
                    vals = tables.get(abn_text) or ["0"]
                    lvls.append(int(float(vals[i] if i < len(vals) else vals[-1])))
                else:
                    lvls.append(int(float(abn_text)))
            out[sid] = {"type": typ, "levels": lvls}
    return out

def main():
    repo = Path(__file__).resolve().parents[1]
    skill_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
        "/workspace/mobius/L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills")
    ours = json.loads((repo / "data/skills.json").read_text())["skills"]
    mobius = parse_mobius(skill_dir)
    snap = {}
    for s in ours:
        sid = s["skillId"]
        if sid not in mobius:
            raise SystemExit(f"skill {sid} missing from Mobius XML")
        snap[str(sid)] = mobius[sid]
    dest = repo / "tests/Fineage.Game.Tests/Fixtures/skill-abnormal-snapshot.json"
    dest.write_text(json.dumps(snap, separators=(",", ":"), ensure_ascii=True) + "\n")
    print(f"wrote {dest} ({len(snap)} skills)")

if __name__ == "__main__":
    main()
