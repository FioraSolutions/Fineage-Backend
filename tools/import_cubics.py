#!/usr/bin/env python3
"""Cubics (Mobius CT_0 stats/cubics + SummonCubic / CubicMastery skill effects) -> data/cubics.json.

- "cubics": every cubic template (id, level): lifetime, action delay, action cap, use-up flag, cubic power, target
  route (TARGET / HEAL) and the cubic's action skills.
- "summonSkills": skills whose effect is SummonCubic, per level: cubic power, item cost and the effect overrides.
- "actions": the cubic action skills with the numbers the zone needs (power, abnormal, land rate, trait, DoT).
- "mastery": CubicMastery passives (max cubics at once).
Values are transcribed as data; the behaviour lives in Fineage.Zone (ZoneWorldCubics).
"""
import json
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = "/workspace/mobius"
BASE = "L2J_Mobius_CT_0_Interlude/dist/game/data/stats"


def git(*args):
    return subprocess.run(["git", "-C", MOBIUS, *args], capture_output=True, text=True, check=True).stdout


def files(sub):
    return git("ls-tree", "--name-only", "HEAD", f"{BASE}/{sub}/").split()


def tag(body, name):
    m = re.search(rf"<{name}>([^<]*)</{name}>", body)
    return m.group(1).strip() if m else None


def value(raw, tables, level, default=None):
    if raw is None:
        return default
    if raw.startswith("#"):
        vals = tables[raw]
        return vals[min(level, len(vals)) - 1]
    return raw


def num(v, default=0):
    if v is None:
        return default
    f = float(v)
    return int(f) if f.is_integer() else f


def skills():
    for path in files("skills"):
        text = git("show", f"HEAD:{path}")
        for m in re.finditer(r'<skill id="(\d+)" levels="(\d+)" name="([^"]*)".*?</skill>', text, re.S):
            body = m.group(0)
            tables = {t: v.split() for t, v in re.findall(r'<table name="(#\w+)">([^<]*)</table>', body)}
            yield int(m.group(1)), int(m.group(2)), m.group(3), body, tables


def main():
    cubics = []
    action_ids = set()
    for path in files("cubics"):
        text = git("show", f"HEAD:{path}")
        for m in re.finditer(r"<cubic ([^>]*)>(.*?)</cubic>", text, re.S):
            a = dict(re.findall(r'(\w+)="([^"]*)"', m.group(1)))
            sk = []
            for s in re.finditer(r"<skill ([^>]*)/>", m.group(2)):
                sa = dict(re.findall(r'(\w+)="([^"]*)"', s.group(1)))
                row = {"skillId": int(sa["id"]), "level": int(sa.get("level", 1))}
                if "triggerRate" in sa:
                    row["triggerRate"] = int(sa["triggerRate"])
                if sa.get("priority", "false") == "true":
                    row["priority"] = True
                sk.append(row)
                action_ids.add(row["skillId"])
            cubics.append({
                "id": int(a["id"]), "level": int(a["level"]),
                "durationSec": int(a.get("duration", 900)), "delaySec": int(a.get("delay", 10)),
                "maxCount": int(a.get("maxCount", -1)), "useUp": a.get("useUp", "0") == "1",
                "power": num(a.get("power", 0)), "targetType": a.get("targetType", "TARGET"),
                "selection": a.get("selectionMode", "RANDOM"), "skills": sk,
            })
    cubics.sort(key=lambda c: (c["id"], c["level"]))

    summon, actions, mastery = [], [], []
    for sid, levels, name, body, tables in skills():
        eff_block = re.search(r"<effects>(.*?)</effects>", body, re.S)
        effects = eff_block.group(1) if eff_block else ""
        if 'effect name="SummonCubic"' in effects:
            eff = re.search(r'<effect name="SummonCubic">(.*?)</effect>', effects, re.S).group(1)
            rows = []
            for lv in range(1, levels + 1):
                rows.append({
                    "level": lv,
                    "power": num(value(tag(eff, "cubicPower"), tables, lv, 0)),
                    "itemId": int(value(tag(body, "itemConsumeId"), tables, lv, 0)),
                    "itemCount": int(value(tag(body, "itemConsumeCount"), tables, lv, 0)),
                    "cubicLevel": int(value(tag(eff, "cubicLvl") or tag(eff, "cubicSkillLevel"), tables, lv, 0)),
                })
            summon.append({
                "skillId": sid, "name": name, "cubicId": int(tag(eff, "cubicId")),
                "party": (tag(body, "targetType") or "SELF") == "PARTY",
                "delaySec": int(tag(eff, "cubicDelay") or 0), "durationSec": int(tag(eff, "cubicDuration") or 0),
                "maxCount": int(tag(eff, "cubicMaxCount") or -1), "chance": int(tag(eff, "cubicSkillChance") or 0),
                "levels": rows,
            })
        if 'effect name="CubicMastery"' in effects:
            eff = re.search(r'<effect name="CubicMastery">(.*?)</effect>', effects, re.S).group(1)
            mastery.append({"skillId": sid, "counts": [int(value(tag(eff, "cubicCount"), tables, lv, 1)) for lv in range(1, levels + 1)]})
        if sid in action_ids:
            kinds = re.findall(r'<effect name="(\w+)"', effects)
            kind = ("mdam" if "MagicalDamage" in kinds else "drain" if "HpDrain" in kinds else "heal" if "Heal" in kinds
                    else "dot" if "DamOverTime" in kinds else "paralyze" if "Paralyze" in kinds else "stun" if "Stun" in kinds
                    else "root" if "Root" in kinds else "aggro" if "TargetMeProbability" in kinds else "debuff")
            dot = re.search(r'<effect name="DamOverTime" ticks="(\d+)">(.*?)</effect>', effects, re.S)
            heal = re.search(r'<effect name="Heal">(.*?)</effect>', effects, re.S)
            drain = re.search(r'<effect name="HpDrain">(.*?)</effect>', effects, re.S)
            stats = [{"stat": st, "op": op, "value": num(v)} for op, st, v in re.findall(r'<(mul|add|sub)\s+stat="(\w+)">([^<]*)</\1>', effects)]
            rows = []
            for lv in range(1, levels + 1):
                r = {"level": lv,
                     "power": num(value(tag(body, "power"), tables, lv, 0)),
                     "magicLevel": int(value(tag(body, "magicLevel"), tables, lv, 0)),
                     "abnormalLevel": int(value(tag(body, "abnormalLevel"), tables, lv, 0))}
                if dot:
                    r["dot"] = num(value(tag(dot.group(2), "power"), tables, lv, 0))
                if heal:
                    r["heal"] = num(value(tag(heal.group(1), "power"), tables, lv, 0))
                rows.append(r)
            actions.append({
                "skillId": sid, "name": name, "kind": kind,
                "debuff": (tag(body, "isDebuff") or "false") == "true",
                "abnormalType": tag(body, "abnormalType") or "", "abnormalTimeSec": int(tag(body, "abnormalTime") or 0),
                "activateRate": int(tag(body, "activateRate") or -1), "basicProperty": tag(body, "basicProperty") or "NONE",
                "trait": tag(body, "trait") or "", "lvlBonusRate": int(tag(body, "lvlBonusRate") or 0),
                "dotTicks": int(dot.group(1)) if dot else 0,
                "drainRatio": num(tag(drain.group(1), "power")) if drain else 0,
                "aggroChance": int(tag(effects, "chance") or 0) if kind == "aggro" else 0,
                "stats": stats, "levels": rows,
            })
    summon.sort(key=lambda r: r["skillId"])
    actions.sort(key=lambda r: r["skillId"])
    doc = {"reference": "Cubics transcribed from Mobius CT 0 Interlude stats/cubics and stats/skills (SummonCubic, CubicMastery).",
           "cubics": cubics, "summonSkills": summon, "actions": actions, "mastery": mastery}
    (ROOT / "data/cubics.json").write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
    print(len(cubics), "cubic templates,", len(summon), "summon skills,", len(actions), "actions,", len(mastery), "mastery")


if __name__ == "__main__":
    main()
