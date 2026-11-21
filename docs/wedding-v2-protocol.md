# Wedding — V2 packet contract (engage dialog)

Mobius Interlude uses `ConfirmDlg` for engage answers on the GameServer protocol. Fineage Net exposes a minimal replacement until the client implements Mobius parity opcodes.

## Opcodes (`Fineage.Net.Contract.NetOpcode`)

| Direction | Opcode | Name |
|-----------|--------|------|
| Server → client | `0x03CB` | `WeddingEngageAsk` |
| Client → server | `0x03CA` | `WeddingEngageAnswer` |

## Payloads (`NetCodec`)

**`WeddingEngageAsk` (server):**

- `string` requestorName (UTF-8)
- `int32` requestorObjectId

**`WeddingEngageAnswer` (client):**

- `int32` response — `1` accept, `0` decline (Mobius `RequestAnswerCoupleAction`)

## Voiced chat (unchanged Mobius strings)

Parsed in `ZoneWorldChat` when the line starts with `.`:

| Command | Constant |
|---------|----------|
| `.engage` | `WeddingProtocol.Engage` |
| `.divorce` | `WeddingProtocol.Divorce` |
| `.gotolove` | `WeddingProtocol.GoToLove` |

NPC **Andromeda** (`managerNpcId` in `data/wedding.json`) uses standard HTML bypass events `ask`, `accept`, `decline` under script name `Wedding`.

## Configuration

All economics and feature flags live in [`data/wedding.json`](../data/wedding.json) (Mobius `config/Custom/Wedding.ini`).