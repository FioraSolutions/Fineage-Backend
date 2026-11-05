# Zone V2 trade wire (minimal)

Interlude reference and Fineage client mapping live in **`docs/trade-java-l2.md`**.

## Opcodes

Trade C→S ids sit after party **18–19 / 26–27** and before admin spoil **30–31**. Confirm/cancel uses **54–55** after death **52–53**. Login **20–25** stay login-only on the zone port.

On **`main`**, S→C **1073–1076** are already used (Olympiad countdown, death, revive, teleport). Trade replies use the free band **1077–1082**. Backend **`dev`** (#166) still maps trade S→C to **1073–1078**; align the Fineage client bridge to the deploy branch you target.

| Direction | Id | Name |
|-----------|-----|------|
| C→S | 28 | `TradeRequest` |
| C→S | 29 | `TradeAnswerRequest` |
| C→S | 54 | `TradeAddItemRequest` |
| C→S | 55 | `TradeDoneRequest` |
| S→C | 1077 | `TradeAsk` |
| S→C | 1078 | `TradeRequestResult` |
| S→C | 1079 | `TradeStart` |
| S→C | 1080 | `TradeOfferUpdate` |
| S→C | 1081 | `TradeConfirmState` |
| S→C | 1082 | `TradeFinished` |

MessagePack shapes: `Fineage.Protocol.Messages` (`Trade*Msg` types). Error codes: `Fineage.Protocol.TradeProtocol`.

## Scope

Two-player trade per zone instance; **150** unit range; max **8** offer rows per side; equipped items rejected; both **TradeDoneRequest(1)** before swap. Handlers in `ZoneWorldTrade` and `ZoneServer`. No weight, private store, or bulk **TradeUpdate** yet.

## Client

Fineage **`dev`** (#509) may still list **1073–1078** for trade RX until a **`main`**-aligned client lands. Inject cascade is paused; update client opcodes when wiring deploy builds.
