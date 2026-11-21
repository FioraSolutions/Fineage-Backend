# Mobius quest dialog HTML (CT0 Interlude)

Vendored NPC dialog pages used by `scripts/gen_quests_*.py` when building `data/quests/*/quest.json`.

Refresh from a local Mobius CT0 checkout:

```bash
python3 scripts/import_mobius_quest_html.py
```

Generators resolve dialogs via `questio.mobius_quest_root()` (cache → this tree → local `ref/`).