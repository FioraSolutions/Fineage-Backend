using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using System.Linq;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Sonic Focus charges and Sonic Blaster <c>player charges="2"</c> gate.</summary>
[Trait("Category", "Integration")]
public sealed class NetForceChargeTests
{
    const int MobId = 9_300_080;
    const int SonicBlaster = 6;
    const int SonicFocus = 8;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["force"] = new("force", true,
            [
                new SpawnNpcDefinition(MobId, 90_001, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> GladiatorSetupAsync()
    {
        var kit = new NetTestKit(Map(), "force", new GeoEngine());
        var (session, player) = await kit.EnterAsync(classId: 2, race: 0, level: 80);
        player.KnownSkills[SonicBlaster] = 37;
        player.KnownSkills[SonicFocus] = 7;
        player.CurMp = player.MaxMp = 2000;
        var bag = player.Inventory!;
        bag.Equip(bag.Add(2523)); // Katana*Katana — dual
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, player, rec);
    }

    static bool Unsuitable(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Any(m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgUnsuitableTerms);

    static void ResolveCast(NetTestKit kit, ZoneEntity player)
    {
        kit.Now = kit.Now.AddSeconds(10);
        kit.World.ResolvePendingHitForTest(player);
    }

    [Fact]
    public async Task Gladiator_skill_list_includes_learned_passives()
    {
        var (kit, _, player, _) = await GladiatorSetupAsync();
        var msg = NetEnterWorldProjector.BuildSkills(player, kit.Now.ToUnixTimeMilliseconds());
        Assert.True(msg.Skills.Length >= 10);

        var game = TestCatalogs.Game;
        bool listHasPassive = msg.Skills.Any(e =>
        {
            var template = game.Skills.Find(e.SkillId);
            return template is { Passive: true };
        });
        bool bookHasPassive = player.KnownSkills.Keys.Any(id =>
        {
            var template = game.Skills.Find(id);
            return template is { Passive: true };
        });
        Assert.True(bookHasPassive);
        Assert.True(listHasPassive);
    }

    [Fact]
    public async Task Sonic_blaster_without_force_charges_is_refused()
    {
        var (kit, session, player, rec) = await GladiatorSetupAsync();
        Assert.Equal(0, player.ForceCharges);
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicBlaster, MobId, CancellationToken.None));
        Assert.True(Unsuitable(rec));
    }

    [Fact]
    public async Task Sonic_focus_builds_charges_then_blaster_casts()
    {
        var (kit, session, player, rec) = await GladiatorSetupAsync();
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicFocus, player.ObjectId, CancellationToken.None));
        ResolveCast(kit, player);
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicFocus, player.ObjectId, CancellationToken.None));
        ResolveCast(kit, player);
        Assert.Equal(2, player.ForceCharges);

        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Clear();
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicBlaster, MobId, CancellationToken.None));
        ResolveCast(kit, player);
        Assert.False(Unsuitable(rec));
        Assert.Equal(0, player.ForceCharges);
    }

    [Fact]
    public async Task Sonic_focus_at_seven_charges_refuses_with_max_capacity_message()
    {
        var (kit, session, player, rec) = await GladiatorSetupAsync();
        player.ForceCharges = 7;
        int mp = player.CurMp;
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Clear();

        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicFocus, player.ObjectId, CancellationToken.None));
        Assert.Equal(7, player.ForceCharges);
        Assert.Equal(mp, player.CurMp);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ForceChargeRules.SysMsgForceMaxCapacity);
    }

    [Fact]
    public async Task Sonic_focus_sends_etc_status_when_charges_change()
    {
        var (kit, session, player, rec) = await GladiatorSetupAsync();
        rec.Of(nameof(IWorldEventPublisher.FanoutEtcStatusUpdate)).Clear();
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicFocus, player.ObjectId, CancellationToken.None));
        ResolveCast(kit, player);
        Assert.Equal(1, player.ForceCharges);
        var status = rec.Of(nameof(IWorldEventPublisher.FanoutEtcStatusUpdate))
            .Select(m => (EtcStatusUpdateMsg)m[1]!)
            .Last();
        Assert.Equal(1, status.Charges);
    }

    [Fact]
    public async Task Force_charges_decay_after_ten_minutes_idle()
    {
        var (kit, session, player, _) = await GladiatorSetupAsync();
        Assert.Null(await kit.World.TrySkillUseAsync(session, SonicFocus, player.ObjectId, CancellationToken.None));
        ResolveCast(kit, player);
        Assert.Equal(1, player.ForceCharges);
        Assert.True(player.ForceChargeResetAtUnixMs > 0);

        kit.AdvanceToTickCovering(player.ForceChargeResetAtUnixMs + 1);
        kit.World.StepSimulation();
        Assert.Equal(0, player.ForceCharges);
        Assert.Equal(0, player.ForceChargeResetAtUnixMs);
    }
}