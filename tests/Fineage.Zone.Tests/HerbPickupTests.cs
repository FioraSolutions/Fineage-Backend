using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Player.doPickupItem herb branch: used on pickup, never stored.</summary>
public sealed class HerbPickupTests
{
    static Task<string?> Pick(NetTestKit kit, ZoneSession s, int id) =>
        kit.World.TryPickupAsync(s, id, CancellationToken.None);

    [Fact]
    public async Task Herb_of_life_heals_on_pickup_and_is_not_stored()
    {
        var kit = new NetTestKit();
        var (s, p) = await kit.EnterAsync(classId: 18);
        p.CurHp = 10;
        int hp = p.MaxHp;
        int ground = kit.World.PlaceGroundForTest(p, 8600);
        Assert.Null(await Pick(kit, s, ground));
        Assert.Equal(Math.Min(hp, 10 + hp * 15 / 100), p.CurHp);
        Assert.Equal(0, p.Inventory!.CountOf(8600));
        Assert.False(kit.World.ExistsForTest(ground));

        // Full HP: still consumed (Mobius has no full-HP refusal), HP stays at max.
        p.CurHp = p.MaxHp;
        ground = kit.World.PlaceGroundForTest(p, 8602);
        Assert.Null(await Pick(kit, s, ground));
        Assert.Equal(p.MaxHp, p.CurHp);
        Assert.False(kit.World.ExistsForTest(ground));
    }

    [Fact]
    public async Task Warrior_herb_grants_three_buffs_and_recovery_restores_both()
    {
        var kit = new NetTestKit();
        var (s, p) = await kit.EnterAsync(classId: 18);
        Assert.Null(await Pick(kit, s, kit.World.PlaceGroundForTest(p, 8612)));
        foreach (int skill in new[] { 2280, 2282, 2284 })
            Assert.Contains(p.Abnormals, a => a.Id == skill);
        Assert.Equal(0, p.Inventory!.CountOf(8612));

        p.CurHp = 1;
        p.CurMp = 1;
        Assert.Null(await Pick(kit, s, kit.World.PlaceGroundForTest(p, 8614)));
        Assert.Equal(p.MaxHp, p.CurHp);
        Assert.Equal(p.MaxMp, p.CurMp);
    }

    [Fact]
    public async Task Dead_player_cannot_pick_up_a_herb()
    {
        var kit = new NetTestKit();
        var (s, p) = await kit.EnterAsync(classId: 18);
        int ground = kit.World.PlaceGroundForTest(p, 8603);
        p.Dead = true;
        p.CurMp = 1;
        Assert.NotNull(await Pick(kit, s, ground));
        Assert.Equal(1, p.CurMp);
        Assert.True(kit.World.ExistsForTest(ground));
    }
}
