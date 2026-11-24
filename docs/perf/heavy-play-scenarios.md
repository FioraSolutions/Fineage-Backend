# Heavy-play load scenarios (#951)

These scenarios replace idle random-walk bots with structured **Warlord pole + AoE** combat so tick and fan-out metrics reflect real play.

## Prerequisites

Local stack with the AoE arena map and combat prep:

```bash
export FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX=loadbot
export FINEAGE_LOGIN_LOADTEST_PASSWORD=loadtest-pass-v1
export FINEAGE_ZONE_LOADTEST_COMBAT=1          # load-test accounts enter as level 77 Warlord (class 3) with a Great Spear
export FINEAGE_ZONE_LOADTEST_TELEPORT=1        # load-test accounts may send TeleportToLocation (economy hub / manor)
export FINEAGE_ZONE_EXTRA_MAPS=loadtest_aoe_arena  # full world + the arena (the arena map is disabled in the datapack)
export FINEAGE_BUILD_CONFIGURATION=Debug
export FINEAGE_STARTING_LEVEL=77
export FINEAGE_STARTING_ADENA=5000000          # level 77 pays gatekeeper fees; economy flows buy from the grocer
./scripts/run-local-net.sh start
```

Map: `data/spawns/loadtest_aoe_arena.json` — center train pack + four 55-mob aggressive clusters (~275 Gremlins) next to
the Talking Island fighter start point, where new Human Fighters enter. It is `enabled: false` so hosted worlds never
spawn it; `FINEAGE_ZONE_MAPS=loadtest_aoe_arena` loads only the arena (train / multiparty), `FINEAGE_ZONE_EXTRA_MAPS`
adds it on top of the full world (combined, which needs the hub NPCs).

Every fighter (every slot of every party, and the train killer) plays like a player farming a pack: it picks the nearest
live NPC, sends one AttackRequest (the server approaches and keeps swinging until the target dies), casts the
`skillRotation` (Whirlwind 36, Thunder Storm 48) every third tick, and picks a new target only after an EntityDie or
despawn. The report prints attack/skill samples, timeouts and kills (EntityDie frames naming a bot as killer).

Reject codes in the economy oracle fall into three classes: explained economy refusals, **expected domain rejects**
(contention: `party_busy`, `party_leader`, `trade_busy`, `trade_range`, `pickup`, `action_blocked`, `target`, `attack`;
counted per code, never a failure), and anything else (for example `skill_unknown`, `unknown_opcode`), which fails the run.

## Scenarios

| YAML | Bots | What it stresses |
| --- | ---: | --- |
| `perf-aoe-train.yaml` | 9 | One killer + 8 observers; pole sweep + Whirlwind/Thunder Storm on the center pack |
| `perf-aoe-multiparty.yaml` | 36 | Four parties (9 fighters each) in mutual AOI, each on its own 55-mob cluster |
| `perf-aoe-combined.yaml` | 72 | Same 36 fighters + 36 economy-hammer bots (`bots-economy-hammer.yaml` mix); 20% of those run gatekeeper trips |

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