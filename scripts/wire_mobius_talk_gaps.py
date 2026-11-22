#!/usr/bin/env python3
"""Add minimal talk hooks for Mobius addTalkId NPCs missing from a quest pack."""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402
from quest_pack_809_helpers import mobius_talk_ids  # noqa: E402

QROOT = questio.mobius_quest_root()


def talk_npc_ids(quest: dict) -> set[int]:
    return {
        d["npcId"]
        for d in quest.get("dialogs", [])
        if d.get("hook") in ("talk", "event", "first")
    }


def mobius_page(script: str, qid: int, npc: int) -> str:
    folder = QROOT / script
    for pat in (f"{npc}-01.htm", f"{npc}-01.html", f"{npc}-1.htm"):
        if (folder / pat).is_file():
            return questio.mobius_quest_html(QROOT, script, qid, pat)
    for p in sorted(folder.glob(f"{npc}-*.htm")):
        return questio.mobius_quest_html(QROOT, script, qid, p.name)
    raise SystemExit(f"{script}: no Mobius HTML for talk npc {npc}")


def wire_quest(qid: int) -> str:
    folder = questio.find_quest_dir(qid)
    if not folder:
        raise SystemExit(f"no pack for {qid}")
    script = next(d.name for d in QROOT.iterdir() if d.name.startswith(f"Q{qid:05d}_"))
    quest = json.loads((folder / "quest.json").read_text(encoding="utf-8"))
    have = talk_npc_ids(quest)
    need = set(mobius_talk_ids(script)) - have
    if not need:
        return f"{qid}: talk ok"
    added = 0
    for npc in sorted(need):
        try:
            html = mobius_page(script, qid, npc)
        except SystemExit:
            html = "<html><body>You are either not on a quest that involves this NPC, or you do not meet this NPC's minimum quest requirements.</body></html>"
        quest.setdefault("dialogs", []).append(
            {
                "npcId": npc,
                "hook": "talk",
                "when": "started",
                "html": html,
            }
        )
        added += 1
    out = questio.externalize_html(quest, folder)
    (folder / "quest.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return f"{qid}: added {added} talk stub(s)"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("quest_ids", nargs="+", type=int)
    args = ap.parse_args()
    for qid in args.quest_ids:
        print(wire_quest(qid))


if __name__ == "__main__":
    main()