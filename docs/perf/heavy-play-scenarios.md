# Heavy-play load scenarios (#951)

These scenarios replace idle random-walk bots with structured **Warlord pole + AoE** combat and party observers so tick and fan-out metrics reflect real play.

## Prerequisites

Local stack with the AoE arena map and combat prep:

```bash
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot
export FINEAGE_LOGIN_LOADTEST_PASSWORD=loadtest-pass-v1
export FINEAGE_ZONE_LOADTEST_COMBAT=1
export FINEAGE_ZONE_MAPS=loadtest_aoe_arena
export FINEAGE_BUILD_CONFIGURATION=Debug
export FINEAGE_STARTING_LEVEL=77
./scripts/run-local-net.sh start
```

Map: `data/spawns/loadtest_aoe_arena.json` — center train pack + four 55-mob aggressive clusters (~275 Gremlins).

## Scenarios

| YAML | Bots | What it stresses |
| --- | ---: | --- |
| `perf-aoe-train.yaml` | 9 | One killer + 8 party observers; pole sweep + Whirlwind/Thunder Storm on ~50 mobs |
| `perf-aoe-multiparty.yaml` | 36 | Four parties (9 each) in mutual AOI; four simultaneous AoE trains |
| `perf-aoe-combined.yaml` | 72 | Same as multiparty + economy hammer (`bots-economy-hammer.yaml`) on background bots (~20% teleports) |

Run:

```bash
dotnet build tools/Fineage.LoadTest -c Release
dotnet tools/Fineage.LoadTest/bin/Release/net10.0/Fineage.LoadTest.dll run \
  --scenario tools/Fineage.LoadTest/scenarios/perf-aoe-train.yaml \
  --report-dir local-logs/loadtest/perf-aoe-train \
  --zone-pid "$(cat local-logs/zone-net.pid)"
```

Or use `./scripts/run-heavy-play-bench.sh` for all three.

## Report fields (before/after each perf PR)

From `load-test-report.md` (peak Prometheus scrape):

- **Tick:** p50 / p95 / p99 / `fineage_tick_max_ms`
- **Stages:** `fineage_tick_stage_ms` p99 — especially `commands`, `aggression`, `random-walk`, movement
- **Outbound:** `fineage_packets_per_second{direction=out}`, `fineage_socket_write_bytes` / `frames` histograms, fanout queue age
- **Client oracle:** attack/skill fan-out latency samples (no shortcut paths)

**SLO (Debug):** p99 tick **&lt; 50 ms** on the three `perf-aoe-*` scenarios (#951). Older town-walk gates remain at 200 ms.

Recorded numbers: [heavy-play-benchmark-results.md](./heavy-play-benchmark-results.md) (VM **2026-11-23**, before #961–#964 on `dev`). Re-run the bench script after the perf stack merges and append the **after** column there and in PR #965.