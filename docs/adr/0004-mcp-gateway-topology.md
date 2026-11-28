# ADR 0004: MCP gateway is a separate optional host

- Status: Accepted
- Date: 2026-11-28
- Issue: [#998](https://github.com/FioraSolutions/Fineage-Backend/issues/998) (epic [#997](https://github.com/FioraSolutions/Fineage-Backend/issues/997))

## Context

#997 requires one authoritative Agent API. MCP and native function calling are two ways to describe and call the same tools; MCP is not the core of the Runtime. The remaining fork is whether an MCP gateway should live as a module of Zone, a module of the Agent Runtime host, or its own process.

Zone is the simulation and the player `PacketEnvelope` process (ADR 0001, ADR 0002). It must not take a dependency on an MCP SDK, and its tick must not know that a tool was invoked via MCP, native function calling, or any other front-end (`docs/architecture/agent-runtime-reconnaissance.md`). Agent Runtime, in a separate host (ADR 0002), owns planning, the operation registry, and AI billing.

## Decision

**The MCP gateway is a separate, optional host.** It is not a Zone module and it is not the Agent Runtime core.

The gateway speaks MCP to external clients and calls the same Agent API the native function-calling path uses (AI-002). It does not submit `ZoneCommand`s itself, does not hold a `ZoneWorld` reference, and does not talk to the Agent Gate as a second writer: it goes through Agent Runtime, which is the only process that plans and the only process that calls the Gate.

Native function calling stays inside the Runtime (or a library it owns). If the MCP host is not started, the Runtime and ordinary play are unaffected.

A Zone-hosted MCP module is **rejected**: it would add an unrelated protocol stack to the simulation process and blur the "one write path" rule. Folding the gateway into the Runtime process is **rejected as the default** so that MCP remains optional and a compromise of the MCP surface does not share a process with billing or with the planner's credentials. A future operator who deliberately collocates the two is not this record's default.

## Consequences

- No MCP types or packages are added to `Fineage.Zone` or to the player net contract.
- Tool names and schemas have a single source (AI-002). The gateway is an adapter, not a second tool list.
- Cards that implement MCP come after the Runtime and the Agent API exist; this record only locks topology.
- Zone still sees only ingress traffic from the Agent Gate (ADR 0003), regardless of how a user or an IDE invoked the tool.
