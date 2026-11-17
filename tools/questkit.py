"""Small authoring kit for quest-engine packs written from Mobius CT_0 quest behaviour (Block H).

Step tables are written by hand per quest (talk / event / kill branches, items, conds, rewards); no Java is copied.
Dialog HTML text comes from the Mobius quest folder; `bypass [-h] Script|Quest <QuestClass> <page>` links become
`quest_event:<id>:<page>`. Every linked page that has no explicit event row gets a plain "show page" event row
(Mobius onEvent returns the event as the page when nothing else matches).
"""
from __future__ import annotations

import json
import os
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOBIUS = pathlib.Path(os.environ.get("MOBIUS_CT0", "/workspace/mobius/L2J_Mobius_CT_0_Interlude"))
QDIR = MOBIUS / "dist/game/data/scripts/quests"

START = [{"op": "create"}, {"op": "start"}]
DONE = {"op": "complete"}


def cond(n): return {"op": "set_cond", "cond": n}
def take(i, c=1): return {"op": "take_item", "itemId": i, "count": c}
def give(i, c=1): return {"op": "give_item", "itemId": i, "count": c}
def adena(c): return {"op": "give_adena", "count": c}
def exp(e, sp=0): return {"op": "give_exp_sp", "exp": e, "sp": sp}
def setv(k, v): return {"op": "set", "key": k, "value": str(v)}
def unset(k): return {"op": "unset", "key": k}
def social(a=3): return {"op": "social", "action": a}
def spawn(npc): return {"op": "spawn_npc", "npcId": npc}


def drop(item, chance=1.0, limit=0, at=None, count=1):
    op = {"op": "drop_item", "itemId": item, "count": count, "chance": chance, "limit": limit}
    if at is not None:
        op["condAtLimit"] = at
    return op


def _page(p: str) -> str:
    return p if p.endswith((".htm", ".html")) else p + ".htm"


def _gates(d: dict, gates: dict) -> dict:
    for k, v in gates.items():
        if v is None:
            continue
        if k in ("atLeast", "below"):
            v = {str(a): b for a, b in v.items()}
        d[k] = v
    return d


def talk(npc, page, when="started", cond=None, ops=(), **gates):
    d = {"npcId": npc, "hook": "talk", "when": when}
    if cond is not None:
        d["cond"] = cond
    _gates(d, gates)
    if ops:
        d["ops"] = list(ops)
    d["htmlFile"] = _page(page)
    return d


def event(npc, name, page=None, ops=(), when="started", cond=None, **gates):
    d = {"npcId": npc, "hook": "event", "when": when, "event": re.sub(r"\.html?$", "", name)}
    if cond is not None:
        d["cond"] = cond
    _gates(d, gates)
    if ops:
        d["ops"] = list(ops)
    d["htmlFile"] = _page(page or d["event"])
    return d


def kill(npc, ops, cond=None, when="started", party=False, **gates):
    d = {"npcId": npc, "hook": "kill", "when": when}
    if cond is not None:
        d["cond"] = cond
    if party:
        d["party"] = True
    _gates(d, gates)
    d["html"] = ""
    d["ops"] = list(ops)
    return d


def _mobius_page(script: str, page: str) -> pathlib.Path | None:
    p = QDIR / script / page
    if p.exists():
        return p
    alt = p.with_suffix(".html" if p.suffix == ".htm" else ".htm")
    return alt if alt.exists() else None


def write(qid: int, folder: str, script: str, name: str, items: list[int], dialogs: list[dict],
          repeatable: bool = False, abortable: bool = True, pages: dict[str, tuple[int, str]] | None = None) -> None:
    """pages: extra pages {file: (npcId, html)} built here (e.g. a Mobius page with a %placeholder% filled in);
    their links are followed like Mobius pages."""
    out = ROOT / "data/quests" / folder
    for old in list(out.glob("*.htm")) + list(out.glob("*.html")):
        old.unlink()
    out.mkdir(parents=True, exist_ok=True)
    link = re.compile(r'bypass (?:-h )?(?:Script|Quest) ' + re.escape(script) + r' ([^"]+?)(?:\.html?)?"')
    npcs_by_page: dict[str, int] = {}
    for d in dialogs:
        if "htmlFile" in d:
            npcs_by_page.setdefault(d["htmlFile"], d["npcId"])
        for f in (d.get("htmlByVar") or {}).get("files", {}).values():
            npcs_by_page.setdefault(f, d["npcId"])
    known_events = {(d["npcId"], d.get("event")) for d in dialogs if d["hook"] == "event"}
    pending = list(npcs_by_page.items())
    written: set[str] = set()
    for page, (npc, text) in (pages or {}).items():
        for m in link.finditer(text):
            ev = m.group(1)
            if (npc, ev) in known_events:
                continue
            target = _mobius_page(script, _page(ev))
            if target is None:
                continue
            dialogs.append({"npcId": npc, "hook": "event", "when": "any", "event": ev, "htmlFile": target.name})
            known_events.add((npc, ev))
            pending.append((target.name, npc))
        (out / page).write_text(link.sub(lambda m: f'quest_event:{qid}:{m.group(1)}"', text), encoding="utf-8")
        written.add(page)
    while pending:
        page, npc = pending.pop()
        if page in written:
            continue
        src = _mobius_page(script, page)
        if src is None:
            raise SystemExit(f"Q{qid}: missing Mobius page {page}")
        text = src.read_text(encoding="utf-8", errors="replace")
        for m in link.finditer(text):
            ev = m.group(1)
            if (npc, ev) in known_events:
                continue
            target = _mobius_page(script, _page(ev))
            if target is None:
                continue  # an action event with no page of its own; the step table must cover it
            dialogs.append({"npcId": npc, "hook": "event", "when": "any", "event": ev, "htmlFile": target.name})
            known_events.add((npc, ev))
            pending.append((target.name, npc))
        text = link.sub(lambda m: f'quest_event:{qid}:{m.group(1)}"', text)
        (out / page).write_text(text, encoding="utf-8")
        written.add(page)
    # Event rows must reference written pages (talk rows may point at a page an event row also uses).
    for d in dialogs:
        if "htmlFile" in d and d["htmlFile"] not in written:
            src = _mobius_page(script, d["htmlFile"])
            if src is None:
                raise SystemExit(f"Q{qid}: missing Mobius page {d['htmlFile']}")
            d["htmlFile"] = src.name
            text = link.sub(lambda m: f'quest_event:{qid}:{m.group(1)}"', src.read_text(encoding="utf-8", errors="replace"))
            (out / src.name).write_text(text, encoding="utf-8")
            written.add(src.name)
    quest = {"id": qid, "name": name, "repeatable": repeatable, "abortable": abortable, "questItems": items,
             "dialogs": dialogs}
    (out / "quest.json").write_text(json.dumps(quest, indent=2) + "\n", encoding="utf-8")
    print(f"{folder}: {len(dialogs)} dialogs, {len(written)} pages")


def attack(npc, ops, cond=None, when="started", **gates):
    """Mobius onAttack step: fires on every landed hit (no page)."""
    d = kill(npc, ops, cond=cond, when=when, **gates)
    d["hook"] = "attack"
    return d


def mobius_page(script: str, page: str) -> str:
    """Raw text of a Mobius quest page (for pages built with a placeholder filled in)."""
    src = _mobius_page(script, _page(page))
    if src is None:
        raise SystemExit(f"missing Mobius page {script}/{page}")
    return src.read_text(encoding="utf-8", errors="replace")


def take_all(*ids): return {"op": "take_all", "items": list(ids)}
def add(k, n=1): return {"op": "add", "key": k, "value": str(n)}


# Interlude first-profession class ids (Mobius PlayerClass.level() == 1).
FIRST_CLASSES = [1, 4, 7, 11, 15, 19, 22, 26, 29, 32, 35, 39, 42, 45, 47, 50, 54, 56]
HUMAN, ELF, DARK_ELF, ORC, DWARF = 0, 1, 2, 3, 4


def collect_all(mobs, n, at, nxt, chance=1.0, limits=None, **gates):
    """Mobius "give one of my item (< n); when every item of the set holds its count, set cond" kill blocks.
    mobs: {npcId: itemId or (itemId, chance)} (several npcs may share an item); limits: per-item counts (default n)."""
    norm = {npc: (v if isinstance(v, tuple) else (v, chance)) for npc, v in mobs.items()}
    items = sorted({item for item, _ in norm.values()})
    lim = {x: (limits or {}).get(x, n) for x in items}
    rows = []
    for npc, (item, p) in norm.items():
        need = {x: lim[x] for x in items if x != item}
        need[item] = lim[item] - 1
        need = {x: c for x, c in need.items() if c > 0}
        rows.append(kill(npc, [drop(item, p, lim[item], nxt)], cond=at, atLeast=need or None, **gates))
        rows.append(kill(npc, [drop(item, p, lim[item])], cond=at, **gates))
    return rows


def spawn_at(npc, x, y, z): return {"op": "spawn_npc", "npcId": npc, "x": x, "y": y, "z": z}
