# Zone V2 trade wire (minimal)

Interlude reference and Fineage client mapping live in **`docs/trade-java-l2.md`**.

## Opcodes

Trade C→S ids sit after party **18–19 / 26–27** and before admin spoil **30–31**. Confirm/cancel uses **54–55** after death **52–53**. Login **20–25** stay login-only on the zone port.

| Direction | Id | Name |
|-----------|-----|------|
| C→S | 28 | `TradeRequest` |
| C→S | 29 | `TradeAnswerRequest` |
| C→S | 54 | `TradeAddItemRequest` |
| C→S | 55 | `TradeDoneRequest` |
| S→C | 1073 | `TradeAsk` |
| S→C | 1074 | `TradeRequestResult` |
| S→C | 1075 | `TradeStart` |
| S→C | 1076 | `TradeOfferUpdate` |
| S→C | 1077 | `TradeConfirmState` |
| S→C | 1078 | `TradeFinished` |

MessagePack shapes: `Fineage.Protocol.Messages` (`Trade*Msg` types). Error codes: `Fineage.Protocol.TradeProtocol`.

## Scope

Two-player trade per zone instance; **150** unit range; max **8** offer rows per side; equipped items rejected; both **TradeDoneRequest(1)** before swap. Handlers in `ZoneWorldTrade` and `ZoneServer`. No weight, private store, or bulk **TradeUpdate** yet.

## `dev` integration

Branch **`dev`** carries trade together with party, inventory, and death slices. Client Zone V2 bridge is **Fineage #460** (not in this repo).
