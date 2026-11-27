# Backup and restore drill (#987)

PostgreSQL and the Zone JSON sidecars are one recovery unit. Losing either half loses
characters/inventory or world state (boss respawns, Seven Signs, Olympiad, lottery, raid
points, punishments, monster race, fishing championship). This document is the operator
contract and the executable drill.

## What a backup contains

`scripts/backup-state.sh backup` writes one timestamped directory under
`FINEAGE_BACKUP_ROOT` (default `local-backups/`, gitignored):

| file | purpose |
| --- | --- |
| `postgres.dump` | `pg_dump --format=custom --no-owner --no-acl` |
| `sidecar.tar.gz` | `FINEAGE_STATE_DIR` archive (all JSON sidecars + `gm-audit.jsonl`) |
| `pg_restore.list` | `pg_restore --list` TOC; the archive readability/integrity record |
| `sidecars.tsv` | per-file SHA-256, size and path |
| `manifest.json` | dump/sidecar SHA-256, sizes, database name/version and creation time |

The backup refuses to run when any of the eight canonical sidecars is missing. It never
writes a partial archive silently. The eight names mirror `WorldStateSystems.All`
(`src/Fineage.Zone/Persistence/WorldStateShutdown.cs`); a test fails when the script list
drifts from the C# closed list.

### Taking a backup

With a local `pg_dump` client and the live connection string:

```bash
FINEAGE_BACKUP_RETENTION_DAYS=14 FINEAGE_BACKUP_RETENTION_COUNT=14 \
  ./scripts/backup-state.sh backup \
  --database-url "$FINEAGE_DATABASE_URL" \
  --state-dir /srv/fineage/state
```

Inside the database container (no client install):

```bash
./scripts/backup-state.sh backup \
  --pg-container fineage-postgres --pg-user fineage --pg-db fineage \
  --state-dir /srv/fineage/state
```

Retention removes only timestamped directories that contain `manifest.json`, oldest first,
when older than `FINEAGE_BACKUP_RETENTION_DAYS` (default 14) or beyond
`FINEAGE_BACKUP_RETENTION_COUNT` (default 14 copies).

### Integrity check

```bash
./scripts/backup-state.sh verify --backup-dir local-backups/20261127T123810Z
```

`verify` re-hashes both archives, reruns `pg_restore --list` and compares the TOC, and
requires the required table-data entries plus all eight sidecars. A checksum or TOC
mismatch is a hard failure.

## Restore safety

Restore is deliberately not a general-purpose live operation. `backup-state.sh restore`
requires all of:

- `FINEAGE_RESTORE_CONFIRM=disposable-only`;
- a container name starting with `fineage-drill-` and labelled
  `fineage.role=backup-restore-drill`, **or** a `127.0.0.1`/`localhost` URL whose database
  starts with `fineage_restore` / `fineage_drill`;
- an empty target database (`store_snapshot` absent) and an empty target state directory.

Those guards make it impossible to point the restore path at the live database by accident.
Real disaster recovery into production hardware should use a fresh database/container and
the same guarded commands, then cut over explicitly.

## Executable drill

`scripts/restore-drill.sh` does the full disposable exercise:

1. two throwaway `postgres:16-alpine` containers (source + fresh restore target);
2. migrations and two app-created characters through `Fineage.Login create`, plus synthetic
   bulk characters/inventory (throwaway data, no production fixture);
3. source Zone start/graceful stop, which must persist all eight sidecars;
4. backup, `pg_restore --list` verification, restore into the fresh container and a new
   sidecar directory;
5. byte-level comparison of `characters`, `character_items`, `accounts`, `store_snapshot`
   and `schema_migrations`, and `migrate --plan` must say up to date;
6. restored Login+Zone with `FINEAGE_STATE_REQUIRE_EXISTING=1` on allocated high ports;
7. a restored app-created character is selected through Login;
8. optional world entry through the existing `Fineage.LoadTest` bot smoke;
9. the existing ledger invariant API must expose
   `fineage_ledger_invariant_violations_total`; the drill fails loudly if it is absent and
   fails when the value is non-zero. It never infers zero from a missing metric.

Run locally:

```bash
./scripts/restore-drill.sh
```

Useful switches: `FINEAGE_DRILL_KEEP=1` (keep containers/logs),
`FINEAGE_DRILL_SKIP_BUILD=1`, `FINEAGE_DRILL_SKIP_SMOKE=1`, and
`FINEAGE_DRILL_REQUIRE_SMOKE=1` (CI gate for world entry).

## Measured drill (2026-11-27, local disposable run)

Environment: macOS, Docker 29.9.3, .NET 10.0.100, Debug binaries, source data 202
characters / 2,023 inventory rows, 53,538-byte custom dump, eight sidecars. This is a
synthetic fixture; production RTO scales with database size and host I/O.

| measurement | result |
| --- | ---: |
| backup (dump + sidecar archive + TOC + verify) | 4 s |
| restore into fresh container + extract sidecars | 1 s |
| restore start → restored Login/Zone listeners ready | 9 s |
| restore start → smoke/checkpoint end | 10 s |
| characters after restore | 202 |
| inventory rows after restore | 2,023 |
| restored migrations | 24 applied, 0 pending |
| restored characters/inventory/accounts/snapshot/migrations | byte-identical |
| sidecars restored and loaded with `FINEAGE_STATE_REQUIRE_EXISTING=1` | 8/8 |
| ledger invariant metric | present, `0` |

**RPO:** 24 hours maximum with the nightly workflow (`0 3 * * *`), because there is no WAL
archiving. A manual run before a risky change lowers it to the time since that run.
**RTO:** 9 s to restored listeners and 10 s to the smoke/checkpoint on this fixture;
production RTO must be re-measured on production-sized data.

The world-entry bot smoke was attempted during this drill and failed on this macOS host.
The endpoint evidence rules out a scenario port mismatch: Login advertised
`127.0.0.1:52680`, the restored Zone logged `net listen 127.0.0.1:52680`, and the temporary
scenario declared `zonePort: 52680`. The restored Zone process nevertheless stopped shortly
after the listener line, before the bots connected (`zone_connect` connection refused).
The same stack was manually started in the same worktree and accepted a TCP connection, and
a manual bot run reached `botsInWorld=4`; that run then failed its consistency oracle. The
drill therefore reports `PASS_WITH_OPEN_ACCEPTANCE` and keeps "world entry after restore" as
an explicit open acceptance item. The CI workflow can require it with
`require_world_entry=true`.

## Invariant / reconciler boundary

This drill integrates the existing `EconomyInvariantChecker` output that Zone already
publishes (`fineage_ledger_invariant_violations_total` and the zone log). It does **not**
implement or replace the #955/#984 world↔Postgres reconciler. If that API is missing the
drill stops with a "blocking dependency" error; it never reports zero divergence without a
metric. A successful backup/restore is not evidence that the reconciler exists.

## Scheduled CI

`.github/workflows/backup-restore-drill.yml` runs nightly and via `workflow_dispatch`
(heavy job, not on ordinary dev PRs). It builds Release, runs the same script, uploads
`report.md`/`report.json` and logs as an artifact, and deletes the local database copy
after upload. Set `require_world_entry` on a manual dispatch to gate on the bot smoke.
