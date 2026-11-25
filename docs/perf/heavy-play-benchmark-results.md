# Heavy-play benchmark results (#951 / PR #965)

Captured on the Cloud Agent VM **2026-11-23** (`local-logs/loadtest-heavy/20261123T114044Z/`), **Debug**, `FINEAGE_ZONE_LOADTEST_COMBAT=1`, map `loadtest_aoe_arena`, geodata on. Run branch: `cursor/perf-heavy-play-369b` rebased on **`dev` including merged #959**; **#961–#964 not yet on `dev`** at capture time — treat this column as **before** the think-bucket / wake-budget stack lands. Re-run `./scripts/run-heavy-play-bench.sh` after those PRs merge for the **after** column.

Simulation “stall” ~4.8s in reports is the ramp window (tick counter not in steady state); **stall count = 0**, no watchdog hang.

## Tick + outbound (steady-state scrapes)

| Scenario | Bots online | tick p50 | tick p99 | tick true max | commands stage p99 | out pps (zone) | out pkt/tick (zone ÷ 20) | bytes/s per online player | bytes/tick per online player |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `perf-aoe-train` | 9 | 0.84 ms | 42.2 ms | 40.3 ms | 39.4 ms | 33.8 | 1.69 | 14.1 KB/s | 705 B |
| `perf-aoe-multiparty` | 36 | 0.89 ms | 33.0 ms | 37.5 ms | 41.5 ms | 405 | 20.2 | 45.9 KB/s | 2.29 KB |
| `perf-aoe-combined` | 72 | 3.39 ms | 144 ms | 243 ms | 160 ms | 2590 | 130 | 624 KB/s | 31.2 KB |

**#951 gate (Debug):** p99 tick **&lt; 50 ms** — pass train + multiparty; **fail combined** until perf stack + economy tuning land.

## Top tick-stage p99 (ms)

| Scenario | 1 | 2 | 3 | 4 | 5 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `perf-aoe-train` | commands 39.4 | flush_dirty_inventory 34.9 | commands.apply 34.4 | persist.snapshot 19.1 | CommitEconomy 19.1 |
| `perf-aoe-multiparty` | commands 41.5 | commands.apply 38.6 | CommitEconomy 16.5 | persist.snapshot 16.4 | clan.hall-auctions 9.6 |
| `perf-aoe-combined` | commands 160 | commands.apply 160 | auto-attacks 35.9 | CommitEconomy 21.5 | persist.snapshot 19.8 |

## Runner / oracle notes

| Scenario | Runner | Primary fail reasons |
| --- | --- | --- |
| `perf-aoe-train` | FAIL | `skillSampleCount=0` (no UseSkill→MagicSkillUse samples) |
| `perf-aoe-multiparty` | FAIL | same skill oracle |
| `perf-aoe-combined` | FAIL | economy oracle (241 issues) + tick gate |

Skill-latency and economy columns are orthogonal to tick/fan-out SLO; fix or relax oracles after the perf stack is green.

## After column (pending)

When #961–#964 are on `dev`, run:

```bash
./scripts/run-heavy-play-bench.sh
```

Paste the new scrape row into this file and into PR #965 — same columns, date + `dev` SHA.