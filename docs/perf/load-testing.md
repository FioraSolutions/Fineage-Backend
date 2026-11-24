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

3. Stress ramp **100 → 1000** mixed bots (`./scripts/run-load-stress.sh`, override `LEVELS="100 300"`). Set `FINEAGE_ZONE_OUTBOUND_QUEUE=512` for soak (default in `run-local-net.sh` for local runs).

4. Full matrix helper (local VM only — **never** point at public hosts):

   ```bash
   ./scripts/run-load-test.sh matrix
   ```

   Toggles `FINEAGE_ZONE_REGION_WORKERS` between `0` and `4` per run. Parallel region workers stay **off** in production until soak tests pass; the matrix reports behavior with workers enabled for comparison only.

5. Stop servers:

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

## Heavy-play scenarios (#951)

Warlord pole + AoE trains, multi-party fan-out, and economy-combined loads are documented in [heavy-play-scenarios.md](heavy-play-scenarios.md). Use `FINEAGE_ZONE_LOADTEST_COMBAT=1` and `FINEAGE_ZONE_MAPS=loadtest_aoe_arena` with the `perf-aoe-*.yaml` files.

## Scenarios

YAML files under `tools/Fineage.LoadTest/scenarios/` control:

- `botCount`, `rampUpSeconds`, `durationSeconds`
- `spawnLayout`: `town` (tight cluster), `spread` (grid), `siege` (ring clump)
- `actionWeights`: move, attack, skill, say, pickup, trade, party, teleport

## Metrics

- **Server:** scrape `FINEAGE_ZONE_METRICS_PORT` (`GET /metrics`) — tick histogram, overruns, stage histograms, ingress queue (`fineage_command_ingress_queued`), packet rates, rejects/errors.
- **Client:** per-bot action RTT, disconnect and stuck counts.
- **Process:** RSS / VmHWM via `/proc` for the zone PID passed to `--zone-pid`.
- **Logs:** `local-logs/login-net.log`, `local-logs/zone-net.log` scanned for exceptions, stage failures, ledger, desync, deadlock, timeout, `handler_error`.

Each run writes **`load-test-report.md`** (LLM-ready, capped at ~30 KiB) and **`load-test-report.json`** under `--report-dir` (aliases: `report.md` / `report.json`). The markdown includes run header (git, build, machine, GC env), peak-scrape latency tables, 10s time-series windows, optional `dotnet-counters` runtime stats, structured Error/Warning counts, bot/oracle stats, **#867 gate verdicts**, and an explicit list of **missing** Prometheus names expected after PR #862. CI (`scripts/ci-load-test-smoke.sh`) prints `load-test-report: …/load-test-report.md` after each run and uploads `local-logs/ci-load-smoke/` as the `load-test-smoke` artifact.

## L2J Mobius comparison

Mobius uses the **classic** client protocol. The load-test tool targets Fineage Net directly. `./scripts/fetch-mobius-interlude.sh` clones [Mobius L2J on GitLab](https://gitlab.com/MobiusDevelopment/L2J_Mobius) (not vendored in this repo) and builds `L2J_Mobius_CT_0_Interlude` with Ant. A full bot-driven Mobius comparison needs a separate GameClient adapter; see `docs/perf/results-2026-11.md`.

## Safety

Run load tests only against **your** local `./scripts/run-local-net.sh` stack unless explicitly instructed to use another environment.