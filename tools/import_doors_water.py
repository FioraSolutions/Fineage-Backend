#!/usr/bin/env python3
"""Transcribe Mobius CT 0 Interlude doors, water, and peace zones into data JSON.

Reads the Mobius datapack (not copied into this repo) and writes original JSON (no GPL source copied).

Mobius reference (L2J_Mobius_CT_0_Interlude):
  dist/game/data/Doors.xml        (DoorData: node1..4 polygon, nodeZ, height, default_status; missing = close)
  dist/game/data/zones/water.xml  (ZoneManager WaterZone: Cuboid nodes + minZ/maxZ)
  dist/game/data/zones/peace.xml  (PeaceZone NPoly + minZ/maxZ)
  dist/game/data/zones/ssq.xml    (PeaceZone rows only)
  dist/game/data/zones/pvp.xml    (ArenaZone NPoly, Mobius ZoneId.PVP)
  dist/game/data/zones/castle_siege.xml (SiegeZone NPoly + castleId; PVP only while that siege runs)
  dist/game/data/zones/swamp.xml       (SwampZone NPoly + move_bonus)
  dist/game/data/zones/damage.xml      (DamageZone NPoly + dmgHPSec / reuse / …)
  dist/game/data/zones/no_landing.xml  (NoLandingZone NPoly + dismountDelay)
  dist/game/data/zones/elven_mother_tree.xml (MotherTreeZone NPoly + HpRegenBonus / MpRegenBonus)

Usage:
  python3 tools/import_doors_water.py /path/to/mobius/data [out_dir=data]
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def doors(data: Path) -> list[dict]:
    out = []
    for d in ET.parse(data / "Doors.xml").getroot():
        if local(d.tag) != "door":
            continue
        a = d.attrib
        x, y, z = (int(v) for v in a["pos"].split(";"))
        nodes = [[int(v) for v in a[f"node{i}"].split(",")] for i in range(1, 5)]
        row = {
            "id": int(a["id"]),
            "name": a.get("name", ""),
            "x": x, "y": y, "z": z,
            "nodes": nodes,
            "nodeZ": int(a["nodeZ"]),
            "height": int(a.get("height", "150")),
            "open": a.get("default_status", "close") == "open",
        }
        if a.get("is_wall") == "true":
            row["wall"] = True
        if "open_time" in a:
            row["openTime"] = int(a["open_time"])
        if "close_time" in a:
            row["closeTime"] = int(a["close_time"])
        if "clanhall_id" in a:
            row["clanHallId"] = int(a["clanhall_id"])
        out.append(row)
    return out


def peace(data: Path) -> list[dict]:
    """Mobius PeaceZone NPoly from zones/peace.xml and zones/ssq.xml."""
    out = []
    for file in ("peace.xml", "ssq.xml"):
        for z in ET.parse(data / "zones" / file).getroot():
            if local(z.tag) != "zone" or z.attrib.get("type") != "PeaceZone":
                continue
            a = z.attrib
            if a.get("shape") != "NPoly":
                print(f"skip non-NPoly peace zone {a.get('name')}", file=sys.stderr)
                continue
            nodes = [[int(n.attrib["X"]), int(n.attrib["Y"])] for n in z if local(n.tag) == "node"]
            if len(nodes) < 3:
                continue
            out.append({
                "name": a.get("name", ""),
                "minZ": int(a["minZ"]),
                "maxZ": int(a["maxZ"]),
                "nodes": nodes,
            })
    return out


def arenas(data: Path) -> list[dict]:
    """Mobius ArenaZone NPoly. These are the static ZoneId.PVP regions."""
    out = []
    for z in ET.parse(data / "zones" / "pvp.xml").getroot():
        if local(z.tag) != "zone" or z.attrib.get("type") != "ArenaZone":
            continue
        a = z.attrib
        if a.get("shape") != "NPoly":
            print(f"skip non-NPoly arena {a.get('name')}", file=sys.stderr)
            continue
        nodes = [[int(n.attrib["X"]), int(n.attrib["Y"])] for n in z if local(n.tag) == "node"]
        if len(nodes) < 3:
            continue
        out.append({
            "name": a.get("name", ""),
            "minZ": int(a["minZ"]),
            "maxZ": int(a["maxZ"]),
            "nodes": nodes,
        })
    return out


def sieges(data: Path) -> list[dict]:
    """Mobius SiegeZone NPoly. ZoneId.PVP is applied only while that castle's siege is active."""
    out = []
    for z in ET.parse(data / "zones" / "castle_siege.xml").getroot():
        if local(z.tag) != "zone" or z.attrib.get("type") != "SiegeZone":
            continue
        a = z.attrib
        if a.get("shape") != "NPoly":
            print(f"skip non-NPoly siege zone {a.get('name')}", file=sys.stderr)
            continue
        castle_id = 0
        nodes = []
        for child in z:
            tag = local(child.tag)
            if tag == "node":
                nodes.append([int(child.attrib["X"]), int(child.attrib["Y"])])
            elif tag == "stat" and child.attrib.get("name") == "castleId":
                castle_id = int(child.attrib["val"])
        if castle_id <= 0 or len(nodes) < 3:
            continue
        out.append({
            "name": a.get("name", ""),
            "castleId": castle_id,
            "minZ": int(a["minZ"]),
            "maxZ": int(a["maxZ"]),
            "nodes": nodes,
        })
    return out


def zone_stats(z) -> dict[str, str]:
    out: dict[str, str] = {}
    for child in z:
        if local(child.tag) == "stat":
            out[child.attrib["name"]] = child.attrib["val"]
    return out


def npoly_zones(data: Path, file: str, type_name: str) -> list[dict]:
    """Mobius NPoly zones under zones/<file> with optional <stat> children."""
    out = []
    path = data / "zones" / file
    if not path.is_file():
        print(f"skip missing {path}", file=sys.stderr)
        return out
    for z in ET.parse(path).getroot():
        if local(z.tag) != "zone" or z.attrib.get("type") != type_name:
            continue
        a = z.attrib
        if a.get("shape") != "NPoly":
            print(f"skip non-NPoly {type_name} {a.get('name')}", file=sys.stderr)
            continue
        nodes = [[int(n.attrib["X"]), int(n.attrib["Y"])] for n in z if local(n.tag) == "node"]
        if len(nodes) < 3:
            continue
        stats = zone_stats(z)
        row = {
            "name": a.get("name", ""),
            "minZ": int(a["minZ"]),
            "maxZ": int(a["maxZ"]),
            "nodes": nodes,
        }
        row.update(stats)
        if stats.get("default_enabled", "true").lower() == "false":
            row["enabled"] = False
        out.append(row)
    return out


def swamp(data: Path) -> list[dict]:
    rows = npoly_zones(data, "swamp.xml", "SwampZone")
    for r in rows:
        r["moveBonus"] = float(r.pop("move_bonus", "0.5"))
    return rows


def damage(data: Path) -> list[dict]:
    rows = npoly_zones(data, "damage.xml", "DamageZone")
    out = []
    for r in rows:
        out.append({
            "name": r["name"],
            "minZ": r["minZ"],
            "maxZ": r["maxZ"],
            "nodes": r["nodes"],
            "enabled": r.get("enabled", True),
            "hpPerTick": int(float(r.get("dmgHPSec", r.get("hpPerTick", "200")))),
            "mpPerTick": int(float(r.get("dmgMPSec", r.get("mpPerTick", "0")))),
            "reuseMs": int(r.get("reuse", 5000)),
            "initialDelayMs": int(r.get("initialDelay", r.get("initialDelayMs", 10))),
            "castleId": int(r["castleId"]) if "castleId" in r else 0,
        })
    return out


def mother_tree(data: Path) -> list[dict]:
    rows = npoly_zones(data, "elven_mother_tree.xml", "MotherTreeZone")
    out = []
    for r in rows:
        out.append({
            "name": r["name"],
            "minZ": r["minZ"],
            "maxZ": r["maxZ"],
            "nodes": r["nodes"],
            "hpRegenBonus": int(r.get("HpRegenBonus", r.get("hpRegenBonus", "0"))),
            "mpRegenBonus": int(r.get("MpRegenBonus", r.get("mpRegenBonus", "0"))),
            "affectedRace": int(r.get("affectedRace", "-1")),
            **({"enterMsgId": int(r["enterMsgId"])} if "enterMsgId" in r else {}),
            **({"leaveMsgId": int(r["leaveMsgId"])} if "leaveMsgId" in r else {}),
        })
    return out


def no_landing(data: Path) -> list[dict]:
    rows = npoly_zones(data, "no_landing.xml", "NoLandingZone")
    for r in rows:
        r["dismountDelaySec"] = int(r.pop("dismountDelay", r.get("dismountDelaySec", "5")))
    return rows


def water(data: Path) -> list[dict]:
    out = []
    for z in ET.parse(data / "zones" / "water.xml").getroot():
        if local(z.tag) != "zone" or z.attrib.get("type") != "WaterZone":
            continue
        a = z.attrib
        if a.get("shape") != "Cuboid":
            print(f"skip non-cuboid water zone {a.get('name')}", file=sys.stderr)
            continue
        pts = [(int(n.attrib["X"]), int(n.attrib["Y"])) for n in z if local(n.tag) == "node"]
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        out.append({
            "name": a.get("name", ""),
            "minX": min(xs), "maxX": max(xs), "minY": min(ys), "maxY": max(ys),
            "minZ": int(a["minZ"]), "maxZ": int(a["maxZ"]),
        })
    return out


def main() -> None:
    data = Path(sys.argv[1])
    out_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("data")
    d, w, p, a, s = doors(data), water(data), peace(data), arenas(data), sieges(data)
    sw, dmg, nl, mt = swamp(data), damage(data), no_landing(data), mother_tree(data)
    (out_dir / "doors.json").write_text(json.dumps(d, separators=(",", ":"), ensure_ascii=False, indent=None).replace("},{", "},\n{") + "\n")
    (out_dir / "water-zones.json").write_text(json.dumps(w, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "peace-zones.json").write_text(json.dumps(p, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "pvp-zones.json").write_text(json.dumps(a, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "siege-zones.json").write_text(json.dumps(s, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "swamp-zones.json").write_text(json.dumps(sw, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "damage-zones.json").write_text(json.dumps(dmg, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "no-landing-zones.json").write_text(json.dumps(nl, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    (out_dir / "mother-tree-zones.json").write_text(json.dumps(mt, separators=(",", ":")).replace("},{", "},\n{") + "\n")
    print(f"doors={len(d)} water={len(w)} peace={len(p)} pvp={len(a)} siege={len(s)} swamp={len(sw)} damage={len(dmg)} noLanding={len(nl)} motherTree={len(mt)}")


if __name__ == "__main__":
    main()
