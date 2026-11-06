using System.Linq;
using Fineage.Login;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NetEscapeScrollTests
{
    const int Soe = 736;
    const int Bsoe = 1538;

    [Theory]
    [InlineData(Soe)]
    [InlineData(Bsoe)]
    public async Task UseItem_escape_scroll_consumes_and_teleports_to_class_spawn(int itemId)
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync(
            "escape",
            "Escaper",
            [new StoredItem(itemId, 2, -1)],
            classId: HumanClasses.Fighter,
            race: 0,
            x: -72674,
            y: 256819,
            z: -3112);

        int before = actor.Inventory.Items.Where(i => i.ItemId == itemId).Sum(i => i.Count);
        Assert.True(before >= 1);

        Assert.Null(await actor.Use(actor.Slot(itemId)));

        int after = actor.Inventory.Items.Where(i => i.ItemId == itemId).Sum(i => i.Count);
        Assert.Equal(before - 1, after);

        Assert.True(bench.Game.Classes.TryGet(HumanClasses.Fighter, out var template) && template != null);
        var spawn = template.Spawns[0];
        Assert.Equal(spawn.X, actor.Session.X);
        Assert.Equal(spawn.Y, actor.Session.Y);
        Assert.Equal(spawn.Z, actor.Session.Z);
    }
}
