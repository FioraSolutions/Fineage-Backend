using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Drops;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius death loot: DropProtection (killer/party only for 15 s, others get SysMsg 145), ItemsAutoDestroy
/// (600 s, herbs 60 s), randDropLim ±70 scatter around the corpse.
/// </summary>
public sealed class NetDropProtectTests
{
    static List<DropItemNetMsg> Drops(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutDropItem)).Select(a => (DropItemNetMsg)a[1]!).ToList();

    static List<int> SysIds(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)).Select(a => ((SystemMessageNetMsg)a[1]!).Id).ToList();

    [Fact]
    public async Task Killer_loot_is_protected_for_others_then_free_and_scattered()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (otherSession, other) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        kit.World.DropDeathLootForTest(TalkingIsland.GremlinNpcId, killer.X, killer.Y, killer.Z, killer);
        var drop = Assert.Single(Drops(rec));
        Assert.InRange(drop.X, killer.X - ZoneWorld.DeathDropScatter, killer.X + ZoneWorld.DeathDropScatter);
        Assert.InRange(drop.Y, killer.Y - ZoneWorld.DeathDropScatter, killer.Y + ZoneWorld.DeathDropScatter);
        int groundId = (int)drop.EntityValue;
        Assert.True(kit.World.TryGetEntityById(groundId, out var ground));
        Assert.Equal(killer.ObjectId, ground.GroundOwnerId);
        other.X = ground.X; other.Y = ground.Y;
        other.Knownlist.Add(groundId);

        rec.Calls.Clear();
        Assert.Equal("protected", await kit.World.TryPickupAsync(otherSession, groundId, CancellationToken.None));
        Assert.Contains(ZoneWorld.SysMsgFailedToPickup, SysIds(rec));
        Assert.True(kit.World.TryGetEntityById(groundId, out _));

        kit.Now = kit.Now.AddMilliseconds(ZoneWorld.DropProtectMs + 1);
        Assert.Null(await kit.World.TryPickupAsync(otherSession, groundId, CancellationToken.None));
        Assert.False(kit.World.TryGetEntityById(groundId, out _));
    }

    [Fact]
    public async Task Mob_loot_auto_destroys_after_600s()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        kit.World.DropDeathLootForTest(TalkingIsland.GremlinNpcId, killer.X, killer.Y, killer.Z, killer);
        int groundId = (int)Assert.Single(Drops(rec)).EntityValue;

        kit.Now = kit.Now.AddMilliseconds(ZoneWorld.GroundItemAutoDestroyMs - 1000);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.True(kit.World.TryGetEntityById(groundId, out _));

        kit.Now = kit.Now.AddMilliseconds(2000);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.False(kit.World.TryGetEntityById(groundId, out _));
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutEntityDespawn)));
    }

    [Theory]
    [InlineData(18342)] // Gremlin (training hall)
    [InlineData(20013)] // Dryad
    [InlineData(20530)] // Young Red Keltir
    [InlineData(20468)] // Kaboo Orc
    public void Elven_mobs_have_generated_droplists(int npcId)
    {
        var catalog = DropCatalog.LoadDefault();
        Assert.True(catalog.TryGet(npcId, out var def));
        Assert.Contains(def!.Death.Groups, g => g.Items.Any(i => i.ItemId == 57));
    }
}
