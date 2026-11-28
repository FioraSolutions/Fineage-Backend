# Deployment: readiness, liveness, releases and rollback (#989)

Incident procedures, including [manual rollback](runbook.md#release-rollback) and the health-endpoint drill, are in [`runbook.md`](runbook.md).

## Health contract

Both processes answer two loopback-reachable JSON endpoints. Body: `{"ok":bool,"checks":[{"name","ok","detail"}]}`;
status **200** when `ok`, **503** otherwise. A probe that throws is a failed check with the exception, never a pass.

| Endpoint | Login (HTTP, `FINEAGE_LOGIN_HTTP_PORT`, default 5080) | Zone (status host `127.0.0.1:FINEAGE_ZONE_STATUS_PORT`, default 7780) |
|----------|------------------------------------------------------|------------------------------------------------------------------------|
| `GET /health/live` | the process answers | the process answers and, once the simulation has started, its loop passed within `FINEAGE_LIVE_MAX_TICK_LAG_MS` (default 30000) |
| `GET /health/ready` | `database`: connects within 3 s and every migration of this build is applied (read-only plan, checked at most every 2 s) | `world` (catalogs, required-data boot validation, sidecars, world built), `state-audit` (#984 startup audit finished), `simulation` (loop passed within `FINEAGE_READY_MAX_TICK_LAG_MS`, default 5000), `database` (as Login) |

- The zone status host starts **before** the world loads, so during boot `/health/ready` is 503 with
  `world: loading catalogs, boot validation and sidecars` rather than a refused connection. A failed boot exits
  the process (the deploy sees the port go away).
- `GET /api/status` (public landing): `ok` is now Login readiness (was the constant `true`), `login` is "this process
  answers", `database` is the database check, `zone`/`playersOnline` unchanged.
- Liveness never depends on the database: a database outage makes both processes **not ready**, not dead.
- Setting `FINEAGE_ZONE_STATUS_PORT=0` disables the zone status host, and with it the zone health endpoints; the
  deploy gate would then never pass, so leave it on in production.

## Release layout on the host

```
<app-root>/
  releases/<sha>/login  releases/<sha>/zone     one directory per deployed commit, never edited after upload
  current -> releases/<sha>                     switched with one rename(2) (atomic)
  login -> current/login   zone -> current/zone the paths the systemd units and run-*.sh already use
  data/ logs/ fineage.env handoff.secret         shared, outside every release
  release-switch.sh run-login.sh run-zone.sh fetch-geodata.sh
```

The first health-gated deploy adopts the old in-place `login/` and `zone/` directories as `releases/legacy-<time>`,
so it already has a release to roll back to. `FINEAGE_KEEP_RELEASES` (default 5) are kept; the current and the
previous release are never pruned.

## Stages of a deploy (`deploy.yml` → `deploy/deploy-to-host.sh` → `deploy/release-switch.sh`)

1. **Upload** the publish trees to `releases/<sha>.partial/` (rsync), plus the host scripts.
2. **Finalize**: rename `.partial` to `releases/<sha>`.
3. **Preflight**: `releases/<sha>/login/Fineage.Login migrate --plan` (read-only; fails on tamper, unknown applied
   ids, unreachable database). Failure: exit 1, `current` untouched.
4. **Switch** `current` to the new release (atomic), **restart** both units.
5. **Gate**: within `FINEAGE_READY_TIMEOUT` (default 240 s) every URL in `FINEAGE_READY_URLS` (Login and Zone
   `/health/ready`) must answer 200, every port in `FINEAGE_READY_TCP_PORTS` (9106, 9777, 5080) must accept, and —
   when `FINEAGE_SMOKE_USER` / `FINEAGE_SMOKE_PASSWORD` are set in `fineage.env` — `POST /api/auth/login` with
   that test account must return 200.
6. **Success**: prune old releases, exit 0 ("Deploy OK").
7. **Failure**: print the last failing check and the unit logs, switch `current` back to the previous release,
   restart, wait for its readiness, exit 1 ("ROLLED BACK: production is on <previous>"). The failed release stays
   in `releases/` for inspection.

### When the binary rollback is refused

Migrations are forward-only and the runner rejects a database that records a migration the binaries do not have.
If the failed release's preflight reported pending migrations, its boot applied them and the previous release
would refuse to start, so the script does **not** switch back; it exits 1 with
`rollback refused: <sha> applied N forward-only migration(s)` and production stays on the failed release. Then:

- fix forward (push a corrected commit — the next deploy goes through the same gate), or
- restore the database from the last backup (`docs/operations/backup-restore.md`), then roll back by hand (below).

## Manual rollback runbook

On the host, in `<app-root>`:

```bash
ls -lt releases/                                   # pick the release to return to
ln -sfn releases/<previous> current.next && mv -T current.next current
sudo systemctl restart fineage-login fineage-zone
curl -s -w ' %{http_code}\n' http://127.0.0.1:5080/health/ready
curl -s -w ' %{http_code}\n' http://127.0.0.1:7780/health/ready
```

Both must print `... 200`. A 503 body names the failing check (`database`, `world`, `state-audit`, `simulation`).

## Partial failure

| Symptom | Meaning | Action |
|---------|---------|--------|
| Login ready, zone `world` pending for minutes | zone still loading or stuck in boot | check `journalctl -u fineage-zone`; a boot error exits the process with the message |
| Zone `state-audit` 503 / process exited with `StoredStateException` | `FINEAGE_STATE_AUDIT=block` and the audit found errors | read the `state audit` log lines (`docs/architecture/world-state-durability.md`) |
| `database` 503 on both | Postgres down or a migration pending | `Fineage.Login migrate --plan`; bring Postgres back — both become ready without a restart |
| zone `simulation` 503, `live` still 200 | loop lagging beyond 5 s (overloaded or a long step) | watchdog lines name the stuck stage; liveness fails only past 30 s |
| zone `live` 503 | simulation loop dead or stuck > 30 s | restart the zone unit |

## Proof

- `tests/deploy/release-switch.test.sh` (CI job `deploy-script-test`): legacy adoption, good release, broken
  release rolled back atomically, rollback refused after migrations, pruning, failed preflight — against a fake
  service on loopback, no host.
- `LoginHealthTests`, `HealthEndpointTests`: readiness 503 with an unreachable / failing database, 503 while a boot
  stage is pending, simulation heartbeat ok / lagging / ended, throwing probes, thresholds.

The rollback runbook has been exercised only by the scripted scenarios above; the first run on the real host is
the next deploy to `main`.
