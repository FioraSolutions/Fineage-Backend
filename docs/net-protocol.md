# Fineage Net (server)

PacketEnvelope login/zone path. Durable accounts use PostgreSQL by default for local Net.

## Ports

| Stack | Login | Zone |
|-------|-------|------|
| Net (`FINEAGE_NET_ENABLE=1`) | 9106 | 9777 |
| Legacy V2 MessagePack | 9206 | 9877 |

## Persistence

| Env | Meaning |
|-----|---------|
| `FINEAGE_STORE=postgres` | `PostgresAccountStore` (default in `scripts/run-local-net.sh`) |
| `FINEAGE_DATABASE_URL` | Npgsql connection string |
| `FINEAGE_STORE=json` | `accounts.json` via `--store` / `FINEAGE_LOGIN_STORE` |

### Postgres layout (Mobius-shaped)

| Table | Role |
|-------|------|
| `store_snapshot` | Full-document backup / import export (same JSON as `accounts.json`) |
| `accounts` | Credentials |
| `characters` | Identity, appearance, `x/y/z`, vitals, level/exp/sp, clan flags |
| `character_items` | Durable `object_id`, `item_id`, `count`, `enchant_level`, `loc` (`inventory` / `paperdoll` / `warehouse`), `loc_data` (paperdoll slot) |
| `character_quests` + `character_quest_vars` | Quest state |
| `clans` | Still payload jsonb until a later epic |
| `store_meta` / sequences | `next_character_id`, `next_clan_id`, `next_item_object_id` |

Hot paths (`TrySavePresence`, vitals, inventory replace) update normalized rows without truncating the whole store. Skills are still derived at zone enter (not persisted).

Import / repair:

```bash
docker compose up -d postgres
FINEAGE_STORE=postgres dotnet run --project src/Fineage.Login -- import-json local-data/accounts.json
FINEAGE_STORE=postgres dotnet run --project src/Fineage.Login -- backfill-normalized
```

## Lobby roster (CharSelect)

`CharacterRoster` / `CreateResult` carry a **Mobius CharSelectionInfo-shaped** summary (same fields as V2 `CharacterSummaryMsg` keys used by the lobby UI):

- identity / appearance / level
- position `x,y,z` (saved or class spawn preview)
- vitals `hp/maxHp`, `mp/maxMp`, `curCp/maxCp` (CP from class template)
- `exp`, `sp`, `karma` (karma 0 until persisted)
- `weaponItemId` + `paperdoll[17]` item ids (not the full bag)
- `deleteSecondsLeft`, `isLastPlayed`

Skills are still derived at zone enter, not listed on CharSelect.

## Scale seams

- `IAccountStore` — Postgres (normalized character domain) now, JSON for tests
- `IHandoffStore` / `MemoryHandoffStore` — HMAC handoff; Redis later

## Local

```bash
./scripts/run-local-net.sh start
./scripts/run-local-net.sh status
```
