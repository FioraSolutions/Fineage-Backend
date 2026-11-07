using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius skill &lt;conditions&gt;&lt;using kind="DAGGER"/&gt;: Mortal Blow without a dagger → SysMsg 113, no cast.</summary>
public sealed class NetSkillWeaponConditionTests
{
    const int MobId = 9_200_201;
    const int MortalBlow = 16;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["cond"] = new("cond", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 1, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit(Map(), "cond", new GeoEngine());
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        player.KnownSkills[MortalBlow] = 1;
        player.CurMp = player.MaxMp = 500;
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, player, rec);
    }

    static bool Unsuitable(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)).Any(m => ((SystemMessageNetMsg)m[1]!).Id == ZoneWorld.SysMsgUnsuitableTerms);

    [Fact]
    public async Task Mortal_blow_without_dagger_is_refused()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var bag = player.Inventory!;
        if (bag.GetEquipped(PaperdollSlot.RHand) is { } w && w.Template.WeaponType == WeaponType.Dagger)
            bag.Equip(bag.Add(1)); // Short Sword
        Assert.Null(await kit.World.TrySkillUseAsync(session, MortalBlow, MobId, CancellationToken.None));
        Assert.True(Unsuitable(rec));
        Assert.Null(player.PendingHit);
        Assert.False(player.SkillReuseUntilMs.ContainsKey(MortalBlow));
    }

    [Fact]
    public async Task Mortal_blow_with_dagger_casts()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var bag = player.Inventory!;
        bag.Equip(bag.Add(10)); // Dagger
        Assert.Null(await kit.World.TrySkillUseAsync(session, MortalBlow, MobId, CancellationToken.None));
        Assert.False(Unsuitable(rec));
        Assert.True(player.PendingHit != null || session.PendingCastSkillId == MortalBlow);
    }
}
