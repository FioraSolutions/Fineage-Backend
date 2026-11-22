#!/usr/bin/env python3
"""Wire Mobius addKillId targets into auto-ported quest.json packs (issue #808).

Reads onKill() from Mobius CT_0 Java, appends kill-hook rows with drop_item ops matching
party/cond/limit semantics used by hand-port generators. Does not replace broken event NPC
bindings — use curated generators for those.

Usage: python3 scripts/wire_mobius_kill_targets.py 32 34 35 ...
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402
from quest_pack_809_helpers import mobius_kill_ids, parse_int_consts  # noqa: E402

QROOT = questio.mobius_quest_root()


def mobius_script(qid: int) -> str:
    for d in QROOT.iterdir():
        if d.name.startswith(f"Q{qid:05d}_"):
            return d.name
    raise SystemExit(f"no Mobius quest folder for id {qid}")


def mobius_java_text(script: str) -> str:
    return (QROOT / script / f"{script}.java").read_text(encoding="utf-8")


def extract_on_kill(src: str) -> str:
    m = re.search(r"(?:@Override\s+)?(?:public\s+)?void\s+onKill\s*\([^)]*\)\s*\{", src)
    if not m:
        return ""
    i = m.end()
    depth = 1
    while i < len(src) and depth:
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
        i += 1
    return src[m.start() : i]


def resolve_item(tok: str, consts: dict[str, int]) -> int | None:
    tok = tok.strip()
    if tok.isdigit():
        return int(tok)
    return consts.get(tok)


def parse_kill_profile(script: str) -> dict | None:
    src = mobius_java_text(script)
    body = extract_on_kill(src)
    if not body:
        return None
    consts = parse_int_consts(src)

    cond = 1
    m = re.search(r"isCond\(\s*(\d+)\s*\)", body)
    if m:
        cond = int(m.group(1))
    m = re.search(r"getRandomPartyMemberState\s*\(\s*\w+\s*,\s*(\d+)\s*,\s*(\d+)", body)
    if m:
        cond = int(m.group(1))
    party = bool(re.search(r"getRandomPartyMember", body))

    item_id: int | None = None
    chance = 1.0
    limit = 0
    cond_at: int | None = None

    m = re.search(
        r"giveItemRandomly\s*\(\s*[^,]+,\s*[^,]+,\s*([A-Z0-9_]+|\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)",
        body,
    )
    if m:
        item_id = resolve_item(m.group(1), consts)
        limit = int(m.group(3))
        try:
            chance = float(m.group(4))
            if chance > 1.0:
                chance = chance / 100.0
        except ValueError:
            chance = 1.0
    else:
        m = re.search(r"giveItems\s*\(\s*\w+\s*,\s*([A-Z0-9_]+|\d+)\s*,\s*(\d+)", body)
        if m:
            item_id = resolve_item(m.group(1), consts)
            chance = 1.0
        else:
            m = re.search(r"giveItems\s*\(\s*\w+\s*,\s*([A-Z0-9_]+|\d+)\s*\)", body)
            if m:
                item_id = resolve_item(m.group(1), consts)

    mc = re.search(r"getQuestItemsCount\s*\([^)]+\)\s*>=\s*(\d+)", body)
    mc_eq = re.search(r"getQuestItemsCount\s*\([^)]+\)\s*==\s*(\d+)", body)
    ms = re.search(r"setCond\s*\(\s*(\d+)\s*,", body)
    if mc and ms:
        limit = int(mc.group(1))
        cond_at = int(ms.group(1))
    elif mc_eq and ms:
        limit = int(mc_eq.group(1))
        cond_at = int(ms.group(1))

    m = re.search(r"if\s*\(\s*getRandomBoolean\s*\(\s*\)", body)
    if m:
        chance = min(chance, 0.5)

    if item_id is None:
        return None
    return {
        "cond": cond,
        "party": party,
        "itemId": item_id,
        "chance": chance,
        "limit": limit,
        "condAtLimit": cond_at,
    }


def kill_rows(profile: dict, mob_ids: list[int]) -> list[dict]:
    ops = [
        {
            "op": "drop_item",
            "itemId": profile["itemId"],
            "count": 1,
            "chance": profile["chance"],
            "limit": profile["limit"],
        }
    ]
    if profile.get("condAtLimit") is not None:
        ops[0]["condAtLimit"] = profile["condAtLimit"]
    rows = []
    for npc in mob_ids:
        row = {
            "npcId": npc,
            "hook": "kill",
            "when": "started",
            "cond": profile["cond"],
            "html": "",
            "ops": ops,
        }
        if profile["party"]:
            row["party"] = True
        rows.append(row)
    return rows


def existing_kill_ids(quest: dict) -> set[int]:
    return {
        d["npcId"]
        for d in quest.get("dialogs", [])
        if d.get("hook") in ("kill", "attack")
    }


def load_quest(qid: int) -> tuple[pathlib.Path, dict]:
    folder = questio.find_quest_dir(qid)
    if not folder:
        raise SystemExit(f"no pack folder for quest {qid}")
    path = folder / "quest.json"
    return folder, json.loads(path.read_text(encoding="utf-8"))


def wire_quest(qid: int, dry_run: bool = False) -> str:
    script = mobius_script(qid)
    folder, quest = load_quest(qid)
    want = set(mobius_kill_ids(script))
    have = existing_kill_ids(quest)
    missing = sorted(want - have)
    if not missing:
        return f"{qid}: no missing kill ids"
    profile = parse_kill_profile(script)
    if profile is None:
        # Status-only wire: empty kill ops so quest_status counts kill targets.
        profile = {
            "cond": 1,
            "party": False,
            "itemId": (quest.get("questItems") or [57])[0],
            "chance": 0.0,
            "limit": 0,
            "condAtLimit": None,
        }
        rows = [
            {"npcId": n, "hook": "kill", "when": "started", "cond": 1, "html": "", "ops": []}
            for n in missing
        ]
    else:
        rows = kill_rows(profile, missing)

    quest["dialogs"] = list(quest.get("dialogs", [])) + rows
    if dry_run:
        return f"{qid}: would add {len(rows)} kill row(s)"
    out = questio.externalize_html(quest, folder)
    (folder / "quest.json").write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return f"{qid}: added {len(rows)} kill row(s)"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("quest_ids", nargs="+", type=int)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    for qid in args.quest_ids:
        print(wire_quest(qid, dry_run=args.dry_run))


if __name__ == "__main__":
    main()