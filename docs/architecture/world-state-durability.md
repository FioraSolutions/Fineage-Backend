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

## World ↔ PostgreSQL coherence (startup state audit)

`Fineage.Zone.Persistence.StoredStateAudit` is the one check between saved state and the running world. The zone
runs it in `Program.cs` right after the world is built (the journal has been replayed into the ledger) and before
the simulation starts, through `ZoneWorld.AuditStoredStateAtStartup()`.

It reads every saved character, pet and clan (`IAccountStore.ListAllCharacters`, `ListAllPets`, `ListClans`) and
reports one `StoredStateIssue` per finding — `code owner container id=…: detail`, for example
`ledger-divergence character 12 'Hero' (account bob) inventory id=0: ledger projects …, store holds …`:

| Code | Severity | Meaning |
|------|----------|---------|
| `missing-reference` | error | A saved row names an item, skill, effect, recipe or henna id the loaded data does not have (inventory, warehouse, freight, pet bag, clan warehouse, learned skills, effects, recipes, dyes). |
| `missing-reference` (quest) | notice | A saved quest row names a quest not in the catalog. The journal keeps the row inactive and saves it back unchanged. |
| `ledger-divergence` | error | The replayed ledger projects different holdings than the store row (bag by template rule: stackables by count, the rest by instance id; warehouse, freight and clan warehouse by count). |
| `ledger-orphan` | error | A character / pet / clan ledger account holds value but the store has no such owner. |
| `ledger-invariant` | error | `EconomyInvariantChecker` found an overdraft or an instance owned twice. |
| `unjournaled` | notice | The store holds a container that has no journal history (pre-ledger rows). Its first ledger move aligns it with an `Untracked` adjustment, which is logged and counted as before. |

Every finding is logged (`state audit …`, errors at Error level), counted in the `fineage.state.audit_issues`
metric (tags `code`, `container`), and summed up in one line:
`state audit characters=N pets=N clans=N errors=N notices=N policy=log|block`.

Policy is `FINEAGE_STATE_AUDIT`:

- `log` (default) — the zone starts; the findings are in the log and the metric.
- `block` — the zone exits with `StoredStateException` listing the errors while any error remains. Notices never
  block. Any other value fails start with a message naming the variable.

The audit never writes. Nothing is corrected or deleted; an operator fixes the data (restore the template, or move
the rows) and restarts.

### Saved rows that name removed ids

The same check guards each enter: `LoadEnteringPlayer` calls `StoredStateAudit.RequireLoadable` before anything is
built, so a character with such a row is refused with every name in `EnterRefusal` and the log
(`enter refused: … StoredStateException: character 12 'Hero' (account bob) cannot be loaded: … learned-skill id=…`).
Before #984 inventory, pet-bag, recipe and effect rows like that were skipped with at most a warning and the next
save of that container deleted them; learned skills and dyes stayed in the row but were silently inactive. Summoning a pet whose saved bag names a missing item is
refused the same way. The loaders behind the check (`LoadInventory`, `EnterSelfPrepare`, `RefreshKnownSkills`,
`RestoreStoredEffects`, `LoadKnownRecipes`) now `Require` the id, so a missing one is an exception, never a skip.

### Offline container writes

Every economic write for a character who is not in the world goes through the economy commit, in the same
transaction as its journal rows: offline freight already did; the offline castle-regalia removal
(`RemoveStoredCastleRegalia`) used to call `TryReplaceItems` directly beside the ledger move and now queues the bag
(`_dirtyOfflineBags`, generalised from the freight-only queue). Ledger reads of an offline bag see that queue first.
A failed commit puts stolen offline writes back in front of anything queued since, so none is lost.

### Proof

`tests/Fineage.Zone.Tests/StoredStateAuditTests.cs` restarts a world over the same store and the same durable
journal (a journal with the Postgres contract: rows become durable only inside the economy commit):

- crash after COMMIT — the first world is dropped without leave or shutdown; the restart's audit is clean and the
  ledger matches the stored bag;
- crash during COMMIT — the commit throws; neither the journal rows nor the bag land; the restart's audit is clean;
- a store row edited behind the ledger is a named `ledger-divergence`, and `block` refuses start;
- an orphan ledger account, a pre-ledger bag (notice only), and saved rows with removed item / skill / effect /
  recipe ids (enter refused, rows kept).

The sidecar crash cases are in `AtomicJsonFileTests`: a crash hook before and after the replace, and a real child
process (`tests/Fineage.StateWriteProbe`) rewriting one snapshot in a loop and killed with SIGKILL at random moments;
every restart reads one complete snapshot.
