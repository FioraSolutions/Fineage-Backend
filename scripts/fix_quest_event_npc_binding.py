#!/usr/bin/env python3
"""Rebind auto-ported quest events to the NPC id encoded in the Mobius event name (Q event page)."""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402

EVENT_NPC = re.compile(r"^(\d{5})[-_]")


def rebind_quest(quest: dict) -> int:
    fixed = 0
    for d in quest.get("dialogs", []):
        if d.get("hook") != "event":
            continue
        ev = str(d.get("event") or "")
        m = EVENT_NPC.match(ev)
        if not m:
            continue
        npc = int(m.group(1))
        if d.get("npcId") != npc:
            d["npcId"] = npc
            fixed += 1
    return fixed


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("quest_ids", nargs="*", type=int, help="default: every pack under data/quests")
    args = ap.parse_args()
    ids = args.quest_ids
    if not ids:
        ids = sorted(int(p.name[:5]) for p in questio.QUESTS_DIR.glob("[0-9]*_*") if (p / "quest.json").is_file())
    total = 0
    for qid in ids:
        folder = questio.find_quest_dir(qid)
        if not folder:
            continue
        quest = json.loads((folder / "quest.json").read_text(encoding="utf-8"))
        n = rebind_quest(quest)
        if not n:
            continue
        out = questio.externalize_html(quest, folder)
        (folder / "quest.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"{qid}: rebound {n} event(s)")
        total += n
    print("total rebound", total)


if __name__ == "__main__":
    main()