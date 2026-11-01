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

Import existing JSON once:

```bash
docker compose up -d postgres
FINEAGE_STORE=postgres dotnet run --project src/Fineage.Login -- import-json local-data/accounts.json
```

## Scale seams

- `IAccountStore` — Postgres now, JSON for tests
- `IHandoffStore` / `MemoryHandoffStore` — HMAC handoff; Redis later

## Local

```bash
./scripts/run-local-net.sh start
./scripts/run-local-net.sh status
```
