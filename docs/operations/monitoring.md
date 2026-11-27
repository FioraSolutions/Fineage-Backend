# Monitoring (#992)

Login and Zone each serve Prometheus text on `GET /metrics` when a metrics port is set. Everything needed to watch
them lives in the repository; nothing here is deployed automatically.

| What | Where |
| --- | --- |
| Metric inventory: every family, unit, process, source and test | [`metrics.md`](metrics.md) |
| Prometheus scrape config | [`monitoring/prometheus/prometheus.yml`](../../monitoring/prometheus/prometheus.yml) |
| Recording rules (percentiles kept for weeks of history) | [`monitoring/prometheus/rules/fineage-recording.yml`](../../monitoring/prometheus/rules/fineage-recording.yml) |
| Alert rules | [`monitoring/prometheus/rules/fineage-alerts.yml`](../../monitoring/prometheus/rules/fineage-alerts.yml) |
| Alert unit tests (`promtool test rules`) | [`monitoring/prometheus/tests/fineage-alerts.test.yml`](../../monitoring/prometheus/tests/fineage-alerts.test.yml) |
| Grafana dashboard | [`monitoring/grafana/dashboards/fineage-overview.json`](../../monitoring/grafana/dashboards/fineage-overview.json) |
| Local Prometheus + Grafana | [`monitoring/docker-compose.yml`](../../monitoring/docker-compose.yml) |

## Connecting

1. **Expose the metrics ports** on the Login and Zone hosts (see `deploy/fineage.env.example`):
   `FINEAGE_LOGIN_METRICS_PORT=9464`, `FINEAGE_ZONE_METRICS_PORT=9465`, `FINEAGE_METRICS_BIND=127.0.0.1`. Keep the
   bind on loopback or a private interface: `/metrics` has no authentication.
2. **Prometheus.** Copy `monitoring/prometheus/` next to Prometheus and edit the two `targets` lists. Job names
   must stay `fineage-zone` and `fineage-login`: the rules and the dashboard select on them. Start Prometheus with
   `--storage.tsdb.retention.time=90d` (and a `--storage.tsdb.retention.size` cap) so weeks of p99 history are kept;
   the recording rules make long-range panels cheap. On a host that already runs Prometheus, add the two jobs and the
   two rule files to its config instead.
3. **Grafana.** Import `monitoring/grafana/dashboards/fineage-overview.json` (Dashboards → New → Import) and pick the
   Prometheus datasource, or mount the provisioning directory as `monitoring/docker-compose.yml` does.
4. **Alertmanager.** The rules carry `severity: critical|warning`. Route them in your Alertmanager; nothing here
   sends notifications.
5. **Local stack.** On a machine that runs Login and Zone locally:
   `docker compose -f monitoring/docker-compose.yml up -d`, then Prometheus at `http://127.0.0.1:9090` and Grafana
   at `http://127.0.0.1:3000`.

Validate changes before committing (CI job `monitoring-rules` runs the same):

```bash
cd monitoring/prometheus
promtool check config prometheus.yml
promtool check rules rules/*.yml
promtool test rules tests/*.test.yml
```

`MetricInventoryTests` (fast suite) fails when a rule or the dashboard reads a metric that is not in the inventory, or
when an alert reads a metric whose inventory row names no existing test.

## Dashboard

`Fineage overview` has one row per concern; percentiles come from the recording rules:

- **Overview:** players online, ready/live per process, failing readiness checks, connections.
- **Simulation tick:** p50/p99/p99.9 against the 50/100 ms gates, steps per second (0 = frozen), longest step and
  watchdog stall, stage p99, overruns/skipped/stage failures.
- **Commands:** command timeline p99 by phase (submit → dequeue → apply → seal → fan-out → mailbox → socket),
  commands waiting for the simulation, ingress wait and rejections.
- **Fan-out and outbound:** queue age p99 against the 40 ms gate, queue depth p99, command → mailbox p99, shed and
  slow-client disconnects, shed bytes by lane.
- **Auditable economy ledger and persistence:** ledger flush and commit failures, commits waiting and their age,
  reconcile/audit findings, persist write p99 by kind (`store_gate_wait` is the DB write-gate wait, `economy` and
  `snapshot` are the commits), character snapshots waiting.
- **Process, GC and disk:** RSS and GC heap, collections and pause fraction, allocation rate, CPU, threads and the
  thread-pool queue, disk free.
- **Network:** frames and bytes per second, handler p99, rejected and errored packets.

## Alerts and what to do

Thresholds come from [`docs/perf/acceptance-gates.md`](../perf/acceptance-gates.md) where a gate exists. Counters
that must stay at zero and that carry labels (reconcile violations, audit errors, silent fallbacks) alert on the
cumulative value, so the first finding fires even though the series did not exist before it.

### FineageTargetDown

`up == 0` for 1 minute: Prometheus cannot scrape `/metrics`. The process is down, the port is not set, or the bind
address is not reachable from Prometheus. Check the service, then `curl http://<bind>:<port>/metrics` on the host.

### FineageNotLive

`fineage_health_live == 0` for 1 minute: the liveness contract (`/health/live`) failed. On Zone the simulation
heartbeat is older than `FINEAGE_LIVE_MAX_TICK_LAG_MS` (default 30 s) or the loop died. Restarting is the expected
response once the logs are captured; a repeat is a bug.

### FineageNotReady

`fineage_health_ready == 0` for 2 minutes. `fineage_health_check{check=...} == 0` names the failing boot stage
(`world`, `state-audit`, `economy-reconcile`) or probe (`simulation-heartbeat`, `simulation`, `database`). A failed
`state-audit` or `economy-reconcile` stage also raises the economy alerts below.

### FineageRestarted

`process_start_time_seconds` changed in the last 15 minutes. One restart after a deploy is expected; repeated
restarts are a crash loop: read the service journal.

### FineageTickFrozen

No simulation step finished for over a minute (`rate(fineage_tick_ms_count[1m]) == 0`). The loop is stuck inside a
step: `fineage_tick_max_ms` shows how long the running step has been going, and the watchdog log names the stage.
Take a dump (`dotnet-dump collect -p <zone pid>`) before restarting.

### FineageWatchdogStall

The step watchdog reported a step that had not returned (`fineage_step_watchdog_stalls` rose). Same checks as
FineageTickFrozen; a stall that recovered still means players saw a freeze.

### FineageTickP99AboveGate

Tick p99 above 50 ms (or p99.9 above 100 ms) for 10 minutes, or late steps skipped ticks (`FineageTickSkipped`).
Look at the stage p99 panel for the stage that grew, players online and NPC activation backlog. Compare with the
Debug soak numbers for the same load.

### FineageTickStageFailures

A simulation stage or region body threw (`fineage_tick_stage_failures_total{stage}`). The log line has the stage,
scope, tick and exception.

### FineageFanoutAgeAboveGate

Fan-out queue age p99 above 40 ms for 10 minutes: the dispatcher worker is behind the simulation. Check fan-out
batch process time, recipients per event and slow-client disconnects.

### FineageSubmitToDequeueAboveGate

Command submit → dequeue p99 above 100 ms for 10 minutes (the submit → apply gate; dequeue → apply is on the same
panel). Commands wait for the next drain: the tick is slow or ingress is saturated.

### FineageQueueGrowing

Fan-out queue depth p99 (`FineageFanoutQueueGrowing`) or commands waiting for the simulation
(`FineageCommandIngressGrowing`) kept growing for 30 minutes above a floor. Acceptance requires flat queues under
steady load; growth means the consumer is slower than the producer and will end in sheds and disconnects.

### FineageSlowClientDisconnectSpike

More than 5 slow-client disconnects in 5 minutes, recipients disconnected because the fan-out backlog was full
(`FineageReliableAudienceShed`), or the hand-off above its hard limit for 5 minutes (`FineageFanoutOverflowShed`).
A few disconnects from bad links are normal; a spike across many clients points at the server's outbound path or the
host network.

### FineageConnectionsRejected

Accepts were refused at the connection cap. Raise the cap only if the host has headroom.

### FineageLedgerFlushFailures

An auditable economy ledger commit failed and was put back for retry (`fineage_ledger_flush_failures_total`,
`fineage_economy_persist_failures_total{reason}`), or the oldest economy commit has waited over 30 s
(`FineageEconomyPersistStuck`). Value-bearing changes are not durable while this lasts. Check Postgres reachability
and the `database` readiness probe; the zone log has the exception type in `reason`.

### FineageReconcileViolations

The ledger ↔ store ↔ live world reconcile found a divergence (`fineage_economy_reconcile_violations_total{phase,code}`),
the startup stored-state audit found an error (`fineage_state_audit_issues_total{severity="error"}`), or the live
ledger invariant check failed (`fineage_ledger_invariant_violations_total`). Value is in two places, missing, or
different between ledger and store. Do not edit rows by hand: the zone log names the container and entity id. With
`FINEAGE_STATE_AUDIT=block` the zone refuses to start on these findings. This path is tested by injection: a store
row edited behind the ledger (`Store_edit_behind_the_ledger_reaches_the_divergence_alert_metrics`) and the promtool
case in `fineage-alerts.test.yml`.

### FineageSilentDataFallback

Required game data fell back silently (`fineage_data_silent_fallback_total{kind}`); the target is zero. The startup
log names the data kind.

### FineagePersistWriteSlow

Persist write p99 above 1 s for 10 minutes for some `kind`, or the oldest unsaved character snapshot is older than
60 s (`FineageCharacterPersistBehind`). `store_gate_wait` high means writers queue on the store's write gate; the
`economy`/`snapshot` kinds are the Postgres commit itself.

### FineageDiskLow

The file system of a registered directory (`path="state"` is the Zone state directory) is below 10 % free
(`FineageDiskLow`), below 5 % (`FineageDiskCritical`), unreadable (`FineageDiskUnreadable`), or the 6-hour trend
fills it within a day (`FineageDiskFullIn24h`). The state directory holds value-bearing sidecars and backups: free
space before the zone fails a write.

### FineageRssGrowth

RSS rose more than 512 MiB above its 6-hour low and is still rising after an hour. Compare with the GC heap panel: a
growing heap is managed retention (take a `dotnet-gcdump`), a flat heap with growing RSS is native memory or
fragmentation.

## History

Prometheus keeps raw samples and the recording-rule series for the configured retention (90 days in the provided
compose file). p99 history on a running stand needs a deployed Prometheus scraping it for days; that deployment is
outside this repository change.
