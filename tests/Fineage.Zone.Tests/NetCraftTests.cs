using Fineage.Game.Recipes;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Self-craft (#281): learn recipe scroll → book → make wooden arrows.</summary>
public sealed class NetCraftTests
{
    const int RecipeListId = 1;
    const int RecipeItemId = 1666;
    const int Stem = 1864;
    const int IronOre = 1869;
    const int WoodenArrow = 17;

    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static object? Last(NetBridgeRecorder rec, string method) =>
        rec.Of(method).Count == 0 ? null : rec.Of(method).Last()[1];

    [Fact]
    public async Task Learn_recipe_and_craft_wooden_arrows()
    {
        var (kit, rec) = World();
        var (session, player) = await kit.EnterAsync(classId: 0);
        // Dwarven Create Item is not auto for fighters — grant level 1 for the test.
        player.LearnedSkills[RecipeCatalog.CreateDwarvenSkillId] = 1;
        kit.World.RefreshKnownSkillsForTest(player);
        Assert.True(player.KnownSkills.ContainsKey(RecipeCatalog.CreateDwarvenSkillId));

        player.Inventory!.Add(RecipeItemId, 1);
        player.Inventory.Add(Stem, 4);
        player.Inventory.Add(IronOre, 2);
        player.CurMp = Math.Max(player.CurMp, 50);

        var scroll = player.Inventory.FindByItemId(RecipeItemId)!;
        Assert.Null(await kit.World.TryUseItemAsync(session, scroll.ObjectId, default));
        Assert.Contains(RecipeListId, player.KnownRecipes);
        Assert.Equal(0, player.Inventory.CountOf(RecipeItemId));

        Assert.Null(await kit.World.TryRecipeBookOpenAsync(session, new RecipeBookOpenMsg { Dwarven = true }, default));
        var book = Assert.IsType<Fineage.Net.Contract.RecipeBookListNetMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutRecipeBookList)));
        Assert.Equal(1, book.Dwarven);
        Assert.Contains(RecipeListId, book.RecipeIds);

        Assert.Null(await kit.World.TryRecipeMakeSelfAsync(session, new RecipeMakeSelfMsg { RecipeId = RecipeListId }, default));
        var info = Assert.IsType<Fineage.Net.Contract.RecipeItemMakeInfoNetMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutRecipeItemMakeInfo)));
        Assert.Equal(RecipeListId, info.RecipeId);
        Assert.Equal(0, info.Result);
        Assert.Equal(0, player.Inventory.CountOf(Stem));
        Assert.Equal(0, player.Inventory.CountOf(IronOre));
        Assert.Equal(500, player.Inventory.CountOf(WoodenArrow));
    }

    [Fact]
    public async Task Make_without_materials_is_refused()
    {
        var (kit, _) = World();
        var (session, player) = await kit.EnterAsync(classId: 0);
        player.LearnedSkills[RecipeCatalog.CreateDwarvenSkillId] = 1;
        kit.World.RefreshKnownSkillsForTest(player);
        player.KnownRecipes.Add(RecipeListId);
        player.CurMp = 100;

        Assert.Equal(CraftProtocol.Materials,
            await kit.World.TryRecipeMakeSelfAsync(session, new RecipeMakeSelfMsg { RecipeId = RecipeListId }, default));
    }
}
