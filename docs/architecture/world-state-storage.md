# World state storage

Issue #996. The Zone's world subsystems keep their state in PostgreSQL. It is written in the same transaction as the auditable economy ledger, so a value move and the subsystem state it changes are durable together or not at all. This page covers the table, the write path, the one-time import of the old JSON sidecars, the startup audit and the failure rules. `world-state-durability.md` covers the shutdown flush registry and the account-side audit.

## Systems

| System id (`world_state.system`) | Owner | State | Pre-#996 sidecar |
| --- | --- | --- | --- |
| `boss-schedule` | `BossSchedule` | raid and grand boss life status, respawn time, HP/MP | `boss-schedule.json` |
| `seven-signs` | `SevenSignsEngine` | cycle, period, cabal and seal scores, player contributions | `seven-signs.json` |
| `olympiad` | `OlympiadBoard` | period, nobles, heroes, ranking, unclaimed passes | `olympiad.json` |
| `lottery` | `LotteryBoard` | running lottery, jackpot, draws, sold tickets, next serial | `lottery.json` |
| `raid-boss-points` | `RaidBossPointsManager` | per-character points by boss | `raid-boss-points-store.json` |
| `punishment` | `PunishmentManager` (Zone), `WorldStatePunishments` (Login) | account and character bans, chat bans, jails | `punishments.json` |
| `monster-race` | `MonsterRaceBoard` | race number and state, bets per lane, runners, speeds, history | `monster-race-state.json` |
| `fishing-championship` | `FishingChampionshipBoard` | current top five, winners and their claims, period end | `fishing-championship.json` |
| `pc-cafe` | `PcCafePointsManager` | per-character PC cafe points | `pc-cafe.json` |

`WorldStateSystems.All` is the closed list, and `WorldStateCatalog` gives each entry its schema version and JSON layout. Castle manor has been in its own tables since migration `012_castle_manor` and is not part of this list.

These stay as files because they are not world state:
- content data such as `data/spawns`, drops, skills, `gm-teleports.json` and `starter-kits.json` (read-only inputs);
- the read-only monster race history archive;
- `gm-audit.jsonl`, an append-only audit log.

## Table

Migration `023_world_state.sql`:

- `world_state(system text primary key, schema_version int, revision bigint, payload jsonb, updated_at)`. There is one row per system. `payload` is the subsystem's versioned snapshot. A CHECK makes `payload->>'schemaVersion'` equal `schema_version`, so a reader can refuse an unknown layout before it parses anything.
- `world_state_imports(system, source_path, source_sha256, archived_path, imported_at)` records the one-time sidecar import (see Cutover).

A single generic document per system was chosen over one normalised table per subsystem for three reasons:
- the snapshots already carry versioned schemas;
- the audit needs the whole document anyway;
- the record of value movement is the ledger, not these rows.

## Write path

1. A subsystem changes its state on the simulation thread and calls `Save()`. `WorldStateStore.Save` serialises the snapshot into an in-memory dirty document with the next `revision`. There is no file or database I/O on the tick or in the command stage, and a source guard test (`World_subsystems_do_no_file_io`) keeps it that way.
2. `ZoneWorld.HasDirtyEconomy` counts dirty documents. The economy commit steals them together with the dirty bags (`StealDirtyEconomy`).
3. `EconomyPersistWorker.Apply` calls `IAccountStore.CommitEconomy(entries, bags, worldState)`. On Postgres, the ledger rows, the bags and the documents are written in **one transaction**. The upsert keeps the newer document: `ON CONFLICT … WHERE world_state.revision < EXCLUDED.revision`.
4. On success the stolen documents are marked committed. On failure they go back into the dirty set, where a newer save made since the steal wins, and the next commit retries them with the bags.

Value moves that carry subsystem state therefore land together with it:

| Move | State change |
| --- | --- |
| `LotteryBuy` | the ticket is added to the lottery state |
| `LotteryPrize` | the claimed ticket is forgotten |
| `MonsterRaceBet` | the lane total grows |
| `MonsterRacePayout` | the payout |
| `FishingChampionshipPrize` | the winner is marked claimed (`TryClaim` runs inside the `RecordBagChange` move) |

A crash before COMMIT loses both sides. A crash after COMMIT keeps both. A graceful shutdown and a `kill -9` therefore restart from the same kind of state: the last committed transaction. The only difference is that a graceful shutdown flushes everything first. `WorldStateDurabilityTests` proves the lottery case with a fault-injected commit and with a kill after commit.

## Loading and failure rules

`WorldStateStore.Open(accounts)` reads every row once at start. A subsystem's `Load` is strict:

- **No row.** The subsystem starts from its documented fresh state, as on first boot. With `FINEAGE_STATE_REQUIRE_EXISTING=1` a missing row is an error instead.
- **Unknown `schema_version`, unparsable payload, or row/payload version mismatch.** These throw `WorldStateException`: `World state 'boss-schedule' (database world_state system='boss-schedule') is unusable: …`. The zone does not start. There is no fresh-on-corrupt fallback for database state.

Login reads the `punishment` row through `WorldStatePunishments.Load`, which applies the same rules. Both processes read the same database row, and no process reads a `punishments.json` any more.

## Cutover from sidecars

`WorldStateCutover.Run` runs at Zone start before any subsystem loads. For each system it looks for the legacy file in `FINEAGE_STATE_DIR`:

| Saved row | Legacy file | Result |
| --- | --- | --- |
| any | absent | nothing to do |
| none | present | The file is read **strictly** with `AtomicJsonFile.LoadStrict`: an invalid JSON, missing `schemaVersion` or unknown version throws `WorldStateCutoverException` naming the file, and nothing is imported. The row (revision 1) and its import record (path and sha256) are inserted in one transaction, and the file is renamed to `<file>.imported-<utc>`. |
| present | present, same sha256 as the recorded import | The previous start was killed between the import and the rename, so the file is only archived now. |
| present | present, anything else | **Refused: two sources of truth.** The zone does not start until an operator moves the file away or restores the database it belongs to. |

PC cafe points never had a version in their file, so a file without one is read as version 1.

## Startup audit (reconcile)

`StoredStateAudit.Run` includes `WorldStateAudit.Check` over the saved rows. `EconomyLedger` keeps a tally per (kind, reference) for the kinds in `WorldStateAudit.TalliedKinds`: the move count and the net amount posted to system accounts. The tally covers the whole replayed journal, not just the idempotency window.

| Code | Severity | Check |
| --- | --- | --- |
| `world-state-ledger` | error | The saved tickets of the running lottery N do not equal the `LotteryBuy` moves naming N. |
| `world-state-ledger` | error | The saved bets of the running race N do not equal the adena that `MonsterRaceBet` moves naming N paid to the track. |
| `world-state-invalid` | error | A row does not parse or breaks the subsystem's own rules: a duplicate lottery serial, a ticket for a future lottery, a negative bet or raid points, PC cafe points out of range, or a punishment key that is not a character id. It also fires for a row with a system id this build does not know. |
| `world-state-orphan` | notice | A row names a character the store does not have. |

The findings follow the same `FINEAGE_STATE_AUDIT` policy (`log` or `block`) as the rest of the audit, and every message names the system and the id.

## Backups

The world state is inside `postgres.dump`, and `backup-state.sh` refuses a dump without `TABLE DATA public world_state`. The state directory is still archived for `gm-audit.jsonl` and the archived sidecars, but no file in it is required. The restore drill compares `world_state` row for row.
