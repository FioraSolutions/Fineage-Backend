using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius doAttack / doCast: no line of sight → SysMsg 181 "Cannot see target.", no swing, attack intention dropped.</summary>
public sealed class NetPlayerLosTests
{
    const int MobId = 9_200_101;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["los"] = new("los", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 1, "Gremlin", NetTestKit.ElfX + 60, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    /// <summary>Flat ground at ElfZ with a 2-cell tall wall between the player (geo local x 829) and the mob (833).</summary>
    static GeoEngine Geo(bool wall)
    {
        var region = GeoEngine.Region.Flat1((short)NetTestKit.ElfZ);
        if (wall)
            region.SetComplexBlock(103, 66, (cx, cy) =>
                cx >= 6 ? ((short)(NetTestKit.ElfZ + 600), (byte)0) : ((short)NetTestKit.ElfZ, GeoEngine.All));
        var geo = new GeoEngine();
        geo.SetRegion(21, 19, region);
        return geo;
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, NetBridgeRecorder Rec)> SetupAsync(bool wall)
    {
        var kit = new NetTestKit(Map(), "los", Geo(wall));
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        Assert.Contains(MobId, player.Knownlist);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, rec);
    }

    [Fact]
    public async Task Wall_between_blocks_the_swing_with_cannot_see_target()
    {
        var (kit, session, rec) = await SetupAsync(wall: true);
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgCannotSeeTarget);
    }

    [Fact]
    public async Task Open_ground_swings_normally()
    {
        var (kit, session, rec) = await SetupAsync(wall: false);
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.True(session.AutoAttackTargetId > 0);
    }
}
