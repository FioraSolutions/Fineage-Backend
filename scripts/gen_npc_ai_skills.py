#!/usr/bin/env python3
"""Patch data/npcs.json with Mobius AI attack skills (skillList ∩ skill-combat.json).

Also copies ai minChance/maxChance (defaults 7/15). Passive for AttackableAI
skill casts vs players.

Usage: gen_npc_ai_skills.py <mobius dist/game/data>
"""
from __future__ import annotations

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
COMBAT = json.loads((ROOT / "data" / "skill-combat.json").read_text())
ATTACK_KINDS = {"physical", "magical", "blow", "energy"}
COMBAT_IDS = {
    int(s["skillId"])
    for s in COMBAT["skills"]
    if (s.get("kind") or "physical").lower() in ATTACK_KINDS
}


def main() -> None:
    if len(sys.argv) != 2:
        print("Usage: gen_npc_ai_skills.py <mobius dist/game/data>", file=sys.stderr)
        sys.exit(2)
    npc_dir = pathlib.Path(sys.argv[1]) / "stats" / "npcs"
    by_id: dict[int, dict] = {}
    for f in sorted(npc_dir.glob("*.xml")):
        text = re.sub(r"<!--.*?-->", "", f.read_text(), flags=re.S)
        for m in re.finditer(r'<npc id="(\d+)"[^>]*>.*?</npc>', text, re.S):
            nid, body = int(m.group(1)), m.group(0)
            row: dict = {}
            ai = re.search(r"<ai\b([^>]*)>", body)
            if ai:
                attrs = ai.group(1)
                # nested <ai><skill minChance=…/></ai>
                nest = re.search(
                    r"<ai\b[^>]*>.*?<skill\b([^>]*)/?>",
                    body,
                    re.S,
                )
                src = nest.group(1) if nest else attrs
                def attr(name: str, default: int | None = None) -> int | None:
                    x = re.search(rf'\b{name}="(\d+)"', src)
                    return int(x.group(1)) if x else default

                mn = attr("minChance")
                mx = attr("maxChance")
                if mn is not None:
                    row["minSkillChance"] = mn
                if mx is not None:
                    row["maxSkillChance"] = mx

            sl = re.search(r"<skillList>(.*?)</skillList>", body, re.S)
            skills = []
            if sl:
                for sm in re.finditer(
                    r'<skill\b[^>]*\bid="(\d+)"[^>]*\blevel="(\d+)"', sl.group(1)
                ):
                    sid, lvl = int(sm.group(1)), int(sm.group(2))
                    if sid in COMBAT_IDS:
                        skills.append({"id": sid, "level": lvl})
            if skills:
                row["aiSkills"] = skills
            if row:
                by_id[nid] = row

    path = ROOT / "data" / "npcs.json"
    data = json.loads(path.read_text())
    n = 0
    for npc in data["npcs"]:
        for k in ("aiSkills", "minSkillChance", "maxSkillChance"):
            npc.pop(k, None)
        patch = by_id.get(npc["npcId"])
        if patch:
            npc.update(patch)
            n += 1

    lines = ["{", f'  "source": {json.dumps(data.get("source", ""))},', '  "npcs": [']
    for i, npc in enumerate(data["npcs"]):
        comma = "," if i + 1 < len(data["npcs"]) else ""
        lines.append(
            "    " + json.dumps(npc, ensure_ascii=False, separators=(", ", ": ")) + comma
        )
    lines.append("  ]")
    lines.append("}")
    path.write_text("\n".join(lines) + "\n")
    print(
        f"aiSkills on {n}/{len(data['npcs'])} npcs "
        f"({sum(1 for v in by_id.values() if v.get('aiSkills'))} with attack skills, "
        f"{len(COMBAT_IDS)} combat catalog ids)"
    )


if __name__ == "__main__":
    main()
