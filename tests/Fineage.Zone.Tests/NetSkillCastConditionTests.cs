using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>&lt;target race="UNDEAD"/&gt;</c> on Holy Strike (49): living humanoid mob refuses with SysMsg 113.</summary>
public sealed class NetSkillCastConditionTests
{
    const int HolyStrike = 49;
    const int HumanoidMob = 20001;
    const int UndeadMob = 20341;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["cond"] = new("cond", true,
            [
                new SpawnNpcDefinition(HumanoidMob, 90001, "Gremlin", NetTestKit.ElfX + 40, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster", Level: 5),
                new SpawnNpcDefinition(UndeadMob, 90002, "Undead Slave", NetTestKit.ElfX + 50, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster", Level: 19),
            ])
        });

    [Fact]
    public async Task Holy_strike_refuses_non_undead_target()
    {
        var kit = new NetTestKit(Map(), "cond", new GeoEngine());
        var (session, player) = await kit.EnterAsync(classId: 10, race: 1);
        player.KnownSkills[HolyStrike] = 1;
        player.CurMp = player.MaxMp = 500;
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, HumanoidMob, CancellationToken.None));
        int mp = player.CurMp;
        Assert.Null(await kit.World.TrySkillUseAsync(session, HolyStrike, HumanoidMob, CancellationToken.None));
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
, m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgUnsuitableTerms);
        Assert.Equal(mp, player.CurMp);
    }

}