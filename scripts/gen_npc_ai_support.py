#!/usr/bin/env python3
"""Build data/npc-ai-support.json — Mobius AISkillScope HEAL/BUFF/DEBUFF/IMMOBILIZE/COT.

Classifies non-passive skillList rows using Mobius skill XML. Buff/debuff need
skill-stat-effects.json; immobilize/silence carry abnormalType slots for control.

Usage: gen_npc_ai_support.py <mobius dist/game/data>
"""
from __future__ import annotations

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SSE_IDS = {
    int(s["skillId"])
    for s in json.loads((ROOT / "data" / "skill-stat-effects.json").read_text())["skills"]
}
COMBAT_IDS = {
    int(s["skillId"])
    for s in json.loads((ROOT / "data" / "skill-combat.json").read_text())["skills"]
}

IMMOBILIZE_SLOTS = {
    "SLEEP",
    "ROOT_MAGICALLY",
    "ROOT_PHYSICALLY",
    "STUN",
    "PARALYZE",
}
SILENCE_SLOTS = {"SILENCE", "SILENCE_PHYSICAL", "SILENCE_ALL"}


def parse_skill_meta(skills_dir: pathlib.Path) -> dict[int, dict]:
    meta: dict[int, dict] = {}
    for f in sorted(skills_dir.glob("*.xml")):
        text = f.read_text()
        for m in re.finditer(
            r'<skill id="(\d+)" levels="(\d+)" name="([^"]+)">(.*?)</skill>', text, re.S
        ):
            sid, levels, name, body = int(m.group(1)), int(m.group(2)), m.group(3), m.group(4)
            op_m = re.search(r'set name="operateType" val="([^"]+)"', body)
            op = op_m.group(1) if op_m else "?"
            if op == "P":
                meta[sid] = {"kind": "passive", "name": name}
                continue

            heal = bool(re.search(r'effect name="Heal"', body))
            cont = "abnormalTime" in body
            debuff = bool(re.search(r'set name="isDebuff" val="true"', body, re.I))
            slot_m = re.search(r'set name="abnormalType" val="([^"]+)"', body)
            slot = slot_m.group(1) if slot_m else ""
            amount_m = re.search(r'<table name="#amount">(.*?)</table>', body, re.S)
            amount = (
                [int(float(x)) for x in amount_m.group(1).split()] if amount_m else None
            )
            mp_m = re.search(r'<table name="#mpConsume">(.*?)</table>', body, re.S)
            mpi_m = re.search(r'<table name="#mpInitialConsume">(.*?)</table>', body, re.S)
            hit_m = re.search(r'set name="hitTime" val="(\d+)"', body)
            reuse_m = re.search(r'set name="reuseDelay" val="(\d+)"', body)
            abn_m = re.search(r'set name="abnormalTime" val="(\d+)"', body)
            abn_lv_m = re.search(r'<table name="#abnormalLevels">(.*?)</table>', body, re.S)
            abn_levels = (
                [int(float(x)) for x in abn_lv_m.group(1).split()] if abn_lv_m else None
            )
            abn_fixed = re.search(r'set name="abnormalLevel" val="(\d+)"', body)

            kind = "other"
            if heal:
                kind = "clan_heal" if "Clan" in name else "heal"
            elif slot in IMMOBILIZE_SLOTS:
                kind = "immobilize"
            elif slot in SILENCE_SLOTS:
                kind = "silence"
            elif cont and debuff:
                kind = "debuff"
            elif cont and not debuff:
                kind = "buff"

            meta[sid] = {
                "kind": kind,
                "name": name,
                "levels": levels,
                "amount": amount,
                "slot": slot,
                "mp": [int(float(x)) for x in mp_m.group(1).split()] if mp_m else None,
                "mpi": [int(float(x)) for x in mpi_m.group(1).split()] if mpi_m else None,
                "hitTimeMs": int(hit_m.group(1)) if hit_m else 0,
                "reuseDelayMs": int(reuse_m.group(1)) if reuse_m else 0,
                "abnormalTimeSeconds": int(abn_m.group(1)) if abn_m else 0,
                "abnormalLevels": abn_levels,
                "abnormalLevelFixed": int(abn_fixed.group(1)) if abn_fixed else None,
            }
    return meta


def table_at(values: list[int] | None, level: int, default: int = 0) -> int:
    if not values:
        return default
    idx = max(0, min(level, len(values)) - 1)
    return values[idx]


def skill_row(sid: int, level: int, info: dict) -> dict:
    row = {
        "id": sid,
        "level": level,
        "hitTimeMs": info["hitTimeMs"],
        "reuseDelayMs": info["reuseDelayMs"],
        "mp": table_at(info.get("mp"), level),
        "mpInitial": table_at(info.get("mpi"), level),
    }
    if info["kind"] in ("heal", "clan_heal"):
        row["amount"] = table_at(info.get("amount"), level)
    else:
        row["abnormalTimeSeconds"] = info["abnormalTimeSeconds"]
        if info.get("abnormalLevelFixed") is not None:
            row["abnormalLevel"] = info["abnormalLevelFixed"]
        else:
            abn = info.get("abnormalLevels")
            row["abnormalLevel"] = table_at(abn, level, default=level)
        if info.get("slot"):
            row["slot"] = info["slot"]
    return row


def main() -> None:
    if len(sys.argv) != 2:
        print("Usage: gen_npc_ai_support.py <mobius dist/game/data>", file=sys.stderr)
        sys.exit(2)

    data_root = pathlib.Path(sys.argv[1])
    meta = parse_skill_meta(data_root / "stats" / "skills")
    by_npc: dict[int, dict] = {}

    for f in sorted((data_root / "stats" / "npcs").glob("*.xml")):
        text = re.sub(r"<!--.*?-->", "", f.read_text(), flags=re.S)
        for m in re.finditer(r'<npc id="(\d+)"[^>]*>(.*?)</npc>', text, re.S):
            nid, body = int(m.group(1)), m.group(2)
            sl = re.search(r"<skillList>(.*?)</skillList>", body, re.S)
            if not sl:
                continue
            heal, buff, debuff, immobilize, silence = [], [], [], [], []
            seen = set()
            for sid_s, lvl_s in re.findall(
                r'\bid="(\d+)"[^>]*\blevel="(\d+)"', sl.group(1)
            ):
                sid, level = int(sid_s), int(lvl_s)
                if sid in seen or sid in COMBAT_IDS:
                    continue
                seen.add(sid)
                info = meta.get(sid)
                if not info or info["kind"] == "passive":
                    continue
                kind = info["kind"]
                if kind in ("heal", "clan_heal") and info.get("amount"):
                    heal.append(skill_row(sid, level, info))
                elif kind == "buff" and sid in SSE_IDS and info["abnormalTimeSeconds"] > 0:
                    buff.append(skill_row(sid, level, info))
                elif kind == "debuff" and sid in SSE_IDS and info["abnormalTimeSeconds"] > 0:
                    debuff.append(skill_row(sid, level, info))
                elif kind == "immobilize" and info["abnormalTimeSeconds"] > 0:
                    immobilize.append(skill_row(sid, level, info))
                elif kind == "silence" and info["abnormalTimeSeconds"] > 0:
                    silence.append(skill_row(sid, level, info))
            if heal or buff or debuff or immobilize or silence:
                row: dict = {"npcId": nid}
                if heal:
                    row["heal"] = heal
                if buff:
                    row["buff"] = buff
                if debuff:
                    row["debuff"] = debuff
                if immobilize:
                    row["immobilize"] = immobilize
                if silence:
                    row["silence"] = silence
                by_npc[nid] = row

    catalog_ids = {
        n["npcId"] for n in json.loads((ROOT / "data" / "npcs.json").read_text())["npcs"]
    }
    rows = [by_npc[i] for i in sorted(by_npc) if i in catalog_ids]

    out = {
        "reference": "Mobius AISkillScope HEAL/BUFF/DEBUFF/IMMOBILIZE/COT from skillList",
        "npcs": rows,
    }
    path = ROOT / "data" / "npc-ai-support.json"
    path.write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n")
    print(
        f"wrote {path.name}: {len(rows)} npcs "
        f"(heal={sum(1 for r in rows if r.get('heal'))}, "
        f"buff={sum(1 for r in rows if r.get('buff'))}, "
        f"debuff={sum(1 for r in rows if r.get('debuff'))}, "
        f"immobilize={sum(1 for r in rows if r.get('immobilize'))}, "
        f"silence={sum(1 for r in rows if r.get('silence'))})"
    )


if __name__ == "__main__":
    main()
