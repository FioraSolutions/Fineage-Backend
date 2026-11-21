# Fineage load testing

Headless bots in `tools/Fineage.LoadTest` speak the **same Net `PacketEnvelope` protocol** as production clients (`Fineage.Net.Contract`). They are **not** the classic Interlude GameClient binary stream.

## Local stack (Mac or Linux)

1. Start PostgreSQL and Login+Zone (ports **9106 / 5080 / 9777**):

   ```bash
   export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot
   export FINEAGE_LOGIN_LOADTEST_PASSWORD=loadtest-pass-v1
   export FINEAGE_LOGIN_METRICS_PORT=9464
   export FINEAGE_ZONE_METRICS_PORT=9465
   ./scripts/run-local-net.sh start
   ```

   On Mac, this matches `./scripts/run-local-net.sh` with login **9106**, HTTP **5080**, zone **9777** as documented in the root README.

2. Run a scenario:

   ```bash
   dotnet build tools/Fineage.LoadTest -c Release
   dotnet tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll run \
     --scenario tools/Fineage.LoadTest/scenarios/bots-50-town.yaml \
     --spawn-layout town \
     --target fineage \
     --report-dir local-logs/loadtest/my-run \
     --login-log local-logs/login-net.log \
     --zone-log local-logs/zone-net.log \
     --zone-pid "$(cat local-logs/zone-net.pid)" \
     --region-workers 0
   ```

3. Full matrix helper (local VM only — **never** point at public hosts):

   ```bash
   ./scripts/run-load-test.sh matrix
   ```

   Toggles `FINEAGE_ZONE_REGION_WORKERS` between `0` and `4` per run. Parallel region workers stay **off** in production until soak tests pass; the matrix reports behavior with workers enabled for comparison only.

4. Stop servers:

   ```bash
   ./scripts/run-local-net.sh stop
   ```

## Test accounts (config, not code branches)

Login honours `Auth.RegisterIfMissing` only when **both** are set:

| Variable | Purpose |
| --- | --- |
| `FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX` | Account must start with this prefix (e.g. `loadbot`) |
| `FINEAGE_LOGIN_LOADTEST_PASSWORD` | Password must match exactly |

Unset either variable and auto-registration is denied. Bots default to `loadbot#####` / `loadtest-pass-v1` in scenario YAML.

## Scenarios

YAML files under `tools/Fineage.LoadTest/scenarios/` control:

- `botCount`, `rampUpSeconds`, `durationSeconds`
- `spawnLayout`: `town` (tight cluster), `spread` (grid), `siege` (ring clump)
- `actionWeights`: move, attack, skill, say, pickup, trade, party, teleport

## Metrics

- **Server:** scrape `FINEAGE_ZONE_METRICS_PORT` (`GET /metrics`) — tick histogram, overruns, stage histograms, ingress queue (`fineage_command_ingress_queued`), packet rates, rejects/errors.
- **Command ingress profiling (p99 branch):** labeled histograms `fineage_command_apply_ms{command}`, `fineage_command_move_phase_ms{phase}` (`geo`, `geo_find_path`, `reconcile_knownlist`, `fanout_same_cell`, `plan_diff`), `fineage_command_release_phase_ms{phase}` (`flush_inventory`, `fanout_drain`), and counter `fineage_command_deferred_total{reason}`. Reports include p99 per label plus deferred counts.
- **Command→fanout (client):** harness latency from tracked `MoveInput` / attack / skill send until the bot receives its own `MoveState` or combat ack (see `BotObservationLog`).

### Ingress latency profile (step 1, 50-bot town, local VM, budgets 90 ms / 24 per tick)

| Signal | Typical |
| --- | --- |
| Client command→fanout p50 | ~180 ms |
| Client command→fanout p99 | ~2.5–2.9 s |
| Server `request:move` apply p99 | &lt;0.5 ms (histogram floor) |
| Move sub-phases (geo / AOI / plan_diff) p99 | &lt;0.5 ms |
| Release (`fanout_drain`) p99 | &lt;0.5 ms |
| Tick p99 | often pegged at 1000 ms bucket |

**Takeaway:** burst move latency is dominated by **tick scheduling / ingress deferral and outbound delivery**, not per-move geo or knownlist work inside `Apply`. Tune `FINEAGE_ZONE_COMMANDS_BUDGET_MS` / `FINEAGE_ZONE_COMMANDS_PER_TICK` only after checking `fineage_command_deferred_total` and tick overruns, not by micro-optimizing AOI first.
- **Client:** per-bot action RTT, disconnect and stuck counts.
- **Process:** RSS / VmHWM via `/proc` for the zone PID passed to `--zone-pid`.
- **Logs:** `local-logs/login-net.log`, `local-logs/zone-net.log` scanned for exceptions, stage failures, ledger, desync, deadlock, timeout, `handler_error`.

Each run writes `report.json` and `report.md` under `--report-dir`. The report **fails** when log signals or disconnect/stuck thresholds trip.

## L2J Mobius comparison

Mobius uses the **classic** client protocol. The load-test tool targets Fineage Net directly. `./scripts/fetch-mobius-interlude.sh` clones [Mobius L2J on GitLab](https://gitlab.com/MobiusDevelopment/L2J_Mobius) (not vendored in this repo) and builds `L2J_Mobius_CT_0_Interlude` with Ant. A full bot-driven Mobius comparison needs a separate GameClient adapter; see `docs/perf/results-2026-11.md`.

## Safety

Run load tests only against **your** local `./scripts/run-local-net.sh` stack unless explicitly instructed to use another environment.