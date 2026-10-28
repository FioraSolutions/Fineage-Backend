# Fineage-Backend

Fineage C# Login+Zone game server (V2 MessagePack primary, Interlude bridge).

## This drop

`src/Fineage.Protocol` only. It builds. It does not listen.

| Path | What |
| --- | --- |
| `V2/` | MessagePack contract. Opcodes 1–4 and 1001–1004 match Unity `ZoneV2MsgType` and `tools/zone-server`. Higher ids are unused extensions. |
| `Net/` | Pipeline TCP accept, bounded outbound queue, V2 and Interlude length-prefix readers, per-connection rate limit. |
| `Telemetry/` | `Fineage.Server` meter and activity source. |
| `Interlude/BlowfishEngine.cs` | Blowfish primitive for an Interlude bridge. No login session, no game session, no `NpcInfo`. |

```bash
dotnet build src/Fineage.Protocol/Fineage.Protocol.csproj
```

Not in this commit: Login host, Zone host, accounts, Human create, Talking Island enter, move, Gremlin spawn.
