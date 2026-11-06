# Quest datapack layout

Mobius keeps one folder per quest (Java + HTML). Fineage mirrors that under `data/quests/`.

```
data/quests/
  _meta.json                 # maxActive, talkRange, reference
  00105_skirmish_with_orcs/
    quest.json               # id, name, questItems, dialogs (ops + gates)
    30218-talk-absent-000.htm  # HTML siblings; dialogs reference htmlFile
    …
```

## Rules

- **One quest = one folder** `NNNNN_slug/` with `quest.json`.
- Talk/event HTML lives as **sibling files**; dialogs use `"htmlFile": "….htm"` (plain name, no paths).
- Kill steps keep `"html": ""`.
- Engine: `QuestCatalog.LoadPack(data/quests)` (see `GameData.Load`).
- Generators call `scripts/questio.py` → `upsert_quest` / `upsert_quests` (rewrites that folder only).
- Do **not** revive a monolith `data/quests.json`.

## Porting a Mobius quest

1. Read `ref/.../scripts/quests/Q00xxx_*/*.java` + HTML.
2. Emit a quest dict (same schema as before).
3. `questio.upsert_quest(quest)` then `gen_drop_items.py` + `gen_quest_item_flags.py`.

Tracking early coverage: [`early-quests-checklist.md`](early-quests-checklist.md), issue #299.
