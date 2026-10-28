# Zone enchant wire

A player selects a scroll, puts an item in the window, and the server rolls. Cancel closes the window. This is the server side of client issue #69.

Opcodes already in use elsewhere (do not reuse): equip **15–17**, chat **8 / 1012 / 1040**, party **18–19 / 26–27 / 1027–1029**, spoil **30–31**, clan **32–38 / 1050–1059**, lobby **20–25 / 1020–1026 / 1030–1033**, presence **1005–1007**, combat **1008–1010 / 1017**, inventory **1014–1016**. **1019**, **1034–1039**, and **1041–1049** stay free.

## Opcodes

| Direction | Id | Name | Body |
|-----------|----|------|------|
| C → S | 39 | `EnchantRequest` | `EnchantRequestMsg` |
| C → S | 40 | `EnchantConfirm` | `EnchantConfirmMsg` |
| C → S | 41 | `EnchantCancel` | `EnchantCancelMsg` (empty map) |
| S → C | 1060 | `EnchantResult` | `EnchantResultMsg` |
| S → C | 1015 | `InventoryUpdate` | existing inventory rows, plus key 10 enchant |
| S → C | 1040 | `SystemMessage` | existing chat message; enchant ids start at 10 |
| S → C | 1016 | `UserInfo` | paperdoll slot key 2 is the enchant, when an equipped item changed |
| S → C | 1005 | `CharInfo` | key 9 is the enchant of each paperdoll slot (keys 7–8 are clan id and crest) |

MessagePack keys are a fixarray. Extra keys are appended. Do not renumber them.

### `EnchantRequest` (39)

| Key | Field | Type |
|-----|--------|------|
| 0 | objectId | int |

Object id of the scroll in the bag. Not a template id.

Success is one `SystemMessage` 10, `Select an item to enchant.` Params are the scroll object id and the scroll item id. The window stays open until confirm or cancel.

### `EnchantConfirm` (40)

| Key | Field | Type |
|-----|--------|------|
| 0 | objectId | int |

Object id of the weapon or armor. The scroll is the one selected by the request.

A finished roll, in order, to the player:

1. `EnchantResult` (1060)
2. `InventoryUpdate` (1015)
3. One or more `SystemMessage` (1040)
4. `UserInfo` (1016) when an equipped item's enchant changed, the item left the paperdoll, or another paperdoll slot changed

Each known player then gets `CharInfo` (1005). A bag item that only changes enchant does not send UserInfo or CharInfo.

Inventory rows, in order: the scroll (remove when its count hits 0, otherwise modify), the target when it changed, crystals from a normal failure, then any other paperdoll row whose slot changed. A remove row has count 0 and still carries the enchant the piece had.

### `EnchantCancel` (41)

Empty map. Reply is `EnchantResult` with result 0, then `SystemMessage` 11.

### `EnchantResult` (1060)

| Key | Field | Type |
|-----|--------|------|
| 0 | result | int |
| 1 | enchantLevel | int |
| 2 | objectId | int |
| 3 | crystalItemId | int |
| 4 | crystalCount | int |

| result | Meaning | enchantLevel |
|--------|---------|----------------|
| 0 | Cancel | 0 |
| 1 | Success | Level after the attempt. It does not increase when the base chance was 0. |
| 2 | Crystal scroll failed; the level is kept | Unchanged level |
| 3 | Blessed scroll failed; reset to +0 | 0 |
| 4 | Normal scroll failed; the item is gone | 0 |

`objectId` is the target. On cancel it is 0. `crystalItemId` and `crystalCount` are set only for result 4 when the item was crystallizable.

## System messages

Ids 1–3 belong to chat. Enchant lines:

| Id | When | Params | Text |
|----|------|--------|------|
| 10 | Scroll selected | `[objectId, itemId]` | `Select an item to enchant.` |
| 11 | Cancel | `[]` | `You have cancelled the enchanting process.` |
| 12 | Success from +0 | `[name]` | `Your {name} has been successfully enchanted.` |
| 13 | Success from +1 or higher | `[levelBefore, name]` | `Your +{levelBefore} {name} has been successfully enchanted.` |
| 14 | Crystal failure | `[]` | `Enchant failed. The enchant value of the item will be retained.` |
| 15 | Blessed failure | `[]` | `Blessed enchant failed. The enchant value of the item became 0.` |
| 16 | Equipped item removed, it was enchanted | `[level, name]` | `The equipment +{level} {name} has been removed.` |
| 17 | Equipped item removed, it was +0 | `[name]` | `{name} has been disarmed.` |
| 18 | Normal failure, it was enchanted | `[level, name]` | `The enchantment has failed. Your +{level} {name} has been crystallized.` |
| 19 | Normal failure, it was +0 | `[name]` | `The enchantment has failed. Your {name} has been crystallized.` |
| 20 | Crystals gained | `[count, crystalName]` | `You have earned {count} {crystalName}.` |

Message 13 names the level **before** the increase. Key 1 of `EnchantResult` is the level **after**.

On a normal or blessed failure of an equipped item, message 16 or 17 is sent before 18/19 or 15.

## Errors

`Error` (1099), keys 0 `code`, 1 `message`. The scroll is not consumed. A failed confirm also clears the selected scroll.

| Code | When |
|------|------|
| `world` | `EnterWorld` has not completed |
| `bad_request` | Body was not readable |
| `dead` | The player is dead. A selected scroll is cleared |
| `not_owned` | Object id is missing |
| `not_scroll` | The request object is not an enchant scroll |
| `no_request` | Confirm or cancel with no selected scroll |
| `not_enchantable` | Target is not an enchantable weapon or armor (includes the Mobius enchant blacklist) |
| `slot_mismatch` | Weapon scroll on armor, or armor scroll on a weapon |
| `grade_mismatch` | Scroll grade is not the item grade |
| `max_enchant` | Current enchant is already the scroll's maximum |

Armor scrolls also enchant jewelry. No-grade gear has no matching scroll.

## Item fields added for the client

| Message | Key | Field |
|---------|-----|--------|
| `ItemList` row | 9 | enchantLevel |
| `InventoryUpdate` row | 10 | enchantLevel |
| `UserInfo` paperdoll slot | 2 | enchantLevel |
| `CharInfo` | 9 | int[17] enchant, parallel to key 6 item ids. Keys 7–8 are the clan id and crest. |

Saved items gain `enchantLevel` (0 when omitted on older saves).
