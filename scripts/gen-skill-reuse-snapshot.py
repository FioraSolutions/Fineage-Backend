#!/usr/bin/env python3
"""Regenerate tests/Fineage.Game.Tests/Fixtures/skill-reuse-snapshot.json from Mobius
CT_0 Interlude skill XML reuseDelay / #reuseDelay tables. Writes numeric values only."""
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
            reuse_text = None
            for child in list(skill):
                if child.tag == "table":
                    tables[child.get("name") or ""] = (child.text or "").split()
                elif child.tag == "reuseDelay":
                    reuse_text = (child.text or "").strip()
            reuses = []
            for i in range(levels):
                if not reuse_text:
                    reuses.append(0)
                elif reuse_text.startswith("#"):
                    vals = tables.get(reuse_text) or ["0"]
                    reuses.append(int(float(vals[i] if i < len(vals) else vals[-1])))
                else:
                    reuses.append(int(float(reuse_text)))
            out[sid] = reuses
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
        reuses = mobius[sid]
        n = s.get("levels") or len(s["levelData"])
        snap[str(sid)] = reuses[:n]
    dest = repo / "tests/Fineage.Game.Tests/Fixtures/skill-reuse-snapshot.json"
    dest.write_text(json.dumps(snap, separators=(",", ":"), ensure_ascii=False) + "\n")
    print(f"wrote {dest} ({len(snap)} skills)")

if __name__ == "__main__":
    main()
