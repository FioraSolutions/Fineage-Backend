using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Shared cast-condition pipelines: item consume at hit, charge spend at cast start.</summary>
public sealed class NetSkillConditionPipelineTests
{
    const int StormCubic = 10;
    const int CursedBone = 1458;
    const int SonicBlaster = 6;
    const int SonicFocus = 8;
    const int MobObjectId = 9_300_080; // spawn row object id (first ctor arg)

    static SpawnCatalog ForceMap() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["pipe"] = new("pipe", true,
            [
                new SpawnNpcDefinition(MobObjectId, 90_001, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, true, 50_000, 1f, 1f, 60, 0, "", "Monster", Level: 1, Aggressive: false)
            ])
        });

    [Fact]
    public async Task Item_consume_checked_before_cast_and_spent_at_hit()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 10, race: 1, level: 40);
        player.KnownSkills[StormCubic] = 1;
        player.CurMp = player.MaxMp = 500;
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        int mp = player.CurMp;
        Assert.Null(await kit.World.TrySkillUseAsync(session, StormCubic, player.ObjectId, CancellationToken.None));
        Assert.Equal(mp, player.CurMp);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgNotEnoughItems);

        player.Inventory!.Add(CursedBone, 5);
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Clear();
        Assert.Null(await kit.World.TrySkillUseAsync(session, StormCubic, player.ObjectId, CancellationToken.None));
        Assert.Equal(5, player.Inventory.CountOf(CursedBone));
        kit.Now = kit.Now.AddSeconds(10);
        kit.World.ResolvePendingHitForTest(player);
        Assert.Equal(0, player.Inventory.CountOf(CursedBone));
    }

    [Fact]
    public async Task Sonic_blaster_spends_charges_at_cast_start_not_on_refusal()
    {
        var kit = new NetTestKit(ForceMap(), "pipe", new GeoEngine());
        var (session, player) = await kit.EnterAsync(classId: 2, race: 0, level: 80);
        player.KnownSkills[SonicBlaster] = 37;
        player.KnownSkills[SonicFocus] = 7;
        player.CurMp = player.MaxMp = 2000;
        player.ForceCharges = 2;
        player.Inventory!.Equip(player.Inventory.Add(2523));

        var refused = await kit.World.TrySkillUseAsync(session, SonicBlaster, 0, CancellationToken.None);
        Assert.True(refused is null or SkillProtocol.Target);
        Assert.Equal(2, player.ForceCharges);

        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobObjectId, CancellationToken.None));
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicBlaster, MobObjectId, CancellationToken.None));
        Assert.Equal(0, player.ForceCharges);
    }
}