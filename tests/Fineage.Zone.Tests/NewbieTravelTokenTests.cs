using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius NewbieTravelToken on the Elven Village Newbie Guide (30599).</summary>
public sealed class NewbieTravelTokenTests
{
    [Fact]
    public async Task Talk_serves_the_guide_page_or_cant_travel_at_twenty()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync(classId: 18);
        elf.Level = 10;
        Assert.Null(kit.World.VillageMasterForTest(session, 30599, "NewbieTravelToken", "", out var html));
        Assert.Contains("Quest NewbieTravelToken 30600", html);

        elf.Level = 20;
        kit.World.VillageMasterForTest(session, 30599, "NewbieTravelToken", "", out html);
        Assert.DoesNotContain("Quest NewbieTravelToken 30600", html);
        Assert.DoesNotContain("not implemented", html);
    }

    [Fact]
    public async Task Trip_takes_one_token_and_needs_one()
    {
        // Default spawns put elven-village NPCs in the world so the destination counts as loaded.
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_village");
        var (session, elf) = await kit.EnterAsync(classId: 18);
        elf.Level = 10;
        kit.World.VillageMasterForTest(session, 30599, "NewbieTravelToken", "30599", out _);
        Assert.Equal(0, elf.Inventory!.CountOf(8542)); // no token: refused, nothing taken

        elf.Inventory.Add(8542, 2);
        Assert.Null(kit.World.VillageMasterForTest(session, 30599, "NewbieTravelToken", "30599", out var trip));
        Assert.True(elf.Inventory.CountOf(8542) == 1, trip);
        Assert.Equal(1, elf.Inventory.CountOf(8542));
        Assert.InRange(elf.X, 45470 - 200, 45470 + 200);

        kit.World.VillageMasterForTest(session, 30599, "NewbieTravelToken", "30601", out var far);
        Assert.Equal(1, elf.Inventory.CountOf(8542)); // Dwarven Village not loaded: token kept
        Assert.Contains("not on a loaded map", far);

        elf.Level = 20;
        kit.World.VillageMasterForTest(session, 30599, "NewbieTravelToken", "30599", out _);
        Assert.Equal(1, elf.Inventory.CountOf(8542));
    }
}
