# Zone Net private store (sell slice)

> Mobius Interlude `PrivateStoreType.SELL` / `SetPrivateStoreListSell` / `RequestPrivateStoreBuy`.
> Issue [#275](https://github.com/FioraSolutions/Fineage-Backend/issues/275). Live opcodes: [`net-protocol.md`](net-protocol.md).

## Scope

| In | Out (follow-up) |
|----|-----------------|
| Open / refresh sell list, sit, title message | Buy-store |
| Buyer list + purchase | Package sell |
| Quit / death / stand / leave close store | Manufacture / craft |
| Weight / slots / MaxAdena checks | Offline traders, NO_STORE zones |

## Opcodes

| Direction | Opcode | Name |
|-----------|--------|------|
| C→S | `0x038D` | `SetPrivateStoreSell` |
| C→S | `0x038E` | `PrivateStoreQuit` (empty) |
| C→S | `0x038F` | `PrivateStoreListRequest` |
| C→S | `0x0390` | `PrivateStoreBuy` |
| S→C | `0x0391` | `PrivateStoreInfo` |
| S→C | `0x0392` | `PrivateStoreListSell` |

Codec: `NetCodec.Encode/Decode*PrivateStore*`. Zone API: `ZoneWorld.TrySetPrivateStoreSellAsync` / `TryPrivateStoreBuyAsync` / …

## Limits

- Interaction range **150** (Mobius `INTERACTION_DISTANCE`).
- Sell slots: **3** (other) / **4** (dwarf race).
- Title truncated to **29** chars.
- Adena cap `ShopTrade.MaxAdena` (2e9) on line and seller total.

## Client

Backend is ready; Fineage client UI for manage / store title / buy window is a coordinated follow-up.
