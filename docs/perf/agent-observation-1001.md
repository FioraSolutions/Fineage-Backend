# AI-004 observation payload and tick measurements

Evidence for #1001 (Zone agent read adapter / observation projection). Measured locally on 2026-11-28 in **Debug**, one blocking `dotnet test` call, Europe/Minsk (UTC+3). Scenes are synthetic clusters on the real Zone knownlist / `PublishObservationAsync` path (RangeAoi, no full-world geodata). Payload cap is `AgentObservation.MaxPayloadBytes` = 32,768; entity cap is 128.

## Payload bytes (official `AgentContractJson` UTF-8)

| Scene | Visible (knownlist) | First page entities | HasMore | Payload bytes |
| --- | ---: | ---: | --- | ---: |
| Town-like (80 NPCs around the observer) | 80 | 80 | no | 23,510 |
| Raid-like (200 NPCs in one AOI) | 200 | 114 | yes | 32,528 |

The raid first page is smaller than 128 because 128 typical NPC rows plus self/details would exceed 32,768 bytes. The projector shrinks the page and sets `HasMore`; it does not silently truncate. The union of pages equals the knownlist.

## Tick p99 with N agents observing (Debug)

Capture runs once on the sim thread via the existing `ZoneRequest` / `OnSimulationAsync` path and publishes an immutable snapshot. Observers only call `TryGetPublishedObservation` and project off-thread; they do not lock the tick.

| Run | Observers | Window | Ticks | p50 (ms) | p99 (ms) | max (ms) |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| Baseline (no observers) | 0 | ~12 ticks after warmup | 12 | — | 44.69 | — |
| Off-thread observe (same world) | 32 | ~12 ticks | 12 | — | 1.07 | — |
| Debug soak | 64 | 12 s | 119 | 0.38 | 23.23 | 23.84 |

All p99 values are under the 200 ms step budget. Observing published snapshots did not raise tick p99 versus the idle baseline (the observe window was quieter than the short warmup window).

Repro (fast payload + lock tests are not `Category=perf`; the 64-observer soak is):

```
dotnet test tests/Fineage.Zone.Tests/Fineage.Zone.Tests.csproj --configuration Debug \
  --filter 'FullyQualifiedName~AgentObservationProjectionTests.Town_and_raid|FullyQualifiedName~AgentObservationProjectionTests.Observing_published|FullyQualifiedName~AgentObservationProjectionTests.Debug_observe'
```
