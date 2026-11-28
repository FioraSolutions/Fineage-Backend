# Operations runbook (#993)

This is the operator procedure for a running Fineage Login + Zone stand. It is based on
the health endpoints, metrics, alerts, release switch, backup/restore and shutdown
behaviour that exist in this repository now. It is not a substitute for
[`monitoring.md`](monitoring.md), [`metrics.md`](metrics.md),
[`deployment.md`](deployment.md) or [`backup-restore.md`](backup-restore.md): those
pages define the contract; this page says what to do when something is wrong.

Default loopback URLs (override with the env vars in `deploy/fineage.env.example`):

| Process | Ready / live | Metrics | Client |
| --- | --- | --- | --- |
| Login | `http://127.0.0.1:5080/health/ready`, `/health/live` | `http://127.0.0.1:9464/metrics` | Net `9106`, HTTP `5080` |
| Zone | `http://127.0.0.1:7780/health/ready`, `/health/live` | `http://127.0.0.1:9465/metrics` | Net `9777`, status `7780` |

`/health/*` returns JSON `{"ok":bool,"checks":[{"name","ok","detail"}]}` and HTTP **200**
when `ok`, **503** otherwise. Liveness never depends on Postgres: a database outage makes
both processes **not ready**, not dead.

## First 10 minutes

1. **Are the processes answering?**
   ```bash
   curl -s -w ' %{http_code}\n' http://127.0.0.1:5080/health/live
   curl -s -w ' %{http_code}\n' http://127.0.0.1:5080/health/ready
   curl -s -w ' %{http_code}\n' http://127.0.0.1:7780/health/live
   curl -s -w ' %{http_code}\n' http://127.0.0.1:7780/health/ready
   curl -s http://127.0.0.1:5080/api/status
   ```
   On a systemd host also: `systemctl status fineage-login fineage-zone` and
   `journalctl -u fineage-zone -n 200 --no-pager` (unit names are `FINEAGE_LOGIN_UNIT` /
   `FINEAGE_ZONE_UNIT`, defaults above). On a script-supervised host, read the process
   log (`local-logs/zone-net.log` for `./scripts/run-local-net.sh`).
2. **Read the failing check, not just the 503.** `fineage_health_check{check=...}` and the
   JSON `checks` array name the stage: `database`, `world`, `state-audit`,
   `economy-reconcile`, `simulation`, `simulation-heartbeat`.
3. **Branch on that name:**

   | You see | Go to |
   | --- | --- |
   | Zone `/health/live` 503, or no tick / watchdog stall | [Zone hang](#zone-hang) |
   | Both `/health/ready` failing `database`, or `migrate --plan` cannot connect | [PostgreSQL unavailable](#postgresql-unavailable) |
   | `FineageDiskLow` / `Critical` / `Unreadable` / `FullIn24h` | [Disk full](#disk-full) |
   | Fan-out age, queue depth or ingress growing; sheds; slow-client spike | [Queue growth](#queue-growth) |
   | Ledger flush / persist stuck / reconcile / state-audit error | [Ledger and reconcile](#ledger-and-reconcile) |
   | Connection cap, handshake timeouts, relog storm | [Reconnect storm](#reconnect-storm) |
   | A deploy failed or you need the previous release | [Release rollback](#release-rollback) |
   | World subsystems missing or unreadable after a start | [World state and sidecars](#world-state-and-sidecars) |

4. **Do not SIGKILL the zone** to “make it restart faster”. SIGTERM (what `systemctl stop`
   sends) runs the graceful path: online characters are saved, the auditable economy
   ledger is flushed, the nine `world_state` documents are committed, then the shutdown
   reconcile runs. SIGKILL skips all of that. `./scripts/run-local-net.sh stop` sends
   SIGTERM and then SIGKILL after about five seconds; on a production host prefer
   systemd and a `TimeoutStopSec` long enough for the flush (tens of seconds, not 5).
5. **Do not edit `characters`, `character_items`, `economy_ledger` or `world_state` by
   hand** to “fix” a divergence. Capture the log line (it names the container and entity)
   and see [Ledger and reconcile](#ledger-and-reconcile).

## Alert index

Every rule in `monitoring/prometheus/rules/fineage-alerts.yml` lands here. Thresholds
come from [`docs/perf/acceptance-gates.md`](../perf/acceptance-gates.md) where a gate exists.

| Alert | Section |
| --- | --- |
| `FineageTargetDown` | [Process down](#process-down) |
| `FineageNotLive` | [Zone hang](#zone-hang) |
| `FineageNotReady` | [First 10 minutes](#first-10-minutes) (then the failing `check`) |
| `FineageRestarted` | [Process down](#process-down) |
| `FineageTickFrozen`, `FineageWatchdogStall`, `FineageTickStageFailures` | [Zone hang](#zone-hang) |
| `FineageTickP99AboveGate`, `FineageTickP999AboveGate`, `FineageTickSkipped` | [Zone hang](#zone-hang) (overload / late steps) |
| `FineageFanoutAgeAboveGate`, `FineageFanoutQueueGrowing`, `FineageCommandIngressGrowing`, `FineageSubmitToDequeueAboveGate` | [Queue growth](#queue-growth) |
| `FineageSlowClientDisconnectSpike`, `FineageReliableAudienceShed`, `FineageFanoutOverflowShed` | [Queue growth](#queue-growth) |
| `FineageConnectionsRejected` | [Reconnect storm](#reconnect-storm) |
| `FineageLedgerFlushFailures`, `FineageEconomyPersistStuck`, `FineagePersistWriteSlow`, `FineageCharacterPersistBehind` | [Ledger and reconcile](#ledger-and-reconcile) |
| `FineageLedgerInvariantViolations`, `FineageReconcileViolations`, `FineageStoredStateAuditErrors` | [Ledger and reconcile](#ledger-and-reconcile) |
| `FineageSilentDataFallback` | [Ledger and reconcile](#ledger-and-reconcile) (required data) |
| `FineageDiskUnreadable`, `FineageDiskLow`, `FineageDiskCritical`, `FineageDiskFullIn24h` | [Disk full](#disk-full) |
| `FineageRssGrowth` | [Zone hang](#zone-hang) (memory) |

## Process down

**Alerts:** `FineageTargetDown` (`up == 0` for 1 m: Prometheus cannot scrape `/metrics`),
`FineageRestarted` (`process_start_time_seconds` changed in the last 15 m).

**Symptom.** `/metrics` connection refused, or the unit is restarting.

**Diagnosis.**

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:9465/metrics   # zone
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:9464/metrics   # login
curl -s http://127.0.0.1:9465/metrics | grep -E 'fineage_build_info|process_start_time_seconds'
# host:
systemctl status fineage-login fineage-zone
journalctl -u fineage-zone -n 200 --no-pager
```

A single restart after a deploy is expected (`FineageRestarted`). A loop is a crash:
read the journal for the boot stage that failed (`world`, `state-audit`,
`economy-reconcile`, a `WorldStateException` / `StoredStateException`, or a migration
error). `FINEAGE_METRICS_BIND` defaults to loopback; Prometheus must be able to reach
that bind. `/metrics` has no authentication.

**Action.** If the process is dead, start it (`systemctl start fineage-zone` or
`./scripts/run-local-net.sh start`). If it is crash-looping, do not keep restarting
blindly: fix the cause in the log (often [PostgreSQL](#postgresql-unavailable), a
[state audit](#ledger-and-reconcile) with `FINEAGE_STATE_AUDIT=block`, or [world
state](#world-state-and-sidecars)).

**Recovery.** `/health/ready` 200 on both, `fineage_health_ready 1`, one
`fineage_build_info` sample for the expected commit.

## Zone hang

**Alerts:** `FineageNotLive`, `FineageTickFrozen`, `FineageWatchdogStall`,
`FineageTickStageFailures`, `FineageTickP99AboveGate`, `FineageTickP999AboveGate`,
`FineageTickSkipped`, and RSS growth (`FineageRssGrowth`).

**Symptom.** Players frozen in place; Zone `/health/live` 503 (simulation heartbeat
older than `FINEAGE_LIVE_MAX_TICK_LAG_MS`, default 30 s); `/health/ready` may already
have been 503 for 5 s (`FINEAGE_READY_MAX_TICK_LAG_MS`). Tick rate is zero, or the
watchdog has reported a step that has not returned.

**Diagnosis.**

```bash
curl -s http://127.0.0.1:7780/health/live
curl -s http://127.0.0.1:9465/metrics | grep -E 'fineage_tick_ms_count|fineage_tick_max_ms|fineage_step_watchdog_stalls|fineage_tick_stage_failures|process_resident_memory_bytes|dotnet_gc_heap_size_bytes'
journalctl -u fineage-zone -n 200 --no-pager | grep -E 'simulation step tick=|watchdog|tick_stage'
```

The watchdog log line names the stuck work, for example:

```text
simulation step tick=N has run 30.0s without finishing: region bodies still running: ...; phase=... work=...
```

or `no region bodies in flight: a world stage, a collect step or the stage barrier is stuck`.
`fineage_tick_max_ms` is how long the running step has been going.
`fineage_tick_stage_failures_total{stage}` is a thrown stage (the log has the exception).
p99 above 50 ms / p99.9 above 100 ms / skipped ticks is overload, not necessarily a deadlock:
look at stage p99, players online and NPC activation backlog, and compare with a Debug soak
at the same load (`docs/perf/acceptance-gates.md`).

For `FineageRssGrowth` (RSS 512 MiB above its 6-hour low and still rising): compare RSS
with `dotnet_gc_heap_size_bytes`. A growing heap is managed retention (`dotnet-gcdump -p
<pid>`); a flat heap with growing RSS is native memory or fragmentation.

**Action.**

1. **Capture first, then restart.** A dump of a live stuck process is worth more than
   another restart:
   ```bash
   # pid from systemctl or the zone log / health detail ("pid N")
   dotnet-dump collect -p <zone-pid> -o /tmp/zone-$(date -u +%Y%m%dT%H%M%SZ).dmp
   ```
2. Restart with SIGTERM, not SIGKILL:
   ```bash
   sudo systemctl restart fineage-zone
   # or, if you must signal by hand:
   kill -TERM <zone-pid>
   ```
   Wait until the process has exited (systemd's `TimeoutStopSec`, or `wait` / `systemctl`
   status). Confirm `/health/ready` 200.
3. If it hangs again immediately, keep the dump and the watchdog line and escalate; do not
   raise the watchdog timeout or the liveness lag to hide it.

**Recovery.** `/health/live` and `/health/ready` 200; `rate(fineage_tick_ms_count[1m])` > 0;
`fineage_step_watchdog_stalls` not still climbing; players can enter.

**Escalation.** Watchdog line + dump + a metrics scrape from the stuck period. A repeat hang
is a bug, not a “restart it again” event.

## PostgreSQL unavailable

**Alerts:** `FineageNotReady` with `fineage_health_check{check="database"} == 0` on Login
and Zone. Persist alerts in [Ledger and reconcile](#ledger-and-reconcile) often follow.

**Symptom.** Both `/health/ready` 503, check `database`. `/health/live` stays 200. Login
`GET /api/status` can still be HTTP 200 with `"ok":false,"database":false`.

**Diagnosis.**

```bash
curl -s http://127.0.0.1:5080/health/ready
curl -s http://127.0.0.1:7780/health/ready
FINEAGE_DATABASE_URL='Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage' \
  Fineage.Login migrate --plan
# host/service, for example:
systemctl status postgresql
pg_isready -h 127.0.0.1 -p 5432
```

`migrate --plan` is read-only: it does not apply migrations, take the advisory lock, or
write checksums. A non-zero exit is “cannot reach the database”, a checksum mismatch, or a
duplicate/missing migration — not a licence to run `--adopt-checksums` casually (see
[`migrations.md`](migrations.md)).

**Action.** Restore Postgres (start the service, fix `pg_hba` / disk / connections). Do
**not** restart Login or Zone first: the database probe is cached for at most 2 s and
both processes become ready again once the database is back. If `migrate --plan` then
reports pending files, the next process boot will apply them (forward-only). If it
reports tamper or an unknown applied id, stop and use [`migrations.md`](migrations.md);
do not restore a random dump over a live database.

If the data itself is lost, go to [Backup and restore](#backup-and-restore) (fresh
database, guarded restore, then cut over).

**Recovery.** Both `/health/ready` 200, `database` check
`reachable, N migrations applied`. `migrate --plan` prints `up to date`.

**Recorded drill (2026-11-28, 03:26 Europe/Minsk, local box, Debug, throwaway database).**
Zone and Login were up and ready (`database: "reachable, 25 migrations applied"`).
Postgres was stopped with `pg_ctl -D <data> -m fast stop` (the box runs a user-owned
Postgres 17, not systemd):

- Login `/health/live` stayed HTTP 200; `/health/ready` became HTTP 503,
  `database: PostgresException: 57P01: terminating connection due to administrator command`.
- Zone `/health/live` stayed HTTP 200 (`simulation-heartbeat` still passing);
  `/health/ready` became HTTP 503, `database: NpgsqlException: Failed to connect to 127.0.0.1:5432`
  (other checks — `world`, `state-audit`, `economy-reconcile`, `simulation` — stayed ok).
- `GET /api/status` stayed HTTP 200 with `{"ok":false,"login":true,"zone":true,"playersOnline":0,"database":false}`.
- `Fineage.Login migrate --plan` exited 1: `migrate failed: Failed to connect to 127.0.0.1:5432`.

After `pg_ctl start`, **without restarting either process**, both `/health/ready` returned
HTTP 200 again within the 2 s probe cache. That is the recovery to expect.

## Disk full

**Alerts:** `FineageDiskLow` (free < 10 %), `FineageDiskCritical` (free < 5 %),
`FineageDiskUnreadable` (`fineage_disk_probe_ok == 0`), `FineageDiskFullIn24h`
(6-hour trend fills the file system within a day).

**Symptom.** Zone state directory (`FINEAGE_STATE_DIR`, scraped as `path="state"`) or
another registered directory is running out of space. The directory holds `gm-audit.jsonl`,
archived pre-#996 sidecars, and is the file system backups often share; Postgres data
directories live on their own file system and must be watched with `df` as well.

**Diagnosis.**

```bash
curl -s http://127.0.0.1:9465/metrics | grep fineage_disk_
df -h "$FINEAGE_STATE_DIR"
df -h /var/lib/postgresql   # or the cluster's data directory
```

The three families are `fineage_disk_probe_ok{path="state"}`,
`fineage_disk_free_bytes{path="state"}`, `fineage_disk_size_bytes{path="state"}`.
The alerts use `free/size`.

**Action.** Free space **before** a write fails: rotate or compress logs, move old
`local-backups/` off the volume (`FINEAGE_BACKUP_RETENTION_DAYS` / `_COUNT`, defaults 14),
`VACUUM` / drop leftover drill databases on a dev box, enlarge the volume. Do not delete
`world_state` or live `FINEAGE_STATE_DIR` files to make room.

**Recovery.** `fineage_disk_free_bytes / fineage_disk_size_bytes` back above 0.10;
`FineageDiskUnreadable` cleared (`probe_ok 1`).

**Recorded drill (2026-11-28, 03:26 Europe/Minsk, local box).** A 16 MiB tmpfs was mounted
on `/tmp/fineage-disk-993` in a user namespace (`unshare --user --map-root-user --mount`)
and a Debug Zone was started with `FINEAGE_STATE_DIR` on that tmpfs.

| When | `df` | `fineage_disk_free_bytes` | `size` | free/size |
| --- | --- | ---: | ---: | ---: |
| empty tmpfs | 16 M, 0 used | 16777216 | 16777216 | 1.00 |
| after `dd` 15 MiB | 16 M, 15 M used, 1.0 M avail (94 %) | 1048576 | 16777216 | **0.0625** |

`fineage_disk_probe_ok{path="state"}` stayed 1. A ratio of 0.0625 is below the 10 %
`FineageDiskLow` threshold and still above the 5 % `FineageDiskCritical` threshold, which
is what the operator should expect at ~94 % used.

## Queue growth

**Alerts:** `FineageFanoutAgeAboveGate` (fan-out queue age p99 > 40 ms for 10 m),
`FineageFanoutQueueGrowing` (depth p99 rising for 30 m above a floor of 32),
`FineageCommandIngressGrowing` (commands waiting for the simulation rising for 30 m
above 256), `FineageSubmitToDequeueAboveGate` (submit → dequeue p99 > 100 ms for 10 m),
`FineageSlowClientDisconnectSpike` (> 5 slow-client disconnects in 5 m),
`FineageReliableAudienceShed`, `FineageFanoutOverflowShed`.

**Symptom.** Outbound lag, visual updates arriving late, sheds, or clients dropped
because the fan-out backlog was full. Acceptance requires **flat** queues under steady
load; a monotone rise means the consumer is slower than the producer.

**Diagnosis.**

```bash
curl -s http://127.0.0.1:9465/metrics | grep -E 'fineage_fanout_queue|fineage_command_ingress_queued|fineage_command_timeline|fineage_outbound_slow_client|fineage_fanout_reliable_audience_shed|fineage_fanout_overflow_shed|fineage_tick_ms'
```

The Grafana row **Fan-out and outbound** (and the recording rules in
`monitoring/prometheus/rules/fineage-recording.yml`) is the same data with p99.
`FineageSubmitToDequeueAboveGate` is usually a slow tick or saturated ingress, not a
separate queue bug: look at tick p99 first ([Zone hang](#zone-hang) overload).
A few slow-client disconnects from bad links are normal; a spike across many clients is
the server's outbound path or the host network.

**Action.** Do not raise shed limits or the 40 ms age gate to “clear” the alert. Reduce
load or restart only after a scrape and a log excerpt are saved. If the tick is also
over the 50/100 ms gates, treat it as a simulation problem first. If only fan-out age
and depth grow while ticks are healthy, look at recipients per event and
slow-client disconnects (one stalled client should be disconnected, not allowed to
stall the dispatcher).

**Recovery.** Queue depth and age p99 flat and back under the gates; shed counters not
still climbing in a 5-minute window.

## Ledger and reconcile

**Alerts:** `FineageLedgerFlushFailures`, `FineageEconomyPersistStuck` (oldest economy
commit > 30 s), `FineagePersistWriteSlow` (persist write p99 > 1 s for 10 m),
`FineageCharacterPersistBehind` (oldest unsaved character snapshot > 60 s),
`FineageLedgerInvariantViolations`, `FineageReconcileViolations`,
`FineageStoredStateAuditErrors`, `FineageSilentDataFallback`.

**Symptom.** Value-bearing changes are not durable, or the auditable economy ledger, the
store and the live world do not agree. Startup with `FINEAGE_STATE_AUDIT=block` refuses
to run when the stored-state audit or the startup reconcile reports an **error**.

**Diagnosis.**

```bash
curl -s http://127.0.0.1:9465/metrics | grep -E 'fineage_ledger_flush_failures|fineage_economy_persist|fineage_ledger_invariant|fineage_economy_reconcile|fineage_state_audit_issues|fineage_data_silent_fallback|fineage_character'
journalctl -u fineage-zone -n 200 --no-pager | grep -E 'economy reconcile|state audit|ledger'
curl -s http://127.0.0.1:7780/health/ready   # state-audit / economy-reconcile / database
Fineage.Login migrate --plan
```

Useful log lines (already structured):

```text
state audit characters=N pets=N clans=N errors=N notices=N policy=log|block
economy reconcile phase=startup|shutdown ledger-accounts=N stored-owners=N ... violations=N notices=N
economy reconcile violation phase=... <code> ...
```

`FINEAGE_STATE_AUDIT` has exactly two values (`src/Fineage.Zone/Persistence/StoredStateAudit.cs`):

| Value | Meaning |
| --- | --- |
| `log` (default, or unset) | Every finding is logged and counted on `/metrics`. The zone still starts. |
| `block` | An **error** finding throws `StoredStateException` and the zone process exits. Notices do not block. |
| anything else | Boot fails immediately: `FINEAGE_STATE_AUDIT=… is not a policy: use 'log' (default) or 'block'`. |

The reconcile uses the same policy at startup. Shutdown always reconciles with `log` so
a violation is recorded and the process still exits.

`kind` on persist-write p99: `store_gate_wait` is the DB write-gate wait;
`economy` / `snapshot` are the Postgres commits themselves. A stuck persist with a
failing `database` check is [PostgreSQL](#postgresql-unavailable) first.

`FineageSilentDataFallback` (`fineage_data_silent_fallback_total{kind}`) means required
game data fell back silently; the target is zero. The startup log names the kind.
This is a data/packaging bug, not something to paper over.

**Action.**

1. If Postgres is down or slow, fix that first.
2. If a flush is failing with the database up, read `reason` on
   `fineage_economy_persist_failures_total{reason}` (exception type) and the zone log.
   Value is being retried; do not restart in a tight loop — each restart must still be a
   graceful SIGTERM so the final flush can land.
3. If the reconcile or state audit reports a **violation** or **error**: do not UPDATE
   rows to make the numbers match. The log names the container and entity id. With
   `FINEAGE_STATE_AUDIT=block` the zone will not start until the stored state is
   consistent; restore from the last good backup ([Backup and restore](#backup-and-restore))
   or escalate with the log. The path is tested by injection
   (`Store_edit_behind_the_ledger_reaches_the_divergence_alert_metrics` and the promtool
   case in `monitoring/prometheus/tests/fineage-alerts.test.yml`).
4. After a clean shutdown you should see
   `economy reconcile phase=shutdown ... violations=0` in the zone log and
   `fineage_ledger_invariant_violations_total` at 0 on the next start.

**Recovery.** Flush/persist failure counters not rising; oldest persist age low; startup
reconcile `violations=0`; `/health/ready` 200 with `state-audit` and `economy-reconcile`
ok. Under `block`, the process actually starts.

## Reconnect storm

**Alerts:** `FineageConnectionsRejected` (accepts refused at the connection cap),
and often the shed / slow-client alerts in [Queue growth](#queue-growth).

**Symptom.** A large fraction of clients fail handshake or relog (`relog handshake
exceeded 30s` at `zone_hello` in a load-test report); `fineage_connections_rejected_total`
rises; Login/Zone accept queues fill.

**Diagnosis.**

```bash
curl -s http://127.0.0.1:9465/metrics | grep -E 'fineage_connections|fineage_command_ingress|fineage_economy_persist_oldest|fineage_tick_max_ms|fineage_step_watchdog'
curl -s http://127.0.0.1:7780/status
```

Caps are `FINEAGE_ZONE_MAX_CONNECTIONS` / `FINEAGE_LOGIN_MAX_CONNECTIONS` (defaults in
`deploy/fineage.env.example`). Raise a cap only if the host has headroom. A relog storm
can also queue behind the store write gate (`store_gate_wait`, character persist age) and
stall ticks; that mechanism and the `reconnect-storm` load profile are described in
[`docs/perf/reconnect-churn-tick-stall.md`](../perf/reconnect-churn-tick-stall.md).

**Action.** If the cap is the cause and the host is fine, raise it deliberately and
record the new value. If ticks are freezing or persist age is climbing, treat it as
[Zone hang](#zone-hang) / [Ledger and reconcile](#ledger-and-reconcile), not as “more
connections”. Do not disable the slow-client disconnect; it protects the fan-out
dispatcher.

**Recovery.** New logins and relogs succeed; `fineage_connections_rejected_total` not
rising in a 5-minute window; tick and persist age back under gates.

## Release rollback

**Alerts:** none of their own; a failed deploy prints `ROLLED BACK: production is on
<previous>` or `rollback refused: <sha> applied N forward-only migration(s)`.

**Symptom.** The new release is not ready within `FINEAGE_READY_TIMEOUT` (default 240 s),
or you need to return to a previous commit by hand.

The layout and the automatic path (`deploy/deploy-to-host.sh` →
`deploy/release-switch.sh`) are in [`deployment.md`](deployment.md). In short: releases
live under `<app-root>/releases/<id>/`, `current` is switched with one `rename(2)`, and
a failed readiness gate switches `current` back and restarts, unless the new binary
already applied forward-only migrations (then rollback of the binaries alone is
refused — fix forward, or restore the database from backup and then roll back by hand).

**Manual rollback** (on the host, in `<app-root>`), after you have identified a release
that can actually boot on the current database (`Fineage.Login migrate --plan` from that
release is clean):

```bash
ls -lt releases/
ln -sfn releases/<previous> current.next && mv -T current.next current
sudo systemctl restart fineage-login fineage-zone
curl -s -w ' %{http_code}\n' http://127.0.0.1:5080/health/ready
curl -s -w ' %{http_code}\n' http://127.0.0.1:7780/health/ready
```

Both must print `… 200`. A 503 body names the failing check.

If `migrate --plan` on the previous binaries reports a migration the old build does not
have, **do not** switch back: restore the database first
([Backup and restore](#backup-and-restore)) or fix forward.

**Recovery.** `current` points at the intended release; both `/health/ready` 200;
`fineage_build_info` shows the expected commit.

**Recorded drill (2026-11-28, 03:27 Europe/Minsk, local box, no production host).**
`bash tests/deploy/release-switch.test.sh` (the same CI job `deploy-script-test`) passed
13 checks, including: a good release going live, a broken release rolled back to the
previous release (the readiness failure is named, the failed release is kept), rollback
refused after forward-only migrations (current stays on the migrating release), pruning
that never drops the previous release, and a failed preflight that leaves `current`
untouched. This is the scripted rollback path; it does not log into a homeserver.

## World state and sidecars

Since #996 the nine world subsystems live in PostgreSQL `world_state` and are committed
with the auditable economy ledger (see
[`docs/architecture/world-state-storage.md`](../architecture/world-state-storage.md)):

`boss-schedule`, `seven-signs`, `olympiad`, `lottery`, `raid-boss-points`, `punishment`,
`monster-race`, `fishing-championship`, `pc-cafe`.

`FINEAGE_STATE_DIR` keeps `gm-audit.jsonl` and any archived pre-#996 JSON sidecars. There
is no manual “import” command: on boot, `WorldStateCutover` reads a leftover sidecar
**once**, writes the row and a `world_state_imports` record in one transaction, and
renames the file to `<file>.imported-<utc>`. A second source of truth, invalid JSON, or
an unknown `schema_version` stops the start (`WorldStateCutoverException` /
`WorldStateException`). `FINEAGE_STATE_REQUIRE_EXISTING=1` makes a missing `world_state`
row an error instead of a fresh start (the restore drill uses this).

**Diagnosis.**

```bash
curl -s http://127.0.0.1:7780/health/ready    # world / state-audit
ls -l "$FINEAGE_STATE_DIR"/*.imported-* 2>/dev/null
psql "$LIBPQ_URL" -c "SELECT system, schema_version, revision, updated_at FROM world_state ORDER BY system;"
psql "$LIBPQ_URL" -c "SELECT system, source_path, archived_path, imported_at FROM world_state_imports;"
```

A missing row on a stand that should already have one is a restore / cut-over problem,
not a reason to invent a JSON file and drop it in the state directory.

**Action.** If boot refuses the state, fix or restore the database; do not delete the
`world_state` row to force a “fresh” world unless you intend to lose that subsystem.
After a graceful SIGTERM the nine rows must be present (the shutdown flush registry
is closed: a new subsystem cannot be added without joining it).

## Backup and restore

Full contract: [`backup-restore.md`](backup-restore.md). The commands are
`scripts/backup-state.sh` and the disposable exercise `scripts/restore-drill.sh`.

```bash
# Backup (Npgsql FINEAGE_DATABASE_URL or a libpq URI — the script converts the former)
FINEAGE_BACKUP_RETENTION_DAYS=14 FINEAGE_BACKUP_RETENTION_COUNT=14 \
  ./scripts/backup-state.sh backup \
  --database-url "$FINEAGE_DATABASE_URL" \
  --state-dir /srv/fineage/state

./scripts/backup-state.sh verify --backup-dir local-backups/<timestamp>

# Restore is refused unless all of these hold:
#   FINEAGE_RESTORE_CONFIRM=disposable-only
#   target is a fineage-drill-* container labelled fineage.role=backup-restore-drill,
#     or a 127.0.0.1/localhost URL whose database name starts with fineage_restore or fineage_drill
#   target database is empty (store_snapshot absent) and the state directory is empty
FINEAGE_RESTORE_CONFIRM=disposable-only ./scripts/backup-state.sh restore \
  --backup-dir local-backups/<timestamp> \
  --database-url "Host=127.0.0.1;Port=5432;Database=fineage_restore_<id>;Username=...;Password=..." \
  --state-dir /tmp/fineage-restore-state
```

`restore-drill.sh` does the full disposable exercise (two `postgres:16-alpine`
containers, source Zone start/graceful stop, backup, restore, byte comparison,
`migrate --plan`, optional bot smoke). It needs Docker. After restore, Login+Zone must
come up with `FINEAGE_STATE_REQUIRE_EXISTING=1`.

A backup is not evidence that the live ledger and store agree; run the shutdown
reconcile / check `fineage_ledger_invariant_violations_total` as well.

**Recorded drill (2026-11-28, 03:27 Europe/Minsk, local box, no Docker).** A Debug Zone
was started against a throwaway database, one character (`BakHero`) was created, the
zone was stopped with SIGTERM (exit 0), then:

- `backup-state.sh backup --database-url "$FINEAGE_DATABASE_URL"` (Npgsql form) wrote
  `local-backups`-style dir `20261128T002718Z`, dump 89 811 bytes, 122 TOC entries.
- `backup-state.sh verify` printed `VERIFIED_BACKUP=...`.
- `backup-state.sh restore` without `FINEAGE_RESTORE_CONFIRM=disposable-only` exited 1:
  `restore refused: set FINEAGE_RESTORE_CONFIRM=disposable-only`.
- The same restore with the confirm flag into `fineage_restore993` and an empty state
  directory succeeded (`restore complete: database + 1 sidecar file(s)` — the
  `gm-audit.jsonl` sample). Comparisons: characters 1=1, character_items 12=12,
  accounts 1=1, `world_state` 9 systems and matching revision hash,
  `schema_migrations` 25=25. `Fineage.Login migrate --plan` on the restored database:
  `applied=25 pending=0 unverified=0` / `up to date`.

`scripts/restore-drill.sh` was not run on this box (no Docker). The commands above are
the same primitives that drill uses, against the guarded `--database-url` path.

## Graceful shutdown (SIGTERM)

**What happens.** Both Login and Zone register `PosixSignalRegistration` for `SIGTERM`
in addition to `Console.CancelKeyPress` (SIGINT / Ctrl+C). A service manager's SIGTERM
cancels the same shutdown token as Ctrl+C.

On Zone, after the Net listener returns, `Program.cs` always:

1. waits for the simulation loop to finish,
2. `ShutdownGracefully()` — detach and persist every online player, close ingress, drain
   fan-out, flush the nine `world_state` systems,
3. `FlushLedgerOnShutdown()` — drain the auditable economy ledger,
4. `ReconcileEconomyAndLog("shutdown")`.

On Login, SIGTERM cancels the Net accept loop and disposes the HTTP host so in-flight
work is not cut off mid-request. Login has no online-player save of its own.

**How to stop.**

```bash
sudo systemctl stop fineage-zone fineage-login          # SIGTERM, then wait TimeoutStopSec
kill -TERM <zone-pid>                                   # same signal; wait until the pid exits
# prove it:
# Zone must exit 0 (143 = 128+15 means the runtime died on SIGTERM without the handler)
```

**Test.** `tests/deploy/sigterm-shutdown.test.sh` (also run by
`tests/Fineage.Zone.Tests/SigtermShutdownTests`, category Integration) starts Login+Zone
against a throwaway database, puts one load-test bot in the world, and sends **SIGTERM
only** (not SIGINT). It fails unless both processes exit 0, the bot's character row is
persisted with a world position, `world_state` has all 9 rows, and the zone log has
`economy reconcile phase=shutdown` with no violation and no `shutdown save` error.

**Recorded drill (2026-11-28, 03:23 Europe/Minsk, local box, Debug).**
`bash tests/deploy/sigterm-shutdown.test.sh`:

- Both processes ready; the bot was in the world after 3 s (`playersOnline=1`).
- Character `sigtermbot00000` / `sigtermbddlh0000` already had
  `has_position=true` at `(-71338,258271,-3104)` before the signal.
  `updated_at` advanced on SIGTERM from `03:23:01.434465+03:00` to
  `03:23:01.722131+03:00` — the shutdown save wrote the row again.
- Zone exit 0, Login exit 0 (not 143).
- `world_state` 9 rows:
  `boss-schedule,fishing-championship,lottery,monster-race,olympiad,pc-cafe,punishment,raid-boss-points,seven-signs`.
- Zone log (UTC in the file, 03:23:01 Europe/Minsk):
  `economy reconcile phase=shutdown ledger-accounts=0 stored-owners=1 live-containers=0 ground-stacks=0 violations=0 notices=1`.

## Recorded drills (summary)

All three required kinds, plus the health/Postgres and disk exercises, were run on the
local Debug box on **2026-11-28** (times Europe/Minsk, UTC+3). No production host was
touched.

| Drill | Time | Result |
| --- | --- | --- |
| Hang / restart path: SIGTERM of a Zone with a player online | 03:23 | Zone + Login exit 0; character `updated_at` advanced; 9 `world_state` rows; shutdown reconcile `violations=0` |
| Postgres loss | 03:26 | live 200, ready 503 (`database`); `migrate --plan` exits 1; ready 200 again after `pg_ctl start` with **no** process restart |
| Disk full (16 MiB tmpfs, 15 MiB fill) | 03:26 | `fineage_disk_free_bytes/size = 0.0625` (Low, not Critical); `probe_ok=1` |
| Backup + guarded restore | 03:27 | dump 89 811 B, verify ok; restore refused without confirm; restore into `fineage_restore993` matched characters/items/accounts/world_state/migrations; `migrate --plan` up to date |
| Rollback (`tests/deploy/release-switch.test.sh`) | 03:27 | 13/13 checks passed, including atomic rollback, refused rollback after migrations, and failed preflight |

