#!/usr/bin/env python3
"""Servitor summon skills (Mobius CT_0 skill XML, effect "Summon") -> data/summons.json.

Per skill level: servitor npcId, the items the cast itself spends (itemConsumeId x itemConsumeCount), the upkeep item
spent every consumeItemInterval seconds while it stays, its lifetime and the owner's exp multiplier.
Values are transcribed as data; the behaviour lives in Fineage.Zone (ZoneWorldSummons).
"""
import json
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = "/workspace/mobius"
SKILLS = "L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills"
NPCS = "L2J_Mobius_CT_0_Interlude/dist/game/data/stats/npcs"


def git(*args):
    return subprocess.run(["git", "-C", MOBIUS, *args], capture_output=True, text=True, check=True).stdout


def value(raw, tables, level, default=None):
    if raw is None:
        return default
    raw = raw.strip()
    if raw.startswith("#"):
        vals = tables[raw]
        return vals[min(level, len(vals)) - 1]
    return raw


def tag(body, name):
    m = re.search(rf"<{name}>([^<]*)</{name}>", body)
    return m.group(1) if m else None


def main():
    out = []
    for path in git("ls-tree", "--name-only", "HEAD", SKILLS + "/").split():
        text = git("show", f"HEAD:{path}")
        for m in re.finditer(r'<skill id="(\d+)" levels="(\d+)" name="([^"]*)".*?</skill>', text, re.S):
            body = m.group(0)
            main_effects = re.search(r"<effects>(.*?)</effects>", body, re.S)
            if not main_effects or 'effect name="Summon"' not in main_effects.group(1):
                continue
            eff = re.search(r'<effect name="Summon">(.*?)</effect>', main_effects.group(1), re.S).group(1)
            tables = {t: v.split() for t, v in re.findall(r'<table name="(#\w+)">([^<]*)</table>', body)}
            target = tag(body, "targetType") or "SELF"
            levels = []
            for lv in range(1, int(m.group(2)) + 1):
                levels.append({
                    "level": lv,
                    "npcId": int(value(tag(eff, "npcId"), tables, lv)),
                    "itemId": int(value(tag(body, "itemConsumeId"), tables, lv, 0)),
                    "itemCount": int(value(tag(body, "itemConsumeCount"), tables, lv, 0)),
                    "upkeepItemId": int(value(tag(eff, "consumeItemId"), tables, lv, 0)),
                    "upkeepCount": int(value(tag(eff, "consumeItemCount"), tables, lv, 1)),
                    "upkeepIntervalSec": int(value(tag(eff, "consumeItemInterval"), tables, lv, 0)),
                    "lifeTimeSec": int(value(tag(eff, "lifeTime"), tables, lv, 3600)),
                    "expMultiplier": float(value(tag(eff, "expMultiplier"), tables, lv, 1)),
                })
            if levels and levels[0]["itemId"] == 0:
                for row in levels:
                    row["itemCount"] = 0
            out.append({"skillId": int(m.group(1)), "name": m.group(3), "corpse": target == "CORPSE_MOB", "levels": levels})
    out.sort(key=lambda r: r["skillId"])
    # Servitor commandable attack skills: the NPC's skillList (stats/npcs) kept where skill-combat.json can roll them.
    combat = {row["skillId"] for row in json.loads((ROOT / "data/skill-combat.json").read_text())["skills"]}
    wanted = {row["npcId"] for s in out for row in s["levels"]}
    npc_skills = {}
    for path in git("ls-tree", "--name-only", "HEAD", NPCS + "/").split():
        text = git("show", f"HEAD:{path}")
        for m in re.finditer(r'<npc id="(\d+)".*?</npc>', text, re.S):
            npc_id = int(m.group(1))
            if npc_id not in wanted:
                continue
            block = re.search(r"<skillList>(.*?)</skillList>", m.group(0), re.S)
            rows = [[int(a), int(b)] for a, b in re.findall(r'<skill id="(\d+)" level="(\d+)"', block.group(1))] if block else []
            rows = [r for r in rows if r[0] in combat]
            if rows:
                npc_skills[str(npc_id)] = rows
    doc = {"reference": "Servitor summon skills transcribed from Mobius CT 0 Interlude stats/skills (effect Summon).",
           "summons": out,
           "npcSkills": dict(sorted(npc_skills.items(), key=lambda kv: int(kv[0])))}
    (ROOT / "data/summons.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
    print(len(out), "summon skills")


if __name__ == "__main__":
    main()
