#!/usr/bin/env python3
"""Append Mobius skill templates for ids listed in data/npc-ai-support.json when missing from data/skills.json."""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from import_skills import load_skill_templates  # noqa: E402

SKILLS = ROOT / "data" / "skills.json"
NPC_AI = ROOT / "data" / "npc-ai-support.json"


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: merge_npc_ai_skills.py <L2J_Mobius_CT_0_Interlude>")
    mobius = Path(sys.argv[1]).resolve()
    skills_root = mobius / "dist/game/data/stats/skills"
    data = json.loads(SKILLS.read_text())
    by_id = {row["skillId"]: row for row in data["skills"]}
    want: set[int] = set()
    for npc in json.loads(NPC_AI.read_text()).get("npcs") or []:
        for scope in ("heal", "buff", "debuff", "immobilize", "silence", "suicide"):
            for row in npc.get(scope) or []:
                sid = row.get("id")
                if isinstance(sid, int) and sid > 0:
                    want.add(sid)
    missing = sorted(sid for sid in want if sid not in by_id)
    if not missing:
        print("npc-ai skills already present")
        return
    templates = load_skill_templates(skills_root)
    added = []
    for sid in missing:
        if sid not in templates:
            raise SystemExit(f"missing mobius template for npc-ai skill {sid}")
        by_id[sid] = templates[sid]
        added.append(sid)
    data["skills"] = [by_id[k] for k in sorted(by_id)]
    SKILLS.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    print(f"merged npc-ai templates: {added}")


if __name__ == "__main__":
    main()