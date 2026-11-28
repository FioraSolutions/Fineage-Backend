# ADR 0003: Agent Runtime talks to Zone through a dedicated Gate, not gRPC and not PacketEnvelope

- Status: Accepted
- Date: 2026-11-28
- Issue: [#998](https://github.com/FioraSolutions/Fineage-Backend/issues/998) (epic [#997](https://github.com/FioraSolutions/Fineage-Backend/issues/997))

## Context

Agent Runtime (a separate host, ADR 0002) must observe the world and submit commands. #997 asked for an audit of the existing command boundary first, and for gRPC only if that audit shows it is needed. The alternative of "just use the existing transport" means the player wire: `PacketEnvelope` on Zone port 9777 (ADR 0001; `src/Fineage.Zone/Program.cs:36, 250–253`).

The write boundary already exists and is complete for gameplay:

- Packet handlers call `ZoneWorld.ExecuteAsync` (`src/Fineage.Zone/World/ZoneWorldCommands.cs:37–38`), which is `_ingress.Submit(session, command, ct)` (`src/Fineage.Zone/World/ZoneCommandIngress.cs:159`).
- Apply is `ZoneCommand.Apply(ZoneWorld, ZoneSession, TickContext)` (`src/Fineage.Zone/World/ZoneCommands.cs:32`), on the simulation thread in `ApplyCommands` (`ZoneWorldCommands.cs:91–123`).
- Reads already go through `ZoneRequest<T>` / `OnSimulationAsync` (`src/Fineage.Zone/World/ZoneRequests.cs:10–34, 43–58`).

Zone has no general-purpose RPC stack. Its HTTP is loopback status and metrics only (`ZoneStatusHost`, `src/Fineage.Protocol/ZoneStatusHost.cs:8–16`). Login's Kestrel surface (`LoginHttpHost`) is the play-landing auth contract, not a Zone API.

Load-test bots (`tools/Fineage.LoadTest`, `FineageBotClient.ConnectAndEnterAsync`) are real `PacketEnvelope` clients. They are not an Agent transport.

The command-seam choice (submit against the live `ZoneSession`, do not fork a second game API) is in [`docs/architecture/agent-runtime-reconnaissance.md`](../architecture/agent-runtime-reconnaissance.md) §6.

## Decision

**v1 transport is a dedicated, authenticated, loopback Agent Gate on the Zone process. gRPC is declined for v1. `PacketEnvelope` is not reused as the Agent wire.**

The Gate is a narrow request/response surface (and, later, an optional event stream) that:

1. Authenticates the Agent Runtime host — not with a player handoff token and not with `ZoneAuth`.
2. Resolves the live in-world `ZoneSession` by character `ObjectId` (reconnaissance §6, option B).
3. Submits writes only through `ExecuteAsync` / `ZoneCommandIngress.Submit`, and reads only through `OnSimulationAsync` / `ZoneRequest<T>`.
4. Returns the same ingress / `Apply` rejection (or success after fan-out) that a player command would get.

gRPC is not needed for this slice: both hosts are in our control, the first contract is request/response, and introducing a gRPC stack would be a new dependency on a process that today has no RPC framework. We will reopen gRPC only if a second implementation language or a measured streaming need appears.

Reusing `PacketEnvelope` is rejected: it would force the Runtime to impersonate a game client (Hello, `ZoneAuth`, enter, mailboxes, rate gates), which is the load-test path the epic forbids, and it would mix Agent traffic with the player opcode namespace (ADR 0001).

The Gate may start as a loopback listener in the same family as `ZoneStatusHost` (a small dedicated HTTP or framed socket on `127.0.0.1`), or as an in-process adapter used only in tests. The public shape is the Agent contract (AI-002), not a second copy of the game command types.

## Consequences

- AI-005 implements the Gate and the `ObjectId → ZoneSession` directory. Until then, Zone's listen set is unchanged.
- Queue depth, max age, and apply-cost caps (`ZoneSimulationSettings.cs:95, 101`) apply to Agent-submitted commands automatically, because they use the same ingress keyed by `Session.ObjectId` (`ZoneCommandIngress.cs:217–221`).
- Fan-out to a connected player stays on `NetPresenceHub`. The Runtime does not attach to `OutboundMailbox`. Observation is a projection (AI-004), not a mirrored client socket.
- Security review of the Gate is in scope for the card that opens it: bind loopback by default, authenticate the Runtime, and keep it closed when the AI layer is disabled (ADR 0002).
