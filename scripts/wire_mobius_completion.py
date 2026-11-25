#!/usr/bin/env python3
"""Append complete ops to quest events that call Mobius exitQuest(false, true)."""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402
from quest_pack_809_helpers import mobius_java  # noqa: E402

DONE = {"op": "complete"}


def exit_talk_pages(script: str) -> set[str]:
    src = mobius_java(script)
    pages: set[str] = set()
    for m in re.finditer(
        r"exitQuest\s*\([^)]*\)\s*;\s*htmltext\s*=\s*\"([^\"]+)\"",
        src,
        re.S,
    ):
        pages.add(re.sub(r"\.html?$", "", m.group(1)))
    for m in re.finditer(
        r"exitQuest\s*\([^)]*\)\s*;[^}]*htmltext\s*=\s*\"([^\"]+)\"",
        src,
        re.S,
    ):
        pages.add(re.sub(r"\.html?$", "", m.group(1)))
    return pages


def exit_events(script: str) -> set[str]:
    src = mobius_java(script)
    events: set[str] = set()
    for block in re.finditer(r'case\s+"([^"]+)"\s*:\s*\{([^}]*)\}', src, re.S):
        body = block.group(2)
        if "exitQuest" not in body:
            continue
        raw = block.group(1)
        events.add(re.sub(r"\.html?$", "", raw))
    for sym in re.finditer(r'case\s+"(\w+)"\s*:\s*\{([^}]*exitQuest[^}]*)\}', src, re.S):
        events.add(sym.group(1))
    return events


def exit_reward_items(script: str) -> list[int]:
    from quest_pack_809_helpers import parse_int_consts

    src = mobius_java(script)
    consts = parse_int_consts(src)
    ids: list[int] = []
    for m in re.finditer(
        r"giveItems\s*\(\s*\w+\s*,\s*([A-Z0-9_]+|\d+)\s*,\s*\d+[^;]{0,200}?exitQuest",
        src,
        re.S,
    ):
        tok = m.group(1)
        if tok.isdigit():
            ids.append(int(tok))
        elif tok in consts:
            ids.append(consts[tok])
    return ids


def wire_quest(qid: int) -> str:
    folder = questio.find_quest_dir(qid)
    if not folder:
        return f"{qid}: no pack"
    script = next(d.name for d in questio.mobius_quest_root().iterdir() if d.name.startswith(f"Q{qid:05d}_"))
    want = exit_events(script) | exit_talk_pages(script)
    rewards = exit_reward_items(script)
    quest = json.loads((folder / "quest.json").read_text(encoding="utf-8"))
    if quest.get("repeatable"):
        return f"{qid}: repeatable skip"
    talk_pages = exit_talk_pages(script)
    added = 0
    for d in quest.get("dialogs", []):
        ev = str(d.get("event") or "")
        hf = str(d.get("htmlFile") or "")
        page_hit = any(p in ev or p.replace("-", "") in hf or p in hf for p in talk_pages)
        if d.get("hook") == "talk" and page_hit:
            ops = list(d.get("ops") or [])
            if not any(o.get("op") == "complete" for o in ops):
                ops.append(dict(DONE))
                d["ops"] = ops
                added += 1
            continue
        if d.get("hook") != "event":
            continue
        hit = ev in want or page_hit
        if not hit and rewards:
            for o in d.get("ops") or []:
                if o.get("op") == "give_item" and o.get("itemId") in rewards:
                    hit = True
                    break
        if not hit:
            continue
        ops = list(d.get("ops") or [])
        if any(o.get("op") == "complete" for o in ops):
            continue
        ops.append(dict(DONE))
        d["ops"] = ops
        added += 1
    for page in sorted(talk_pages):
        if any(page in str(d.get("event") or "") or page in str(d.get("htmlFile") or "") for d in quest["dialogs"]):
            continue
        npc = int(page.split("-", 1)[0]) if page.split("-", 1)[0].isdigit() else 0
        if not npc:
            continue
        try:
            html = questio.mobius_quest_html(questio.mobius_quest_root(), script, qid, page + ".html")
        except SystemExit:
            try:
                html = questio.mobius_quest_html(questio.mobius_quest_root(), script, qid, page + ".htm")
            except SystemExit:
                continue
        quest.setdefault("dialogs", []).append(
            {
                "npcId": npc,
                "hook": "talk",
                "when": "started",
                "ops": [dict(DONE)],
                "html": html,
            }
        )
        added += 1
    if not added and want:
        prefixes = {
            int(m.group(1))
            for ev in want
            if (m := re.match(r"^(\d+)", ev))
        }
        for d in reversed(quest.get("dialogs", [])):
            if d.get("hook") == "event" and d.get("npcId") in prefixes:
                ops = list(d.get("ops") or [])
                if any(o.get("op") == "complete" for o in ops):
                    break
                ops.append(dict(DONE))
                d["ops"] = ops
                added += 1
                break
    if not added:
        return f"{qid}: no matching dialogs"
    out = questio.externalize_html(quest, folder)
    (folder / "quest.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return f"{qid}: added complete on {added} event(s)"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("quest_ids", nargs="+", type=int)
    args = ap.parse_args()
    for qid in args.quest_ids:
        print(wire_quest(qid))


if __name__ == "__main__":
    main()