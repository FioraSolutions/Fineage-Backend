# Zone Net private store (sell + buy + package)

> Mobius Interlude `PrivateStoreType.SELL` / `BUY` / `PACKAGE_SELL`.
> Issues [#275](https://github.com/FioraSolutions/Fineage-Backend/issues/275) (sell), [#277](https://github.com/FioraSolutions/Fineage-Backend/issues/277) (buy), [#279](https://github.com/FioraSolutions/Fineage-Backend/issues/279) (package).
> Live opcodes: [`net-protocol.md`](net-protocol.md).

## Scope

| In | Out (follow-up) |
|----|-----------------|
| Open / refresh sell, package-sell, or buy list | Manufacture / craft |
| Visitor list + buy-from-sell / sell-into-buy | Offline traders, NO_STORE zones |
| Package buy must request every listed row | Client manage UI |
| Quit / death / stand / leave close store | |
| Weight / slots / MaxAdena checks | |

## Opcodes

| Direction | Opcode | Name |
|-----------|--------|------|
| C→S | `0x038D` | `SetPrivateStoreSell` (`Packaged` 0/1) |
| C→S | `0x038E` | `PrivateStoreQuit` (empty) |
| C→S | `0x038F` | `PrivateStoreListRequest` |
| C→S | `0x0390` | `PrivateStoreBuy` (buy from sell store) |
| S→C | `0x0391` | `PrivateStoreInfo` |
| S→C | `0x0392` | `PrivateStoreListSell` (`Packaged` flag) |
| C→S | `0x0393` | `SetPrivateStoreBuy` |
| C→S | `0x0394` | `PrivateStoreSellTo` (sell into buy store) |
| S→C | `0x0395` | `PrivateStoreListBuy` |

## Limits

- Interaction range **150**.
- Sell slots: **3** / **4** (dwarf). Buy slots: **4** / **5** (dwarf).
- Opening a buy store requires bag adena ≥ total wish cost.
- Title truncated to **29** chars.
- Adena cap `ShopTrade.MaxAdena` (2e9).
