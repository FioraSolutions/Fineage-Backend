# Zone Net manufacture / recipe shop

> Mobius `PrivateStoreType.MANUFACTURE` + `RequestRecipeShop*`. Issue [#283](https://github.com/FioraSolutions/Fineage-Backend/issues/283).
> Self-craft: [`craft-v2-protocol.md`](craft-v2-protocol.md) (#281).

## Scope

| In | Out |
|----|-----|
| Open / refresh manufacture list (known recipes + fee) | ALT_GAME_CREATION timed craft |
| Visitor list via `PrivateStoreListRequest` | Rare production / altStatChange XP |
| Make-info + make-item (fee+mats from customer, MP/skill from maker) | RecipeShopManageList UI packet |
| Quit via existing `PrivateStoreQuit` | Offline traders / NO_STORE zones |

## Opcodes

| Dir | Opcode | Name |
|-----|--------|------|
| C→S | `0x039A` | `SetRecipeShop` |
| C→S | `0x039B` | `RecipeShopMakeInfo` |
| C→S | `0x039C` | `RecipeShopMakeItem` |
| S→C | `0x039D` | `RecipeShopSellList` |
| S→C | `0x039E` | `RecipeShopItemInfo` |

Store type byte: **`5`** (`PrivateStoreProtocol.TypeManufacture`, Mobius id).

## Flow notes

- Fee is taken **before** materials and the success roll (kept on fail), matching Mobius `RecipeItemMaker.finishCrafting`.
- Product and material consume happen on the **customer**; MP + create skill on the **manufacturer**.
- Max listings: `CraftProtocol.ManufactureSlots` (50).
