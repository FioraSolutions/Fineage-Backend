#!/usr/bin/env python3
"""Split data/quests.json monolith into data/quests/_meta.json + per-quest folders (HTML externalized)."""
from __future__ import annotations

import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import questio  # noqa: E402

src = ROOT / "data" / "quests.json"
if not src.exists():
    raise SystemExit(f"missing {src}")

data = json.loads(src.read_text(encoding="utf-8"))
questio.ensure_meta(
    reference=data.get("reference"),
    max_active=int(data.get("maxActive") or 25),
    talk_range=int(data.get("talkRange") or 150),
)

for q in data.get("quests") or []:
    folder = questio.upsert_quest(q)
    print(f"quest {q['id']}: {folder.name} ({len(q.get('dialogs') or [])} dialogs)")

src.unlink()
print(f"removed {src}")
print(f"pack: {len(list(questio.iter_quest_json()))} quests")
