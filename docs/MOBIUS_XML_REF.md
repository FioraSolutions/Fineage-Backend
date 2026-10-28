# Mobius CT0 Interlude XML reference

Fineage backend catalogs live as hand-maintained JSON under `data/`. **Copy or convert from Mobius XML before inventing stats** (ids, HP, speeds, spawn counts, item pDef, and so on).

## Upstream source

| | |
|---|---|
| **Project** | [Mobius Development / L2J_Mobius](https://gitlab.com/MobiusDevelopment/L2J_Mobius) |
| **Chronicle tree** | `L2J_Mobius_CT_0_Interlude/` |
| **Datapack root (XML)** | `L2J_Mobius_CT_0_Interlude/dist/game/data/` |

GitHub mirrors exist (for example community forks), but the canonical CT0 Interlude layout above is what this repo documents.

## Local reference checkout (gitignored)

Keep a **sparse** clone outside tracked JSON — not the full ~1486 XML files in git:

```bash
mkdir -p ref
git clone --depth 1 --filter=blob:none --sparse \
  https://gitlab.com/MobiusDevelopment/L2J_Mobius.git ref/mobius-ct0-interlude
cd ref/mobius-ct0-interlude
git sparse-checkout set L2J_Mobius_CT_0_Interlude/dist/game/data/stats/items \
  L2J_Mobius_CT_0_Interlude/dist/game/data/stats/npcs \
  L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills \
  L2J_Mobius_CT_0_Interlude/dist/game/data/spawns
```

After checkout, point imports at:

`ref/mobius-ct0-interlude/L2J_Mobius_CT_0_Interlude/dist/game/data`

(or set `FINEAGE_MOBIUS_XML_ROOT` to that absolute path).

Default resolver (when the env var is unset) looks for:

- `ref/mobius-ct0-interlude/dist/game/data` from the repo root, then
- `ref/mobius-ct0-interlude/data` (legacy layout).

Path `ref/mobius-ct0-interlude/` is listed in `.gitignore`.

## Mobius folder → Fineage JSON

| Mobius path under datapack `data/` | Fineage tracked output | Notes |
|-----------------------------------|------------------------|--------|
| `stats/items/*.xml` | `data/items.json` | Weapons, armor, etc. Merge by item id; keep `equip` blocks hand-tuned when XML cond rules are not mapped yet. |
| `stats/npcs/*.xml` | `data/npcs.json` | Template stats (HP, speeds, type). Example: Gremlin **20001** in `20000-20099.xml`. |
| `stats/skills/*.xml` | `data/skills.json` | Future combat/skills; importer writes a stub header until skills are wired. |
| `spawns/**/*.xml` | `data/spawns/*.json` | Territory spawnlists (`<spawn>`, `<territory>`, `<npc count respawnDelay>`). Fixed map rows (Cedric gremlins) are a separate slice. |
| (drops in npc XML / drop groups) | `data/drops/` | Planned; not imported yet. |
| `stats/pcTemplates` / class XML | `data/classes/` | Planned; login class templates stay in code until migrated. |

### Race village spawn folders (client tiles later)

When expanding beyond Talking Island, Mobius groups starter-area spawns under:

- `spawns/ElvenTerritory/`
- `spawns/DarkElfTerritory/`
- `spawns/OrcTerritory/`
- `spawns/DwarvenTerritory/`

Import with `--spawn-files` pointing at the relevant XML files.

## Environment variables

| Variable | Purpose |
|----------|---------|
| `FINEAGE_MOBIUS_XML_ROOT` | Absolute or relative path to Mobius `dist/game/data` (or legacy `data` root). |
| `FINEAGE_MOBIUS_IMPORT_ON_BOOT` | When `1` / `true` / `yes`, Zone process runs a small NPC + spawn import into the resolved `data/` directory before listening. |

## Importer

One-shot CLI (also `--selftest` for CI):

```bash
dotnet run --project src/Fineage.MobiusImport -- --out data --npcs --items --spawn-files TalkingIsland/TalkingIslandMonsters.xml
```

Or:

```bash
./scripts/import-mobius-xml.sh
```

Examples:

```bash
export FINEAGE_MOBIUS_XML_ROOT="$PWD/ref/mobius-ct0-interlude/L2J_Mobius_CT_0_Interlude/dist/game/data"
dotnet run --project src/Fineage.MobiusImport -- --out data --npcs --npc-ids 20001
dotnet run --project src/Fineage.MobiusImport -- --out data --items --item-ids 57,2369
dotnet run --project src/Fineage.MobiusImport -- --out data --spawns --spawn-files TalkingIsland/TalkingIslandMonsters.xml
```

### Conversion rules (implemented)

- **NPC maxHp**: `ceil(Mobius vitals/@hp)`.
- **NPC walk/run**: Mobius `speed/walk|run @ground` × **2** (Gremlin: walk 20→40, run 50→100). Runtime `TalkingIsland.cs` still sends run **80** until spawn/NPC catalog drives wire stats.
- **Items**: `type` → Fineage `kind`; `set` nodes → icon, bodypart, weapon_type, armor_type, stackable; `stat type=pDef` → `pDef`.
- **Spawns**: territory nodes and npc count / respawnDelay preserved in JSON for zone spawn catalog work.

Do **not** commit the full Mobius tree. Prefer regenerated JSON. A slim `data/mobius-xml/` allowlist in `.gitignore` exists only if a few fixture XML files must be tracked; default workflow is converter output.

## Proof path in repo

- `data/npcs.json` — Gremlin **20001** from real Mobius XML (see reference field).
- Importer unit tests use minimal XML under `tests/Fineage.Game.Tests/Fixtures/mobius/` (no upstream clone required in CI).

Runtime Talking Island gremlins still use hard-coded `TalkingIsland.cs` until spawn JSON is wired; catalog JSON is the canonical source for the next slice.
