#!/usr/bin/env python3
"""Build data/npc-ai-support.json — Mobius AISkillScope HEAL/BUFF/DEBUFF/IMMOBILIZE/COT/SUICIDE.

Classifies non-passive skillList rows using Mobius skill XML. Buff/debuff need
skill-stat-effects.json; immobilize/silence carry abnormalType slots for control.
Suicide rows carry MagicalAttack power + affectRange (NPC Death Bomb).

Reads the pinned Mobius CT 0 Interlude datapack only (tools/mobius_pin.py): the CT 0 skill XML
layout is `<mpConsume>#table</mpConsume>` elements, and Epilogue/HighFive input is refused. The
Nov 4 version of this file was generated from HighFive (`<set name=...>` layout), which folds the
initial MP cost into mpConsume and has different NPC skill tables.

Usage: gen_npc_ai_support.py <L2J_Mobius_CT_0_Interlude | .../dist/game/data> [--check] [--allow-unpinned]
  --check  regenerate in memory and exit 1 if data/npc-ai-support.json differs (CI)
"""
from __future__ import annotations

import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "scripts"))
import mobius_pin  # noqa: E402
import sync_skill_defs_from_mobius as sync  # noqa: E402
SSE_IDS = {
    int(s["skillId"])
    for s in json.loads((ROOT / "data" / "skill-stat-effects.json").read_text())["skills"]
}
# Skills Fineage defines (data/skills.json). RequiredDataCrossCheck rejects a cast of any other skill, so
# NPC skills we do not ship a definition for stay out of this file instead of dangling.
SKILL_IDS = {int(s["skillId"]) for s in json.loads((ROOT / "data" / "skills.json").read_text())["skills"]}
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


def _int_list(values):
    return None if values is None else [int(v) for v in values]


def _text(node: ET.Element, tag: str) -> str:
    return sync.text_of(node, tag)


def parse_skill_meta(skills_dir: pathlib.Path) -> dict[int, dict]:
    """Per-skill facts the NPC AI needs, read from the CT 0 skill XML (element layout)."""
    meta: dict[int, dict] = {}
    for f in sorted(skills_dir.glob("*.xml")):
        for node in ET.parse(f).getroot().iter("skill"):
            sid, levels, name = int(node.get("id")), int(node.get("levels", "1")), node.get("name") or ""
            if _text(node, "operateType") == "P":
                meta[sid] = {"kind": "passive", "name": name}
                continue
            tables = sync.skill_tables(node)

            def per_level(tag: str):
                return _int_list(sync.resolve(node, tag, tables, levels))

            heal_effects = [e for e in node.iter("effect") if e.get("name") == "Heal"]
            heal = bool(heal_effects)
            suicide = _text(node, "isSuicideAttack").lower() == "true"
            cont = sync.direct(node, "abnormalTime") is not None
            debuff = _text(node, "isDebuff").lower() == "true"
            slot = _text(node, "abnormalType")
            amount = None
            amount_table = tables.get("#amount")
            if amount_table:
                amount = [int(float(x)) for x in amount_table]
            power = per_level("power")
            affect = per_level("affectRange")
            hit = per_level("hitTime")
            reuse = per_level("reuseDelay")
            abn_time = per_level("abnormalTime")
            abn_level = per_level("abnormalLevel")

            kind = "other"
            if suicide:
                kind = "suicide"
            elif heal:
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
                "power": power,
                "affectRange": affect,
                "slot": slot,
                "mp": per_level("mpConsume"),
                "mpi": per_level("mpInitialConsume"),
                "hitTimeMs": hit,
                "reuseDelayMs": reuse,
                "abnormalTimeSeconds": abn_time,
                "abnormalLevels": abn_level,
            }
    return meta


def table_at(values: list[int] | None, level: int, default: int = 0) -> int:
    if not values:
        return default
    idx = max(0, min(level, len(values)) - 1)
    return values[idx]


def abnormal_seconds(info: dict, level: int) -> int:
    return table_at(info.get("abnormalTimeSeconds"), level)


def skill_row(sid: int, level: int, info: dict) -> dict:
    row = {
        "id": sid,
        "level": level,
        "hitTimeMs": table_at(info.get("hitTimeMs"), level),
        "reuseDelayMs": table_at(info.get("reuseDelayMs"), level),
        "mp": table_at(info.get("mp"), level),
        "mpInitial": table_at(info.get("mpi"), level),
    }
    if info["kind"] == "suicide":
        row["power"] = table_at(info.get("power"), level)
        row["affectRange"] = table_at(info.get("affectRange"), level) or 150
    elif info["kind"] in ("heal", "clan_heal"):
        row["amount"] = table_at(info.get("amount"), level)
    else:
        row["abnormalTimeSeconds"] = abnormal_seconds(info, level)
        row["abnormalLevel"] = table_at(info.get("abnormalLevels"), level, default=level)
        if info.get("slot"):
            row["slot"] = info["slot"]
        # AURA signet skills (5123–5129) pulse on everyone inside affectRange; the zone needs that radius
        # to know who the EffectPoint NPC buffs.
        if table_at(info.get("affectRange"), level):
            row["affectRange"] = table_at(info.get("affectRange"), level)
    return row


def main() -> None:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = {a for a in sys.argv[1:] if a.startswith("--")}
    if len(args) != 1 or flags - {"--check", "--allow-unpinned"}:
        print(__doc__, file=sys.stderr)
        sys.exit(2)

    given = pathlib.Path(args[0])
    data_root = given if (given / "stats").is_dir() else given / "dist" / "game" / "data"
    verified = mobius_pin.require_pinned_ct0(
        data_root,
        "dist/game/data/stats/skills",
        "dist/game/data/stats/npcs",
        allow_unpinned="--allow-unpinned" in flags,
    )
    meta = parse_skill_meta(data_root / "stats" / "skills")
    by_npc: dict[int, dict] = {}

    skipped_undefined: set[int] = set()
    for f in sorted((data_root / "stats" / "npcs").glob("*.xml")):
        for npc in ET.parse(f).getroot().findall("npc"):
            nid = int(npc.get("id"))
            # skillList rows, then the EffectPoint signet skill: CT 0 keeps it in
            # <parameters><skill name="union_skill" .../> (HighFive lists it in skillList).
            wanted: list[tuple[int, int]] = []
            skill_list = npc.find("skillList")
            if skill_list is not None:
                wanted += [(int(e.get("id")), int(e.get("level"))) for e in skill_list.findall("skill")]
            params = npc.find("parameters")
            if params is not None:
                wanted += [
                    (int(e.get("id")), int(e.get("level")))
                    for e in params.findall("skill")
                    if e.get("name") == "union_skill"
                ]
            if not wanted:
                continue
            heal, buff, debuff, immobilize, silence, suicide = [], [], [], [], [], []
            seen = set()
            for sid, level in wanted:
                if sid in seen:
                    continue
                seen.add(sid)
                info = meta.get(sid)
                if not info or info["kind"] == "passive":
                    continue
                kind = info["kind"]
                # Suicide MagicalAttack rows live in skill-combat too — still SUICIDE scope, not ATTACK.
                if kind == "suicide" and info.get("power"):
                    if sid not in SKILL_IDS:
                        skipped_undefined.add(sid)
                        continue
                    suicide.append(skill_row(sid, level, info))
                    continue
                if sid in COMBAT_IDS:
                    continue
                supported = (
                    (kind in ("heal", "clan_heal") and info.get("amount"))
                    or (kind == "buff" and sid in SSE_IDS and abnormal_seconds(info, level) > 0)
                    or (kind == "debuff" and sid in SSE_IDS and abnormal_seconds(info, level) > 0)
                    or (kind == "immobilize" and abnormal_seconds(info, level) > 0)
                    or (kind == "silence" and abnormal_seconds(info, level) > 0)
                )
                if not supported:
                    continue
                if sid not in SKILL_IDS:
                    skipped_undefined.add(sid)
                    continue
                if kind in ("heal", "clan_heal"):
                    heal.append(skill_row(sid, level, info))
                elif kind == "buff":
                    buff.append(skill_row(sid, level, info))
                elif kind == "debuff":
                    debuff.append(skill_row(sid, level, info))
                elif kind == "immobilize":
                    immobilize.append(skill_row(sid, level, info))
                else:
                    silence.append(skill_row(sid, level, info))
            if heal or buff or debuff or immobilize or silence or suicide:
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
                if suicide:
                    row["suicide"] = suicide
                by_npc[nid] = row

    catalog_ids = {
        n["npcId"] for n in json.loads((ROOT / "data" / "npcs.json").read_text())["npcs"]
    }
    rows = [by_npc[i] for i in sorted(by_npc) if i in catalog_ids]

    print(f"skipped {len(skipped_undefined)} NPC support skills with no definition in data/skills.json")
    out = {
        "reference": "Mobius AISkillScope HEAL/BUFF/DEBUFF/IMMOBILIZE/COT/SUICIDE from skillList",
        "npcs": rows,
    }
    path = ROOT / "data" / "npc-ai-support.json"
    text = json.dumps(out, ensure_ascii=False, indent=1) + "\n"
    if "--check" in flags:
        same = path.read_text(encoding="utf-8") == text
        print(f"npc-ai-support.json vs Mobius CT 0 [{verified}]: {'identical' if same else 'DIFFERS'} ({len(rows)} npcs)")
        sys.exit(0 if same else 1)
    path.write_text(text)
    print(
        f"wrote {path.name} [{verified}]: {len(rows)} npcs "
        f"(heal={sum(1 for r in rows if r.get('heal'))}, "
        f"buff={sum(1 for r in rows if r.get('buff'))}, "
        f"debuff={sum(1 for r in rows if r.get('debuff'))}, "
        f"immobilize={sum(1 for r in rows if r.get('immobilize'))}, "
        f"silence={sum(1 for r in rows if r.get('silence'))}, "
        f"suicide={sum(1 for r in rows if r.get('suicide'))})"
    )


if __name__ == "__main__":
    main()
