# ADR 0001: Custom PacketEnvelope wire (not retail Interlude framing)

- Status: Accepted
- Date: 2026-11-28
- Issue: [#994](https://github.com/FioraSolutions/Fineage-Backend/issues/994)

## Context

Fineage gameplay and content are oriented to Mobius CT_0 Interlude *message semantics* (fields and handlers). The retail Interlude client/server conversation uses a different on-wire framing and a different opcode namespace. Fineage Login and Zone already speak only the Net `PacketEnvelope` protocol defined in `src/Fineage.Net.Contract`. This record states that decision, what "parity" means for the wire, and which claims the tests in this repository enforce.

## Decision

Fineage Login (port 9106) and Zone (port 9777) speak **only** the custom `PacketEnvelope` protocol, currently **major 1 / minor 0**. They do not accept or emit retail Interlude frames, and they do not use retail `0x` opcode ids as the Fineage wire identifiers.

Retail Interlude packets are a *mapping source*: each Mobius `ClientPackets` / `ExClientPackets` / `ServerPackets` entry is recorded against zero or more `NetOpcode` names in `docs/wire-coverage.md` and `docs/data/retail-net-opcode-map.json`. That mapping is how we talk about coverage. It is not a promise that the bytes on the socket look like a retail client.

## What "parity" means on this wire

- **In scope for the wire:** message semantics. A mapped Fineage opcode carries the same *fields* the corresponding Mobius packet is there to convey (entity ids, positions, counts, and so on), encoded with `NetCodec` inside a `PacketEnvelope` payload. Round-trip tests prove `Encode` / `Decode` of those Fineage fields, using the Mobius class as the field list, not as a byte template.
- **Out of scope for the wire:** retail frame layout, retail 1-byte / extended `0xD0` / `0xFE` ids on the socket, and any other retail handshake. A `yes` in `docs/wire-coverage.md` means a handler is wired, not that behaviour matches Mobius. Behavioural evidence lives only in `docs/mobius-parity-matrix.md`.

## Grounding: Fineage `PacketEnvelope` (this repo)

All citations below are the code as of this record. The tests listed in Consequences fail if these limits or agreement rules change unnoticed.

### Framing

`NetFraming` (`src/Fineage.Net.Contract/Envelope/NetFraming.cs`) encodes one frame as:

1. `uint32` little-endian `totalLength`, equal to **header + payload only** (the 4-byte prefix is *not* included in `totalLength`);
2. a 14-byte `PacketEnvelope` header;
3. the payload bytes (`NetCodec` output).

`PacketEnvelope` (`src/Fineage.Net.Contract/Envelope/PacketEnvelope.cs`) fixes:

| Constant | Value | Meaning |
|---|---:|---|
| `ProtocolMajor` | 1 | Current wire major. Also the default `Major` on a new envelope. |
| `ProtocolMinor` | 0 | Advertised minor. Also the default `Minor`. |
| `HeaderSize` | 14 | `major` (u8) + `minor` (u8) + `opcode` (u16 LE) + `flags` (u16 LE) + `requestSequence` (u32 LE) + `payloadLen` (u32 LE) |
| `MaxPayloadBytes` | 60_000 | Hard payload cap |
| `MaxFrameBytes` | 60_014 | `HeaderSize + MaxPayloadBytes`; hard cap on `totalLength` |

`PacketFlags` defines `Reliable` (bit 0) and `Compressed` (bit 1). The host sets `Reliable` on envelopes it builds (`NetConnection.TryEnvelope`). `Compressed` is reserved; the encoder and decoder do not apply a compression pass today.

Inbound validation (`NetFraming.TryReadFrame` / `TryDecode` / `ReadAsync`, and `NetInboundPipe` which throws `InvalidOperationException` with the same code):

| Condition | Error |
|---|---|
| `totalLength < HeaderSize` or `totalLength > MaxFrameBytes` | `bad_frame_length` |
| Header `payloadLen > MaxPayloadBytes` | `oversized` |
| Body length ≠ `HeaderSize + payloadLen` | `length_mismatch` |
| Body shorter than `HeaderSize` | `short_header` |
| Prefix or body still incomplete | no error; wait for more bytes |
| `Encode` / `EncodeInto` with payload larger than `MaxPayloadBytes` | throws `InvalidOperationException` (`payload too large`) |

Framing does **not** accept or reject protocol versions. A well-formed frame with `Major != 1` still decodes. Version agreement is enforced after decode (below). Tests in `EnvelopeFramingRejectionTests` lock these codes, including oversize, length mismatch, and random-input (fuzz) cases that must not hang and must only surface a defined error or "need more data".

### Opcodes

`NetOpcode` is a namespaced `ushort` table (high byte ≈ domain, low byte ≈ message): session `0x01xx`, lobby `0x02xx`, world `0x03xx`, and so on. `Hello` is `0x0101`, `HelloAck` is `0x0102`, `Reject` is `0x01FF`, `Auth` is `0x0201`. Extended Mobius packets that Fineage folded in use `NetOpcodeMobius` (`ExServerBase = 0xFE00`, `ExClientBase = 0xD000`) so a Fineage opcode can sit at `0xFE12` or `0xD005` without being a retail frame.

A handful of vehicle client ids live under `NetOpcode.ClientPackets` because the retail client and server numbers collide (`0x5C` / `0x5D`). Those names are aliases of the Mobius *client* ids, not a second Fineage wire.

### How client and server agree (versioning)

1. Every frame header carries `Major` / `Minor`. The host inbox (`NetConnection.AcceptFrameAsync`) delivers a frame only when `envelope.Major == PacketEnvelope.ProtocolMajor`. A mismatched major is a **protocol rejection**: the connection is closed and the frame is not written to the inbox (`FailProtocol`). `Minor` is carried and advertised; it is not a rejection criterion today (`ProtocolMinor` is 0, and no minor negotiation is implemented).
2. The first application message is `Hello` / `HelloAck`. Both payloads carry `Major`, `Minor`, and `FeatureFlags` (`HelloMsg` / `HelloAckMsg` in `SessionMessages.cs`). `ProtocolCapabilities` defines `FeatureSession`, `FeatureLobby`, and `FeatureWorldPresence`. Login grants Session+Lobby; Zone grants Session+WorldPresence. The granted set is the intersection with the client's flags, or the full grant when the client sends `0`.
3. If a `Hello` payload has `Major != ProtocolMajor`, Login and Zone send `Reject` with code `protocol_mismatch` and leave `HelloCompleted` false (`NetProtocol.TryAcceptHello`). Later opcodes still require a completed Hello (`RequireHello`).
4. The server always answers a successful Hello with *its* `ProtocolMajor` / `ProtocolMinor` in `HelloAck`. There is no other version exchange on this wire.

### Auth and zone entry (this wire)

After Hello, Login authenticates with `Auth` (`0x0201`): the payload is the account name, the password, a register-if-missing flag, and an optional language tag (`NetCodec.EncodeAuth`). Success returns `AuthResult`. Selecting a character returns `Handoff` (`0x0208`) with a short-lived handoff token plus the zone host and port. Zone entry is `Hello` then `ZoneAuth` (`0x0301`) carrying that token; a session that is already bound is rejected with `already_authenticated`.

This repository does not speak the leftover Interlude login listener that still exists in the Unity client (`tools/login-stub` in FioraSolutions/Fineage). That is a different wire and a different port pair. Fineage Login+Zone only accept `PacketEnvelope` on 9106 / 9777.

### Contract agreement between the two repositories

`src/Fineage.Net.Contract` is the source of truth. The Unity client keeps in-assets copies under `l2-unity/Assets/Scripts/Networking/Net/Contract` (Unity cannot symlink out of `Assets/`). The client script `scripts/sync-net-contract.py` compares the two trees after lowering backend sources to Unity C# 9. Client CI already runs that check against backend `dev`.

Backend CI runs `scripts/check-net-contract-sync.py`, which invokes the client's script and compares the drift set to `docs/data/net-contract-client-gap.txt`. That file is the explicit pending/incompatibility list. The job fails when the live comparison disagrees with the file (the gap grew, shrank, or a previously matching file changed). An empty gap file is the accepted end state. A PR that changes the contract must either stay within the recorded gap or update the file in the same PR (and a follow-up client sync should shrink it). The body tag `Contract-Sync: pending` documents the same fact in the pull request; the gap file is what CI reads.

## Grounding: retail Interlude framing (Mobius CT_0)

The retail contrast is taken from `L2J_Mobius_CT_0_Interlude` (`org.l2jmobius.commons.network` and `gameserver.network`):

- `ConnectionConfig.HEADER_SIZE` is **2**. `WritablePacket.writeHeader` writes a `uint16` little-endian **total packet length that includes those 2 bytes**.
- The next byte is the primary opcode. Extended groups use primary `0xD0` (client) or `0xFE` (server) plus a sub-opcode; Mobius lists them in `ClientPackets` / `ExClientPackets` / `ServerPackets`.
- `ReadHandler.MAX_PACKET_SIZE` is **65533** (0xFFFF total minus the 2-byte header). A declared size above that disconnects the client.
- Version agreement is a `ProtocolVersion` client packet carrying a 32-bit integer, checked against `ServerConfig.PROTOCOL_LIST`. That is not Hello/HelloAck and is not `PacketEnvelope` major/minor.

Fineage does not decode or emit that frame. `NetOpcodeMobius` only *folds* the extended id into the Fineage `ushort` namespace for mapping and for opcodes that reused those numbers.

## Options considered

1. **Speak the retail Interlude wire from Login+Zone** so a stock client could connect. Rejected: the live codec, host, tests, and Unity Net client are already `PacketEnvelope`. Switching would throw away that contract and couple the server to retail frame layout.
2. **Dual-stack** (PacketEnvelope and retail on different ports). Rejected for now: two contracts, two agreement rules, and a permanent risk of mixing "packet implemented" with "bytes match retail". The leftover Interlude stub stays in the client repo and is not served from here.
3. **PacketEnvelope only, with a documented semantic mapping to Mobius packets** (this decision).

## When to revisit

Revisit this ADR if any of the following become a real requirement:

- Login or Zone must accept a stock Interlude client (retail frames and retail 0x ids on the socket);
- `ProtocolMajor` must bump (breaking header or payload agreement) or `ProtocolMinor` must become a rejection criterion;
- The Unity client stops copying `Fineage.Net.Contract` and consumes the backend assembly another way;
- Payload limits (`MaxPayloadBytes` / `MaxFrameBytes`) have to change.

A major bump is a new agreement: old clients must be rejected with `protocol_mismatch` (already the rule for a wrong Hello major) and this record must be updated.

## Consequences

- `docs/data/retail-net-opcode-map.json` is generated from `docs/wire-coverage.md`. `tools/check_retail_opcode_map.py` and `RetailOpcodeMapTests` fail when the two disagree, when a Fineage name is not in `NetOpcode`, or when the markdown summary counts do not match a recount of the tables.
- `docs/wire-coverage.md` keeps **wire / mapping / handler wiring** separate from **gameplay parity**. The latter stays in `docs/mobius-parity-matrix.md`.
- Framing, fuzz, oversize, and version-mismatch tests in `tests/Fineage.Net.Tests` enforce the limits and rejection rules in this record.
- `CodecRoundTripCatalogTests` require every `NetCodec` Encode/Decode pair to round-trip a populated message (Fineage fields, not retail bytes).
- Backend CI (`net-contract-sync.yml`) fails when the Unity client comparison disagrees with `docs/data/net-contract-client-gap.txt`.
