# World state durability

Issue #984. This document is the inventory and contract for the JSON sidecars the Zone keeps beside PostgreSQL.
It covers the durable writer, the explicit load/fresh-state policy, the shutdown flush registry, and what is still
outside this change (the world ↔ Postgres reconciler).

## What is stored where

PostgreSQL owns accounts, characters, inventories, quests, clan/siege rows, and the ledger
(`src/Fineage.Accounts/Persistence`). A process restart reads them from Postgres.

`FINEAGE_STATE_DIR` (CLI `--state-dir`, default `data/`) owns the world subsystems that are not account rows.
Every file below is written through `Fineage.Game.Persistence.AtomicJsonFile`:

| File | System id | Owner | Snapshot |
| --- | --- | --- | --- |
| `boss-schedule.json` | `boss-schedule` | `BossSchedule` / `BossActor` | raid and grand-boss life status, respawn time, HP/MP |
| `seven-signs.json` | `seven-signs` | `SevenSignsEngine` | cycle, period, cabal/seal scores and owners, player contributions, next change |
| `olympiad.json` | `olympiad` | `OlympiadBoard` | period, nobles, heroes, ranking, unclaimed passes |
| `lottery.json` | `lottery` | `LotteryBoard` | running lottery, draws, sold tickets, next serial |
| `raid-boss-points-store.json` | `raid-boss-points` | `RaidBossPointsManager` | per-character points by boss |
| `punishments.json` | `punishment` | `PunishmentManager` | active account/character bans, chat bans and jails |
| `monster-race-state.json` | `monster-race` | `MonsterRaceBoard` | race number/state, bets, runners, speeds, history (separate from the `data/monster-race.json` settings catalog) |
| `fishing-championship.json` | `fishing-championship` | `FishingChampionshipBoard` | current top five, winners, period end |

`gm-audit.jsonl` is an append-only audit stream, not a snapshot; it is not rewritten or versioned by the snapshot
mechanism. Data catalogs (`items.json`, `npcs.json`, …) are read-only inputs and are not world state.

`monster-race-state.json` is deliberately separate from the `data/monster-race.json` settings catalog because the
default state directory is `data/`. `fishing-championship.json` keeps its historical state filename; production
deployments should point `FINEAGE_STATE_DIR` outside `data/` so state does not share a directory with the catalogs.

## Source of truth

- PostgreSQL is the source of truth for account/character/economy rows.
- The sidecar is the source of truth for the world subsystem it owns: after a restart the subsystem resumes from the
  last durably replaced snapshot, exactly as Mobius reloads its managers from its saved state.
- The sidecar is not a second copy of inventory or adena. World operations that move adena or items still use the
  ledger (issue #955); this document only makes the surrounding non-account state durable.

## Durable write

`AtomicJsonFile.Write` is the only writer:

1. Serialize to a uniquely named temp file in the **same directory** as the target.
2. `FileStream.Flush(flushToDisk: true)` before the replacement, so the temp contents are on disk.
3. Replace the target with the atomic same-directory rename (`File.Move(..., overwrite: true)`).
4. fsync the directory on Unix where the platform/filesystem supports it; Windows relies on the NTFS journal.

A reader therefore sees either the complete old snapshot or the complete new snapshot. A crash before step 3 leaves
the old file; a crash after step 3 leaves the new file. A temp file is never treated as the live snapshot and is
deleted when the write fails.

Each snapshot writes `schemaVersion` (currently `1`). Readers also accept the pre-#984 key `version`, so existing
`data/` directories upgrade in place; the next write emits `schemaVersion`.

## Load errors and fresh-state policy

- **Missing file:** first boot starts that subsystem from its documented fresh snapshot. This is the default because
  a new deployment has no sidecars yet. Set `FINEAGE_STATE_REQUIRE_EXISTING=1` once the state directory has been
  initialized to make a missing sidecar a hard startup error.
- **Corrupt / unreadable file:** startup fails with `StateFileException` naming the system and the exact path, e.g.
  `World state 'boss-schedule' at '/srv/fineage/data/boss-schedule.json' is unusable: invalid JSON: …`.
  The process exits instead of silently starting with an empty schedule.
- **Unsupported `schemaVersion`:** startup fails with the file path, the found version, and the version this build
  supports. There is no automatic downgrade or alias.
- **Explicit escape hatch:** `FINEAGE_STATE_ALLOW_FRESH_ON_CORRUPT=1` logs the rejected path and starts that
  subsystem fresh. Do not use it in production without preserving/analyzing the corrupt file first.

## Shutdown flush registry

`Fineage.Zone.Persistence.WorldStateSystems` is the closed list of the eight stateful sidecars above.
`ShutdownFlushRegistry` maps each system id to its existing save operation. `ZoneWorld` registers all eight and calls
`AssertComplete()` during construction, so the Zone cannot start if a subsystem is missing from the registry. A
future sidecar must be added to the closed list and registered; a test also asserts the world exposes the full list.

`ZoneWorld.ShutdownGracefully()` detaches and persists online players, then calls `FlushAll()` on the registry.
`FlushAll()` attempts every subsystem even if one fails; failures are logged with the system id and the concrete
path/error and stay available for diagnostics. The `Program` shutdown path flushes the ledger in a `finally` so a
sidecar failure cannot skip the journal drain. The old late `bosses.Save()` after `ShutdownGracefully()` is gone:
the registry is the single shutdown save path.

## After a kill -9

- The next start reads the last atomically replaced snapshot; partial temp files are ignored.
- Boss respawn times, Seven Signs cycle, Olympiad period, lottery tickets/draw, raid points, punishments, monster
  race and fishing championship resume from that snapshot.
- Online player rows are persisted through the normal character save/ledger path; the sidecar layer does not
  change Mobius gameplay formulas, schedules, or reward rules.

## Still required for full #984 acceptance

This change is the durability/plumbing half. The world ↔ PostgreSQL coherence half remains:

- a startup reconciler that compares the ledger projection with persisted inventory/adena;
- per-config logging/blocking when the projection diverges;
- the fault-injection reconciliation scenario from `docs/testing/economy-fault-injection.md`.

Those are domain/ledger invariants and belong to the #955 work; this document does not claim that a partial
sidecar-plumbing change closes the entire issue.
