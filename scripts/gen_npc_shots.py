#!/usr/bin/env python3
"""Patch data/npcs.json with Mobius SoulShot / SpiritShot stock + chance from stats/npcs XML.
Keeps one-NPC-per-line formatting. Usage: gen_npc_shots.py <mobius stats/npcs dir>
"""
import json, re, sys, pathlib

src = pathlib.Path(sys.argv[1])
by_id = {}
for f in sorted(src.glob("*.xml")):
    text = re.sub(r"<!--.*?-->", "", f.read_text(), flags=re.S)
    for m in re.finditer(r'<npc id="(\d+)"[^>]*>.*?</npc>', text, re.S):
        body, nid = m.group(0), int(m.group(1))
        def param(name):
            x = re.search(r'<param name="%s" value="([^"]+)"' % name, body)
            return int(float(x.group(1))) if x else None
        row = {}
        for xml_name, key in (
            ("SoulShot", "soulShot"),
            ("SoulShotRate", "soulShotChance"),
            ("SpiritShot", "spiritShot"),
            ("SpiritShotRate", "spiritShotChance"),
        ):
            v = param(xml_name)
            if v is not None and v > 0:
                row[key] = v
        if row:
            by_id[nid] = row

path = pathlib.Path(__file__).resolve().parent.parent / "data" / "npcs.json"
data = json.loads(path.read_text())
n = 0
for npc in data["npcs"]:
    for k in ("soulShot", "soulShotChance", "spiritShot", "spiritShotChance"):
        npc.pop(k, None)
    patch = by_id.get(npc["npcId"])
    if patch:
        npc.update(patch)
        n += 1

# Preserve historical one-NPC-per-line layout.
lines = ['{', f'  "source": {json.dumps(data.get("source", ""))},', '  "npcs": [']
for i, npc in enumerate(data["npcs"]):
    comma = "," if i + 1 < len(data["npcs"]) else ""
    lines.append("    " + json.dumps(npc, ensure_ascii=False, separators=(", ", ": ")) + comma)
lines.append("  ]")
lines.append("}")
path.write_text("\n".join(lines) + "\n")
print(f"soul/spirit shot fields on {n}/{len(data['npcs'])} npcs ({len(by_id)} in Mobius XML)")
