# Database migrations (forward-only)

Login and Zone share one migration runner in `src/Fineage.Accounts/Persistence/MigrationRunner.cs`.
Boot (`PostgresAccountStore.Open`) and `Fineage.Login migrate` use the same runner; there is no
separate migration tool or per-process migration list.

Migrations live in `src/Fineage.Accounts/Persistence/Migrations` and are copied next to the
published binaries. `schema_migrations` is the only history table:

| column | meaning |
|--------|---------|
| `id` | migration file name, e.g. `021_cursed_weapons.sql` |
| `applied_at` | server time when the migration committed |
| `checksum` | `sha256:` of the normalized file, `NULL` only for rows from the pre-checksum runner |

## Rules

1. **Forward-only.** Never edit, rename, reorder or delete a migration that any environment may
   already have applied. The file name is the applied id and the checksum guards its contents.
2. **One new file per change.** Use the next free three-digit prefix:
   `022_short_description.sql`. The prefix is ordering, not a version number per feature.
3. **No new duplicate prefixes.** `021_*` and `022_*` are fine; two `022_*` files are rejected.
   Historical collisions `018_*` (`character_freight`, `wedding_couples`) and `020_*`
   (`account_language`, `augmentation_options`) remain in place because live databases already
   record both ids. They are the only allowlisted duplicates in `MigrationCatalog`.
4. **Rollback is a new forward migration.** Write a compensating `NNN_revert_*.sql`; do not delete
   the original.
5. **No silent repair.** A missing applied file or a checksum mismatch stops boot/deploy with the
   migration id and both checksums. Restore the original file or add a new migration.

## Checksums and upgrade compatibility

- New applications record `sha256:` of the SQL with line endings normalized to LF and a UTF-8 BOM
  removed, so Windows checkouts do not look tampered.
- On boot and before every apply, the runner validates **every** applied row with a checksum before
  writing anything. A mismatch raises `MigrationTamperException`.
- Databases created before checksum tracking have `NULL` checksums. They keep working (upgrade
  compatibility). Boot/`--plan` print an explicit warning per row; checksums are **never**
  backfilled automatically.
- To opt into tamper detection once, after confirming the files are the ones that were applied:

  ```bash
  Fineage.Login migrate --adopt-checksums
  ```

  This command is the only path that writes a checksum for a legacy `NULL` row.

## Dry run

```bash
Fineage.Login migrate --plan        # alias: --dry-run
```

Read-only: it does not create `schema_migrations`, take the advisory lock, or write checksums. It
prints applied/pending/unverified counts and each pending file. Exit code is non-zero on a duplicate
prefix, a tampered/missing applied migration, or an unreachable database.

`deploy/deploy-to-host.sh` runs this from the **newly staged** Login binary before replacing live
binaries. A failed preflight leaves the running services untouched. Pending migrations are not a
failure: they are applied by the new binary on boot.

## Applying and failure retry

`Fineage.Login migrate` applies pending files in file-name order. Each migration runs in its own
transaction and is recorded with its checksum in the same transaction. A failure rolls the file
back and leaves it unrecorded, so a retry re-applies the same file. Anything that committed earlier
is skipped on retry.

A single `pg_advisory_lock(87201401)` serializes Login and Zone boot/migrate. The lock is released
on every exit path.

## Adding a migration

1. Create `NNN_name.sql` with the next free prefix.
2. Keep it idempotent where practical (`IF NOT EXISTS`), but do not rely on that to fix a bad
   already-applied file.
3. Run fast checks (no database needed):

   ```bash
   dotnet test tests/Fineage.Login.Tests/Fineage.Login.Tests.csproj \
     --filter 'FullyQualifiedName~Migration' -c Debug
   ```

   `MigrationCatalogTests` is the CI prefix guard; `MigrationRunnerTests` covers fresh databases,
   existing databases, failure retry, tamper detection and explicit checksum adoption.
4. If the change needs real PostgreSQL semantics, add an `Integration` test and let the
   perf/postgres workflow run it; unit tests must stay runnable without a database.

## Operational notes

- Boot still applies pending migrations. Deploy preflight only prevents starting a build that
  would fail validation; it does not run the migrations early.
- The `.sql` files are part of the publish output. A binary without its `Persistence/Migrations`
  folder fails with `Could not locate Persistence/Migrations SQL folder`, never with an empty run.
- The current migration set is the source of truth for all environments; `store_snapshot` remains a
  backup export, not a migration mechanism.
