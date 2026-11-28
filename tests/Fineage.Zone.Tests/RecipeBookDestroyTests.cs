using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>#935: Mobius RequestRecipeBookDestroy — forget one recipe list id.</summary>
public sealed class RecipeBookDestroyTests
{
    const int RecipeListId = 1;

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
    public async Task Destroy_removes_recipe_persists_and_refreshes_book()
    {
        var (kit, rec) = World();
        var (session, player) = await kit.EnterAsync(classId: 0);
        player.KnownRecipes.Add(RecipeListId);

        Assert.Null(await kit.World.TryRecipeBookDestroyAsync(
            session, new RecipeBookDestroyMsg { RecipeId = RecipeListId }, default));
        Assert.DoesNotContain(RecipeListId, player.KnownRecipes);
        var stored = kit.Store.FindById(session.Account, session.ObjectId)!;
        Assert.DoesNotContain(RecipeListId, stored.KnownRecipes ?? []);

        var book = Assert.IsType<RecipeBookListMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutRecipeBookList)));
        Assert.Equal(1, book.Dwarven);
        Assert.DoesNotContain(RecipeListId, book.RecipeIds);
    }

    [Fact]
    public async Task Destroy_unknown_recipe_leaves_book_unchanged()
    {
        var (kit, rec) = World();
        var (session, player) = await kit.EnterAsync(classId: 0);
        player.KnownRecipes.Add(RecipeListId);

        Assert.Null(await kit.World.TryRecipeBookDestroyAsync(
            session, new RecipeBookDestroyMsg { RecipeId = 9_999_999 }, default));
        Assert.Contains(RecipeListId, player.KnownRecipes);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutRecipeBookList)));
    }
}
