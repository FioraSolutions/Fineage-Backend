# Deployment, readiness, and rollback (#989)

`deploy/deploy-to-host.sh` stages the published Login/Zone/LoadTest trees on the host and calls
`deploy/release-manager.sh`. The manager owns one app root:

```
<app root>/releases/<id>/login   immutable Login publish tree
<app root>/releases/<id>/zone    immutable Zone publish tree
<app root>/releases/<id>/smoke   the existing Fineage.LoadTest smoke client
<app root>/current -> releases/<id>
<app root>/previous -> releases/<known-good-id>
<app root>/login -> current/login
<app root>/zone -> current/zone
<app root>/data, logs, fineage.env, handoff.secret  shared, never replaced
```

`current` and `previous` are replaced with one `rename(2)` of a symlink. A failed switch therefore
leaves the old pointer intact. The first deploy imports the legacy live `login/` and `zone/`
directories as a bootstrap release, so the first managed deploy already has a rollback target.

## Stages

1. **Stage.** Publishes are rsynced to `releases/.staging-<id>-<pid>`, never over live files.
2. **Migration preflight.** The newly staged `Fineage.Login migrate --plan` runs against the shared
   database. A checksum/missing-file/unreachable-database failure aborts before any pointer move.
   Pending migrations are reported and applied by the new process on boot, as documented in
   [migrations.md](migrations.md).
3. **Atomic switch.** The staging directory is moved to `releases/<id>`, `current` is replaced
   atomically, and the prior `current` target is recorded in `previous`.
4. **Restart.** systemd restarts `fineage-login` and `fineage-zone` (or `FINEAGE_RESTART=scripts`
   uses the existing run scripts).
5. **Readiness.** Both `/readyz` endpoints must return HTTP 200 with `{"status":"ready","ready":true}`
   before the deploy can succeed. A timeout or a 503 with reasons triggers rollback.
6. **World-entry smoke.** `Fineage.LoadTest --smoke` authenticates a dedicated `deploysmoke00000`
   account and enters the world. The password is generated into `<app root>/smoke.secret` (mode 600)
   on first use. A failed enter triggers rollback. `FINEAGE_DEPLOY_SMOKE=skip` is the only way to
   skip it, and it is logged explicitly.
7. **Record.** `<app root>/last-deploy.json` records release, previous, result, and reason.

## Readiness contract

- `GET /livez` and `GET /healthz` are cheap process liveness. They do not check dependencies.
- `GET /readyz` is the deploy contract: HTTP 200 only when ready, otherwise **503** with
  `status: "not_ready"`, a `checks` map, and concrete `reasons`.
- Login readiness checks PostgreSQL, `schema_migrations` current, the `economy_ledger` table, the
  bound Net listener, and the validated handoff secret.
- Zone readiness checks the same database/migration/ledger state plus required catalogs, loaded
  world and NPC spawns, all eight world-state sidecars, the handoff secret, the running simulation,
  and the bound Net listener. Zone exposes it on its loopback status host (default `7780`).
- A missing probe is a failed check. There is no silent healthy default.

`GET /api/status` on Login now exposes `database`, `migrations`, `ready`, and `reasons` in addition
to the play-landing `zone`/`playersOnline` fields. It returns 503 when Login itself is not ready;
a ready Login with a down Zone stays 200 with `zone:false` and `ready:false`.

## Rollback

On a restart, readiness, or smoke failure the manager:

1. switches `current` back to `previous`;
2. restarts the units;
3. waits for the previous release's `/readyz` to pass;
4. writes `result: rolled_back` only if that check passes.

The failed release is kept under `releases/<id>` for inspection. If the previous release also fails
readiness, the manager reports `ROLLBACK FAILED` and `result: rollback_failed`; it never claims a
successful rollback. An operator then uses `releases/`, `last-deploy.json`, and the systemd journal.

Manual rollback uses the same verified path:

```bash
sudo bash /home/nikita/fineage-backend/deploy/release-manager.sh rollback \
  --app-root /home/nikita/fineage-backend/deploy
```

The local fake-host drill in `tests/Fineage.Net.Tests/DeployReleaseManagerTests.cs` starts a
readiness server that answers 503 for the new release and 200 for the previous one, then proves the
deploy exits non-zero, `current` points back at the previous release, and `last-deploy.json` says
`rolled_back`.
