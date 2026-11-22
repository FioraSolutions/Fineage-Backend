"""Shared helpers for scripts/gen_quests_stub_pack_809.py (issue #809)."""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402

QROOT = questio.mobius_quest_root()

START = [{"op": "create"}, {"op": "start"}]
cond = lambda c: {"op": "set_cond", "cond": c}
DONE = {"op": "complete"}
EXIT = {"op": "exit"}
setv = lambda k, v: {"op": "set", "key": k, "value": str(v)}
adena = lambda n: {"op": "give_adena", "count": n}
exp_sp = lambda e, s=0: {"op": "give_exp_sp", "exp": e, "sp": s}
give = lambda i, c=1: {"op": "give_item", "itemId": i, "count": c}
take = lambda i, c=1: {"op": "take_item", "itemId": i, "count": c}
take_all = lambda *ids: {"op": "take_all", "items": list(ids)}


def drop(i, chance, limit=0, at=None, count=1, party=False):
    row = {"op": "drop_item", "itemId": i, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        row["condAtLimit"] = at
    return row


def spawn_at(npc, x, y, z):
    return {"op": "spawn_npc", "npcId": npc, "x": x, "y": y, "z": z}


def summon_at(npc, x, y, z, despawn_sec=300, owned=True, say=None, gone_vars=None):
    row = {"op": "summon", "npcId": npc, "x": x, "y": y, "z": z, "despawnSec": despawn_sec, "owned": owned}
    if say:
        row["say"] = say
    if gone_vars:
        row["goneVars"] = gone_vars
    return row


def mobius_java(script: str) -> str:
    path = QROOT / script / f"{script}.java"
    if not path.is_file():
        raise SystemExit(f"missing Mobius script {path}")
    return path.read_text(encoding="utf-8")


def parse_int_consts(src: str) -> dict[str, int]:
    out: dict[str, int] = {}
    for m in re.finditer(r"(?:static\s+final\s+)?int\s+([A-Z0-9_]+)\s*=\s*(-?\d+)\s*;", src):
        out[m.group(1)] = int(m.group(2))
    return out


def parse_int_arrays(src: str) -> dict[str, list[int]]:
    out: dict[str, list[int]] = {}
    for m in re.finditer(r"int\[\]\s+([A-Z0-9_]+)\s*=\s*\{([^}]*)\}", src):
        out[m.group(1)] = [int(x) for x in re.findall(r"\b\d{4,6}\b", m.group(2))]
    return out


def resolve_ids(tok: str, consts: dict[str, int], arrays: dict[str, list[int]] | None = None) -> list[int]:
    if tok.isdigit():
        return [int(tok)]
    if tok in consts:
        return [consts[tok]]
    if arrays and tok in arrays:
        return list(arrays[tok])
    return []


def mobius_kill_ids(script: str) -> list[int]:
    src = mobius_java(script)
    consts = parse_int_consts(src)
    ids: set[int] = set()
    for m in re.finditer(r"addKillId\s*\(\s*(\w+)\.keySet\s*\(\s*\)\s*\)", src):
        map_name = m.group(1)
        for put in re.finditer(rf"{re.escape(map_name)}\.put\(\s*(\d+)", src):
            ids.add(int(put.group(1)))
    for m in re.finditer(r"addKillId\s*\(([^)]*)\)", src):
        arg = m.group(1).strip()
        if ".keySet()" in arg:
            continue
        for tok in re.split(r"[,\s]+", arg):
            if not tok or tok == "DROP.keySet()":
                continue
            for i in resolve_ids(tok, consts):
                ids.add(i)
    # C#-style for (int id = A; id <= B; id++) loops in Four Goblets
    for m in re.finditer(r"for\s*\(\s*int\s+id\s*=\s*(\d+)\s*;\s*id\s*<=\s*(\d+)", src):
        lo, hi = int(m.group(1)), int(m.group(2))
        ids.update(range(lo, hi + 1))
    java_path = QROOT / script / f"{script}.java"
    if java_path.is_file():
        sys.path.insert(0, str(ROOT / "tools"))
        from quest_status import parse_mobius

        ids.update(parse_mobius(str(java_path))["kill"])
    return sorted(ids)


def mobius_talk_ids(script: str) -> list[int]:
    src = mobius_java(script)
    consts = parse_int_consts(src)
    arrays = parse_int_arrays(src)
    ids: set[int] = set()
    for fn in ("addStartNpc", "addTalkId"):
        for m in re.finditer(fn + r"\s*\(([^)]*)\)", src):
            for tok in re.split(r"[,\s]+", m.group(1).strip()):
                for i in resolve_ids(tok, consts, arrays):
                    if i >= 30000 or i < 18000:
                        ids.add(i)
    return sorted(ids)


def html_loader(script: str, qid: int):
    def html(name: str) -> str:
        return questio.mobius_quest_html(QROOT, script, qid, name)

    def d(npc, hook, page, when="started", ops=None, event=None, raw=None, **gates):
        row = {"npcId": npc, "hook": hook, "when": when}
        if event is not None:
            row["event"] = event
        row.update(gates)
        if page is not None or raw is not None:
            row["html"] = raw if raw is not None else html(page)
        elif hook in ("kill", "attack"):
            row["html"] = ""
        if ops:
            row["ops"] = ops
        return row

    return d


def kill_stubs(d, mobs: list[int], cond: int = 1, ops=None, party=False):
    ops = ops or []
    return [d(m, "kill", None, cond=cond, party=party, raw="", ops=ops) for m in mobs]


def talk_stub(d, npc: int, cond: int | None, page: str, **gates):
    return d(npc, "talk", page, cond=cond, **gates)