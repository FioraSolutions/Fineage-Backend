#!/usr/bin/env python3
"""Shared I/O for Fineage quest pack: data/quests/_meta.json + data/quests/<id>_<slug>/quest.json (+ HTML siblings)."""
from __future__ import annotations

import json
import pathlib
import re
import shutil

ROOT = pathlib.Path(__file__).resolve().parent.parent
QUESTS_DIR = ROOT / "data" / "quests"
META_PATH = QUESTS_DIR / "_meta.json"


def slugify(name: str) -> str:
    s = re.sub(r"[^a-z0-9]+", "_", (name or "quest").lower()).strip("_")
    return (s[:48] or "quest")


def quest_dir(quest_id: int, name: str) -> pathlib.Path:
    return QUESTS_DIR / f"{int(quest_id):05d}_{slugify(name)}"


def ensure_meta(reference: str | None = None, max_active: int = 25, talk_range: int = 150) -> None:
    QUESTS_DIR.mkdir(parents=True, exist_ok=True)
    meta = {"maxActive": max_active, "talkRange": talk_range}
    if reference:
        meta["reference"] = reference
    elif META_PATH.exists():
        old = json.loads(META_PATH.read_text(encoding="utf-8"))
        if "reference" in old:
            meta["reference"] = old["reference"]
    META_PATH.write_text(json.dumps(meta, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def _html_filename(dialog: dict, index: int) -> str:
    parts = [str(dialog.get("npcId", 0)), str(dialog.get("hook", "talk"))]
    if dialog.get("event"):
        parts.append(str(dialog["event"]))
    if dialog.get("cond") is not None:
        parts.append(f"c{dialog['cond']}")
    parts.append(f"{index:03d}")
    return "-".join(parts) + ".htm"


def externalize_html(quest: dict, folder: pathlib.Path) -> dict:
    """Copy quest dict; write non-empty HTML to sibling files and replace with htmlFile."""
    out = dict(quest)
    dialogs = []
    for i, raw in enumerate(quest.get("dialogs") or []):
        d = dict(raw)
        html = d.get("html")
        if html is None:
            dialogs.append(d)
            continue
        text = html if isinstance(html, str) else str(html)
        if text.strip() == "":
            d["html"] = ""
            d.pop("htmlFile", None)
            dialogs.append(d)
            continue
        name = _html_filename(d, i)
        (folder / name).write_text(text, encoding="utf-8")
        d.pop("html", None)
        d["htmlFile"] = name
        dialogs.append(d)
    out["dialogs"] = dialogs
    return out


def find_quest_dir(quest_id: int) -> pathlib.Path | None:
    prefix = f"{int(quest_id):05d}_"
    if not QUESTS_DIR.is_dir():
        return None
    for p in QUESTS_DIR.iterdir():
        if p.is_dir() and p.name.startswith(prefix):
            return p
    return None


def upsert_quest(quest: dict) -> pathlib.Path:
    """Write one quest pack folder (replace existing id folder). Returns folder path."""
    qid = int(quest["id"])
    name = quest["name"]
    ensure_meta()
    for old in list(QUESTS_DIR.iterdir()) if QUESTS_DIR.is_dir() else []:
        if old.is_dir() and old.name.startswith(f"{qid:05d}_"):
            shutil.rmtree(old)
    folder = quest_dir(qid, name)
    folder.mkdir(parents=True, exist_ok=True)
    body = externalize_html(quest, folder)
    (folder / "quest.json").write_text(
        json.dumps(body, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return folder


def upsert_quests(quests: list[dict]) -> None:
    for q in quests:
        upsert_quest(q)


def iter_quest_json() -> list[pathlib.Path]:
    if not QUESTS_DIR.is_dir():
        return []
    return sorted(QUESTS_DIR.glob("*/quest.json"))


def load_all_quests() -> list[dict]:
    rows = []
    for path in iter_quest_json():
        rows.append(json.loads(path.read_text(encoding="utf-8")))
    rows.sort(key=lambda q: q["id"])
    return rows
