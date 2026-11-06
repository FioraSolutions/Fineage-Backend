#!/usr/bin/env python3
"""Patch data/npcs.json with Mobius weapon + race defence traits.

Mobius has no npcs.xml defence-trait field: Npc skillList passives apply
DefenceTrait effects (Formulas.calcWeaponTraitBonus / calcAttackTraitBonus type-2).
We resolve those passives offline and store the folded _defenceTraits multipliers
(default 1). Weapon keys feed calcWeaponTraitBonus; *Weakness / valakas keys feed
type-2 weakness when the attacker also has AttackTrait (Detect Weakness buffs).

Usage: gen_npc_weapon_traits.py <mobius dist/game/data>
  expects <root>/stats/skills/*.xml and <root>/stats/npcs/*.xml
"""
from __future__ import annotations

import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

WEAPON = {
    "SWORD",
    "BLUNT",
    "DAGGER",
    "POLE",
    "FIST",
    "BOW",
    "ETC",
    "DUAL",
    "DUALFIST",
    "DUALDAGGER",
}

# Mobius TraitType type=2 (weakness) used by calcAttackTraitBonus.
RACE = {
    "BUG_WEAKNESS",
    "ANIMAL_WEAKNESS",
    "PLANT_WEAKNESS",
    "BEAST_WEAKNESS",
    "DRAGON_WEAKNESS",
    "GIANT_WEAKNESS",
    "CONSTRUCT_WEAKNESS",
    "VALAKAS",
}

TRAITS = WEAPON | RACE

# Fineage JSON keys (camelCase).
KEY = {
    "SWORD": "sword",
    "BLUNT": "blunt",
    "DAGGER": "dagger",
    "POLE": "pole",
    "FIST": "fist",
    "BOW": "bow",
    "ETC": "etc",
    "DUAL": "dual",
    "DUALFIST": "dualfist",
    "DUALDAGGER": "dualdagger",
    "BUG_WEAKNESS": "bugWeakness",
    "ANIMAL_WEAKNESS": "animalWeakness",
    "PLANT_WEAKNESS": "plantWeakness",
    "BEAST_WEAKNESS": "beastWeakness",
    "DRAGON_WEAKNESS": "dragonWeakness",
    "GIANT_WEAKNESS": "giantWeakness",
    "CONSTRUCT_WEAKNESS": "constructWeakness",
    "VALAKAS": "valakas",
}


def load_skill_traits(skills_dir: pathlib.Path) -> dict[int, list[dict[str, float]]]:
    """skillId → per-level map of trait → Mobius defenceTraits multiplier."""
    out: dict[int, list[dict[str, float]]] = {}
    for f in sorted(skills_dir.glob("*.xml")):
        root = ET.parse(f).getroot()
        for sk in root.iter("skill"):
            fr = sk.find("for")
            if fr is None:
                continue
            tables = {t.get("name"): (t.text or "").split() for t in sk.findall("table")}
            levels = int(sk.get("levels", "1"))
            sid = int(sk.get("id"))
            level_maps: list[dict[str, float]] = [defaultdict(lambda: 1.0) for _ in range(levels)]
            found = False
            for effect in fr.findall("effect"):
                if effect.get("name") != "DefenceTrait":
                    continue
                for param in effect:
                    if param.tag != "param":
                        continue
                    for name, raw_s in param.attrib.items():
                        if name not in TRAITS:
                            continue
                        found = True
                        if raw_s.startswith("#"):
                            vals = tables.get(raw_s)
                            if not vals:
                                continue
                            for i in range(levels):
                                raw = float(vals[i] if i < len(vals) else vals[-1])
                                if raw == 0:
                                    continue  # DefenceTrait skips 0
                                level_maps[i][name] *= (raw + 100.0) / 100.0
                        else:
                            raw = float(raw_s)
                            if raw == 0:
                                continue
                            mul = (raw + 100.0) / 100.0
                            for i in range(levels):
                                level_maps[i][name] *= mul
            if found:
                out[sid] = [{k: v for k, v in m.items()} for m in level_maps]
    return out


def npc_traits(body: str, skill_traits: dict[int, list[dict[str, float]]]) -> dict[str, float]:
    sl = re.search(r"<skillList>(.*?)</skillList>", body, re.S)
    if not sl:
        return {}
    traits: dict[str, float] = defaultdict(lambda: 1.0)
    for sm in re.finditer(r'<skill\b[^>]*\bid="(\d+)"[^>]*\blevel="(\d+)"', sl.group(1)):
        sid, lvl = int(sm.group(1)), int(sm.group(2))
        maps = skill_traits.get(sid)
        if not maps:
            continue
        row = maps[min(lvl, len(maps)) - 1]
        for trait, mul in row.items():
            traits[trait] *= mul
    return {KEY[k]: round(v, 6) for k, v in traits.items() if abs(v - 1.0) > 1e-9}


def main() -> None:
    if len(sys.argv) != 2:
        print("Usage: gen_npc_weapon_traits.py <mobius dist/game/data>", file=sys.stderr)
        sys.exit(2)
    root = pathlib.Path(sys.argv[1])
    skills = load_skill_traits(root / "stats" / "skills")
    by_id: dict[int, dict[str, float]] = {}
    for f in sorted((root / "stats" / "npcs").glob("*.xml")):
        text = re.sub(r"<!--.*?-->", "", f.read_text(), flags=re.S)
        for m in re.finditer(r'<npc id="(\d+)"[^>]*>.*?</npc>', text, re.S):
            nid = int(m.group(1))
            t = npc_traits(m.group(0), skills)
            if t:
                by_id[nid] = t

    path = pathlib.Path(__file__).resolve().parent.parent / "data" / "npcs.json"
    data = json.loads(path.read_text())
    n = 0
    race_n = 0
    for npc in data["npcs"]:
        npc.pop("defenceTraits", None)
        patch = by_id.get(npc["npcId"])
        if patch:
            npc["defenceTraits"] = patch
            n += 1
            if any(k.endswith("Weakness") or k == "valakas" for k in patch):
                race_n += 1

    lines = ["{", f'  "source": {json.dumps(data.get("source", ""))},', '  "npcs": [']
    for i, npc in enumerate(data["npcs"]):
        comma = "," if i + 1 < len(data["npcs"]) else ""
        lines.append("    " + json.dumps(npc, ensure_ascii=False, separators=(", ", ": ")) + comma)
    lines.append("  ]")
    lines.append("}")
    path.write_text("\n".join(lines) + "\n")
    print(
        f"defenceTraits on {n}/{len(data['npcs'])} npcs "
        f"(race/valakas={race_n}, {len(by_id)} in Mobius XML, {len(skills)} DefenceTrait skills)"
    )


if __name__ == "__main__":
    main()
