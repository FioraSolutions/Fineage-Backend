# ADR 0002: Agent Runtime is a separate host

- Status: Accepted
- Date: 2026-11-28
- Issue: [#998](https://github.com/FioraSolutions/Fineage-Backend/issues/998) (epic [#997](https://github.com/FioraSolutions/Fineage-Backend/issues/997))

## Context

Epic #997 needs an Agent Runtime that plans with a model, exposes tools, and submits work into Zone. Two shapes were on the table: an in-process module inside `Fineage.Zone`, or a separate host. The current Zone process is a manual composition root (`src/Fineage.Zone/Program.cs`) with no generic host and no DI. Its only client transport is `PacketEnvelope` TCP on port 9777 (`Program.cs:36, 250–253`). The only HTTP it serves is loopback status and metrics (`ZoneStatusHost` in `src/Fineage.Protocol/ZoneStatusHost.cs:8–16, 34–45`; `FineageMetricsHost` started at `Program.cs:156`). Kestrel exists in Login (`LoginHttpHost`, `src/Fineage.Login/LoginHttpHost.cs:27–28`), not in Zone.

The simulation tick is already under hard rules: `WorldAffinity` (`src/Fineage.Zone/World/WorldAffinity.cs:25–35`) allows world mutation only on the step; `ApplyCommands` (`src/Fineage.Zone/World/ZoneWorldCommands.cs:91–179`) runs `ZoneCommand.Apply` on that thread; `ILedgerJournal.Append` must not block on I/O (`src/Fineage.Game/Economy/LedgerJournal.cs:9–17`). #997 forbids an LLM, provider HTTP, or a wait on Agent Runtime inside that tick, and requires that ordinary play continue when the AI layer is disabled.

The reconnaissance map is [`docs/architecture/agent-runtime-reconnaissance.md`](../architecture/agent-runtime-reconnaissance.md). Transport is ADR 0003. MCP is ADR 0004.

## Decision

**Agent Runtime is a separate host**, not a module of `Fineage.Zone`.

The planner, model calls, tool orchestration, operation registry (AI-006), and AI billing/credits live in that host. Zone remains the simulation and the player `PacketEnvelope` process.

Zone may grow a thin **Agent Gate** (ADR 0003): authenticate the Runtime, resolve a live `ZoneSession`, and call the existing `ExecuteAsync` / `OnSimulationAsync` ingress. That Gate is not the Runtime, does no planning, and does not call a model.

The Runtime is optional. If the host is not started, or the Gate is not bound, ordinary login and play are unchanged. No core migration is required to turn the AI layer off.

An in-process Runtime inside Zone is **rejected** as the home of planning, billing, or model I/O: it would put a new long-running, I/O-heavy service in the same process as the tick, couple AI billing to the Zone process that owns the auditable economy ledger, and make "AI disabled" a code path inside the simulation host rather than the absence of a process.

## Consequences

- Later cards create the Agent host (and only then a `Fineage.Agent.*` project, which #997 forbids inventing before this reconnaissance). Zone's composition root does not gain a generic host in order to host the Runtime.
- Process isolation is the first line of defence for "no LLM in the tick". The Gate still must not `await` the Runtime on the simulation thread.
- AI billing/credits belong to the Agent host's own store, isolated from `ILedgerJournal` and `IAccountStore.CommitEconomy` (`src/Fineage.Accounts/IAccountStore.cs:82–86`). See the reconnaissance document, §5.
- Identity (AI-003) is issued and stored by the Runtime; Zone only needs the bind result that lets the Gate resolve `ZoneSession`.
- A second network hop (Runtime → Gate → ingress) is accepted in exchange for isolation. ADR 0003 keeps that hop on loopback for v1.
