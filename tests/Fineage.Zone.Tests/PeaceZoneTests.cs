using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius PeaceZone NPoly: playable versus playable is refused when either stands inside.</summary>
public sealed class PeaceZoneTests
{
    [Fact]
    public void Talking_island_town_contains_its_interior_and_not_the_origin()
    {
        var peace = PeaceZones.LoadDefault();
        Assert.True(peace.Count >= 127);
        Assert.True(peace.Contains(-84309, 242995, -3700));
        Assert.False(peace.Contains(0, 0, 0));
        Assert.False(peace.Contains(-84309, 242995, 0));
    }

    [Fact]
    public void Gludin_arena_is_a_pvp_zone()
    {
        var pvp = PvpZones.LoadDefault();
        Assert.Equal(6, pvp.Count);
        Assert.True(pvp.Contains(-87920, 142220, -2000));
        Assert.False(pvp.Contains(0, 0, 0));
    }

    [Fact]
    public async Task Two_players_inside_a_peace_zone_cannot_swing()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (session, player) = await kit.EnterAsync(classId: 0);
        var (other, victim) = await kit.EnterAsync(classId: 0);
        victim.PvpFlagged = true;
        victim.MaxHp = 500;
        victim.CurHp = 500;
        Assert.True(kit.World.Peace.Contains(player.X, player.Y, player.Z));

        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TryAttackAsync(session, other.ObjectId, CancellationToken.None));
        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Equal(500, victim.CurHp);
        // Mobius doAttack in a peace zone is ActionFailed with no system message. SysMsg 85 is for bad skills.
        Assert.DoesNotContain(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgPeaceTarget);

        player.X = session.X = 0;
        player.Y = session.Y = 0;
        player.Z = session.Z = 0;
        victim.X = other.X = 40;
        victim.Y = other.Y = 0;
        victim.Z = other.Z = 0;
        Assert.Null(await kit.World.TryAttackAsync(session, other.ObjectId, CancellationToken.None));
        Assert.Equal(other.ObjectId, session.AutoAttackTargetId);
    }
}
