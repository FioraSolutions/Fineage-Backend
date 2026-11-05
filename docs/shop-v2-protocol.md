# Zone V2 shops

Buy from a merchant buylist, sell back at half the reference price, and exchange through a multisell. The NPC window is still `BypassRequest` (95) from `docs/npc-html-v2-protocol.md`. This page is the trade that follows `Buy`, `Sell`, `Multisell`, `exc_multisell`, and `Exchange`.

Opcodes already taken include NPC HTML **95** / **1094**. Olympiad is reserved at **60–64** / **1070–1073** / system messages **160–179**. Teleport, warehouse, and newbie buffs are reserved at **105–109** / **1105–1109** / **190–209**. This block is the next free shop range: requests **96–99**, replies **1095–1098**, system messages **180–189**. **1099** stays `Error`.

MessagePack keys are a fixarray (`0..n-1`). Extra keys may be appended later. Do not renumber these.

## Choice

The bypass only names the window. The bag changes on a later request, so a missed click does not buy anything. `InventoryUpdate` (1015) stays the bag diff. A new `BuyList` / `SellList` / `MultiSellList` is the window, and `ShopBalance` is the adena, weight, and slot line the client used to read from `StatusUpdate`.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 96 | `RequestBuyItem` | `RequestBuyItemMsg` |
| C → S | 97 | `RequestSellItem` | `RequestSellItemMsg` |
| C → S | 98 | `RequestMultiSellChoose` | `RequestMultiSellChooseMsg` |
| C → S | 99 | `RequestPreviewItem` | `RequestPreviewItemMsg` (wear, stubbed) |
| S → C | 1095 | `BuyList` | `BuyListMsg` |
| S → C | 1096 | `SellList` | `SellListMsg` |
| S → C | 1097 | `MultiSellList` | `MultiSellListMsg` |
| S → C | 1098 | `ShopBalance` | `ShopBalanceMsg` |
| S → C | 1015 | `InventoryUpdate` | existing rows |
| S → C | 1040 | `SystemMessage` | ids 180–189 |

The player must already be in the world, alive, and within **150** of the same non-attackable NPC the bypass used. That distance is `NpcHtmlProtocol.InteractionDistance`.

## Bypass

| Command | Window |
|---------|--------|
| `Buy <listId>` | `BuyList` when `data/buylists.json` names this NPC. Otherwise HTML `I have no such list.` |
| `Sell` | `SellList` for a `Merchant`, a `Fisherman`, or any NPC named by a buylist |
| `Multisell <listId>` | Full `MultiSellList`. Enchant is not copied |
| `exc_multisell <listId>` | Only rows whose first ingredient is an unequipped bag item. Enchant is copied when the list has `maintainEnchantment` |
| `Exchange <listId>` | Same as `exc_multisell`. CT 0 has no `Exchange` class; the HTML uses `exc_multisell`, and this command is the same window |
| `Wear <listId>` | HTML `Wear is not implemented yet.` |

A list id with leading zeros (`003`) is the integer 3. Opening a window stores it on the player. The next trade packet must use that NPC and that list.

## `BuyList` (1095)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | listId | int |
| 2 | baseTaxPercent | int |
| 3 | castleTaxPercent | int |
| 4 | items | array |

Each item:

| Key | Field | Type |
|-----|--------|------|
| 0 | itemId | int |
| 1 | price | int |
| 2 | count | int |

`price` is the unit price the server will charge, tax included. `count` below 0 means the row is not limited. Otherwise it is the stock left. `castleTaxPercent` is 0 when that castle has no owner.

`ShopBalance` (1098) follows the list.

## `RequestBuyItem` (96)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | listId | int |
| 2 | items | array of `{0 itemId, 1 count}` |

The list must be the one just opened, and every item id must be on it. A non-stackable count above 1 is rejected. Count per line is at most **10000**. Lines per packet are at most **250**.

Unit price:

```
base = product price
if itemId is 3960..4026: base = base * RateSiegeGuardsPrice   # default 1
unit = (int)(base * (1 + baseTaxPercent/100 + castleTaxPercent/100))
```

Truncation is toward zero. A unit price of 0 is refused (`ONLY_GM_ITEMS_FREE`). The product price in JSON is the Mobius price: the item reference price when the buylist omits `price`, or the explicit price. When that explicit price is below `reference / 2` and the list names an NPC, the stored price is `reference / 2` (`CorrectPrices`).

The whole order is refused, with no bag change, when adena, weight, slots, stock, or the adena cap fails. Success, in order:

1. `InventoryUpdate` (adena, then each new or stacked item)
2. `SystemMessage` 183
3. `BuyList` with the new stock
4. `ShopBalance`

Adena cap is **2_000_000_000** (`MaxAdena`). Weight limit is `floor(CON bonus * 69000)` from the class template. Slots are **80**, or **100** for race 4 (dwarf). Every bag row counts, including paperdoll. A stackable item the player already holds costs no new slot. Each line of an item they do not hold costs one slot, which is the Mobius buy check.

Limited stock (`count` and `restockMinutes` on the product) decreases on a successful buy and is full again `restockMinutes` after that buy. The timer starts at the purchase, not at server boot.

## `SellList` (1096)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | items | array |

Each item is an unequipped piece with sell price at least 1:

| Key | Field | Type |
|-----|--------|------|
| 0 | objectId | int |
| 1 | itemId | int |
| 2 | count | int |
| 3 | price | int |
| 4 | enchantLevel | int |

`price` is `referencePrice / 2`. Adena, ancient adena, and templates marked `sellable: false` are omitted. Equipped pieces are omitted.

## `RequestSellItem` (97)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | items | array of `{0 objectId, 1 count}` |

`Sell` must be open on that NPC. A line that is missing, worn, or not sellable is skipped. If every line is skipped, the reply is system message 187 and the bag is unchanged. If the adena after the sale would pass **2_000_000_000**, nothing is sold and the reply is 185. Otherwise the sold rows change, adena is added, then 183, a fresh `SellList`, and `ShopBalance`.

There is no refund warehouse. Sold items are destroyed.

## `MultiSellList` (1097)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | listId | int |
| 2 | maintainEnchantment | bool |
| 3 | inventoryOnly | bool |
| 4 | castleTaxPercent | int |
| 5 | entries | array |

Each entry:

| Key | Field | Type |
|-----|--------|------|
| 0 | entryId | int |
| 1 | ingredients | array |
| 2 | products | array |

Each part is `{0 itemId, 1 count, 2 enchantLevel}`.

`entryId` is `templateEntryId * 100000`, plus the enchant when this row keeps enchant. The template entry id is 1-based in file order.

`exc_multisell` sets `inventoryOnly`. The server keeps enchant only when that flag is set and the list has `maintainEnchantment`. One unequipped piece of the first ingredient is chosen, the first distinct `(itemId, enchant)` in bag order. Gear ingredients and products on that row use that enchant. A normal `Multisell` never copies enchant.

Castle tax is added only to adena ingredients marked `tax`, and only when the list has `applyTaxes`, the NPC's town has a castle, and that castle has an owner. The added adena is `round(count * taxPercent/100)` with halves away from zero, then folded into the normal adena ingredient. Town base tax is not applied to multisell. Ancient adena (5575) is an ordinary ingredient.

## `RequestMultiSellChoose` (98)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | listId | int |
| 2 | entryId | int |
| 3 | amount | int |

`entryId` is the prepared id from the open list. `amount` is 1..10000. A non-stackable product row rejects amount above 1. Ingredients are taken from unequipped items. Enchant 0 on the prepared part means any enchant, and single pieces are taken from the lowest enchant first. An exact enchant, including +0 on a keep-enchant row, matches only that level. Equipped items do not count.

Not enough of an ingredient is system message 186. The text is `{count} unit(s) of the item {name} is/are required.` Params are the count and the name. Weight and slots use the same limits as buy. Success is `InventoryUpdate`, system message 183, `ShopBalance`, and a fresh `MultiSellList` when the list was inventory-only.

Negative item ids (special multisell products such as fame) are refused with 187.

## `ShopBalance` (1098)

| Key | Field | Type |
|-----|--------|------|
| 0 | adena | int |
| 1 | weight | int |
| 2 | maxWeight | int |
| 3 | slots | int |
| 4 | maxSlots | int |
| 5 | baseTaxPercent | int |
| 6 | castleTaxPercent | int |

## `RequestPreviewItem` (99)

| Key | Field | Type |
|-----|--------|------|
| 0 | npcObjectId | int |
| 1 | listId | int |
| 2 | itemId | int |

The NPC must be in range. The reply is system message 189, `Wear is not available.` The bag does not change. The `Wear` bypass is still the HTML stub.

## System messages

| Id | When | Text |
|----|------|------|
| 180 | Not enough adena | You do not have enough adena. |
| 181 | The purchase does not fit the weight limit | You have exceeded the weight limit. |
| 182 | The purchase does not fit the slot limit | Your inventory is full. |
| 183 | Buy, sell, or multisell succeeded | The transaction is complete. |
| 184 | Count, amount, or line count is out of range | You have exceeded the quantity that can be inputted. |
| 185 | The sale would pass the adena cap | You have exceeded your out-of-pocket adena limit. |
| 186 | Multisell ingredient short | `{count} unit(s) of the item {name} is/are required.` |
| 187 | Unknown, free, equipped, or otherwise unsellable | That item cannot be traded. |
| 188 | Limited stock is short | That item is out of stock. |
| 189 | Wear preview | Wear is not available. |

A refused trade sends one of these and does not send `InventoryUpdate`.

## Errors (1099)

| Code | When |
|------|------|
| `world` | `EnterWorld` has not finished |
| `bad_request` | Body, or the packet does not match the open window |
| `range` | Farther than 150 from the NPC |
| `dead` | The player is dead |
| `quest_npc` | Object id is not an NPC in this zone |
| `npc_attackable` | The NPC is attackable |

## Data

`data/buylists.json` and `data/multisell.json` are every CT 0 list. Enabled-town merchants (Talking Island village, plus the Cedric hall map which shares that town tax) resolve because their item ids are in `data/items.json` with `price` and `weight`. Other lists are loaded too. Buying an id that is not in the item catalog is 187.

Town base tax follows Mobius `MerchantPriceConfig.xml` (Talking Island **15**, castle **1** Gludio, Giran **10** castle **3**, and the other Interlude towns). Castle tax is `Castle.TaxPercent` from the siege board and is **0** while `OwnerClanId` is 0.

## Reference

Rules and numbers were read from L2J Mobius CT 0 Interlude and rewritten here. The XML was not committed.

| Mobius | Used for |
|--------|----------|
| `dist/game/data/buylists/*.xml` | List ids, NPC allow-list, prices, limited `count`, `restock_delay` |
| `dist/game/data/multisell/*.xml` | Entries, `maintainEnchantment`, `applyTaxes`, tax ingredients |
| `dist/game/data/stats/items/*.xml` | Reference price, weight, stackable, sellable, body slot |
| `dist/game/data/MerchantPriceConfig.xml` | Town base tax and castle id |
| `Buy.java`, `Sell.java`, `Multisell.java`, `Wear.java` | Bypass names. `exc_multisell` is inventory-only |
| `RequestBuyItem`, `RequestSellItem`, `MultiSellChoose` | Checks and the price formulas |
| `Product`, `BuyListData`, `PreparedListContainer`, `PreparedEntry` | Stock timer, price fallback, enchant copy, adena tax |
| `PlayerConfig` | `MaxAdena` 2000000000, slots 80/100, weight `69000 * CON bonus`, multisell amount 10000 |
| `GeneralConfig` | `OnlyGMItemsFree`, `CorrectPrices`, `MultisellAmountLimit` |
| `Castle.getTaxRate` via the siege `Castle` row | Owner tax. No owner means 0 |

Deferred: wear preview, the refund warehouse (`AllowRefund`), paying the castle treasury, augmentation copy, and negative multisell ids.
