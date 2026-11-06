# Zone Net self-craft

> Mobius `RecipeData` / `RequestRecipeItemMakeSelf`. Issue [#281](https://github.com/FioraSolutions/Fineage-Backend/issues/281).
> Catalog: `data/recipes.json` (from `scripts/gen_recipes.py` ← Mobius `Recipes.xml`).

## Scope

| In | Out |
|----|-----|
| Learn recipe scroll on use | Rare production / altStatChange XP |
| Recipe book open (dwarven / common tabs) | Normalized Postgres recipe table (JSON mirror for now) |
| Make-self: materials, MP, create skill level, success rate | Manufacture shop → [`craft-manufacture-v2-protocol.md`](craft-manufacture-v2-protocol.md) (#283) |

## Opcodes

| Dir | Opcode | Name |
|-----|--------|------|
| C→S | `0x0396` | `RecipeBookOpen` |
| C→S | `0x0397` | `RecipeMakeSelf` |
| S→C | `0x0398` | `RecipeBookList` |
| S→C | `0x0399` | `RecipeItemMakeInfo` |

Create skills: dwarven **172**, common **1320** (Mobius `CommonSkill`).
