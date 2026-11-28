# AI-005 Agent command path tick measurements

Evidence for #1002 (Agent command adapter through the existing ingress, `CommandSource=AI`). Measured locally on 2026-11-28 in **Debug**, one blocking `dotnet test` call, Europe/Minsk (UTC+3). Eight WorldBench characters submitted `move` through `AgentCommandAdapter` (live `ZoneSession`, `CommandSource.Ai`, ~5 Hz each) for 12 s after a 2 s warmup.

Acceptance gates in `docs/perf/acceptance-gates.md` name tick work p99 ≤ 50 ms (and a 200 ms step budget). This run stays under both.

| Run | AI-driven characters | Window | Ticks | p50 (ms) | p99 (ms) | max (ms) |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| Debug soak, moves through the Agent Gate | 8 | 12 s | 34 | 0.31 | 35.43 | 35.43 |

A tighter submit loop (every 20 ms) can keep `QueuedDepth > 0` and spend the inter-tick wait in `AdmitQueuedCommands` only; the measured rate is paced so full steps still run. Ingress already applies a per-`ObjectId` cost budget and deficit round-robin, so AI and player subjects share fairness. Repro (`Category=perf`, skipped on the ordinary PR fast gate):

```
dotnet test tests/Fineage.Zone.Tests/Fineage.Zone.Tests.csproj --configuration Debug \
  --filter 'FullyQualifiedName~Debug_tick_p99_with_many_ai_driven_characters'
```
