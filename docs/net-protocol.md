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
| `store_snapshot` | Backup / import-export document only (same JSON as `accounts.json`) — **not** runtime SoT for characters |
| `accounts` | Credentials |
| `characters` | Identity, appearance, `x/y/z`, vitals, level/exp/sp, clan flags |
| `character_items` | Durable `object_id`, `item_id`, `count`, `enchant_level`, `loc` (`inventory` / `paperdoll` / `warehouse`), `loc_data` (paperdoll slot) |
| `character_quests` + `character_quest_vars` | Quest state |
| `clans` | Still payload jsonb until a later epic |
| `store_meta` / sequences | `next_character_id`, `next_clan_id`, `next_item_object_id` |

`PostgresAccountStore.FindById` / `List` read **normalized tables** (then hydrate the in-process mirror for hot writes). Login and Zone are separate processes; Zone must not rely on a stale JSON snapshot for enter.

Hot paths (`TrySavePresence`, vitals, inventory replace) update normalized rows without truncating the whole store. Skills are still derived at zone enter (AutoSkills), not persisted.

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

## EnterWorld (zone Net)

After handoff auth, `EnterWorld` runs **one** world join: `ZoneWorld.TryEnterAsync` (same Mobius path as V2 — inventory restore, vitals, AutoSkills, AOI). Net then **projects the live `ZoneEntity`** (no second store read with Human defaults):

| Order | Opcode | Payload |
|-------|--------|---------|
| 1 | `EntitySpawn` `0x0304` | self player: name, classId, **race/sex**, xyz, speeds |
| 2 | `PlayerStats` `0x0305` | level/exp/sp, hp/mp/cp |
| 3 | `ItemList` `0x030A` | bag + paperdoll rows (`objectId`, `itemId`, `count`, `enchant`, `equippedSlot`) |
| 4 | `SkillList` `0x030B` | AutoSkills (`skillId`, `level`, `reuseRemainingMs`) |

Client: stash `PlayerInfo` from spawn → load tile → spawn; apply ItemList via `ZoneV2GearSession`, SkillList via existing skill apply services.

## Presence fanout (Net multiplayer)

V2 MessagePack flushes still go to a discarded stream on the Net path. Net peers get PacketEnvelope fanout via `NetPresenceHub`:

| Event | Opcodes |
|-------|---------|
| Enter | Self bootstrap (above). Then `EntitySpawn` for each known **player and NPC** (`IsSelf=false`; NPC `Kind=Npc`, `TemplateId=npcId`). Entrant CharInfo-shaped spawn to Net player peers. |
| Move | `MoveState` to self + stayed known Net **players**. Newly visible player/NPC → `EntitySpawn`. Forgotten → `EntityDespawn` `0x030C`. |
| NPC chase (sim) | `MoveState` to Net watchers that already know the NPC (`INetPresenceBridge`). |
| Select / Attack | `SelectTarget` `0x030D` → `TargetSelected` `0x030F`. `AttackRequest` `0x030E` → world combat; `AttackState` `0x0310` + `StatusUpdate` `0x0311` via NetBridge (incl. mob swings / hit timer). Approach uses the same MoveState bridge as player move. |
| Leave / disconnect | `EntityDespawn` of leaver to known Net peers. |

AOI (who is on the knownlist):

| Env | Effect |
|-----|--------|
| `FINEAGE_ZONE_AOI_CELL` &gt; 0 | `GridAoi` with that cell size (default design size 3000) |
| else `FINEAGE_ZONE_INTEREST_RANGE` &gt; 0 | `RangeAoi` |
| else | `SingleCellAoi` (whole zone — local village default) |

## Scale seams

- `IAccountStore` — Postgres (normalized character domain) now, JSON for tests
- `IHandoffStore` / `MemoryHandoffStore` — HMAC handoff; Redis later
- `IAoi` — SingleCell / Range / Grid (`FINEAGE_ZONE_AOI_CELL`)

## Local

```bash
./scripts/run-local-net.sh start
./scripts/run-local-net.sh status
```
