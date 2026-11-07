using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Interlude cast ↔ movement: cast stops the run, moves/skills mid-cast are queued, no sit while casting.</summary>
public sealed class NetCastMoveTests
{
    const int MobId = 9_200_301;
    const int WindStrike = 1177;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["castmove"] = new("castmove", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 1, "Gremlin", NetTestKit.ElfX + 300, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player)> SetupAsync()
    {
        var kit = new NetTestKit(Map(), "castmove");
        var (session, player) = await kit.EnterAsync(classId: 25, race: 1); // Elven Mystic
        player.KnownSkills[WindStrike] = 1;
        player.CurMp = player.MaxMp = 500;
        var (bridge, _) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, player);
    }

    static Task<string?> Move(NetTestKit kit, ZoneSession s, ZoneEntity p, int dx) =>
        kit.World.TryMoveAsync(s, new MoveToMsg { ObjectId = p.ObjectId, DestX = p.X + dx, DestY = p.Y, DestZ = p.Z }, CancellationToken.None);

    static Task<string?> Cast(NetTestKit kit, ZoneSession s, int skill = WindStrike) =>
        kit.World.TrySkillUseAsync(s, skill, MobId, CancellationToken.None);

    long Now(NetTestKit kit) => kit.Now.ToUnixTimeMilliseconds();

    [Fact]
    public async Task Cast_stops_the_run_and_faces_target()
    {
        var (kit, session, player) = await SetupAsync();
        Assert.Null(await Move(kit, session, player, -400));
        Assert.True(player.MoveActive);
        kit.Now = kit.Now.AddMilliseconds(200);

        Assert.Null(await Cast(kit, session));
        Assert.True(player.CastingUntilMs > Now(kit));
        Assert.False(player.MoveActive);
        int stopX = player.X;
        Assert.True(stopX < NetTestKit.ElfX, "stopped where the run had got to");
        Assert.Equal(Fineage.Zone.Combat.ZoneCombat.HeadingFrom(player.X, player.Y, NetTestKit.ElfX + 300, NetTestKit.ElfY), player.Heading);

        kit.Now = kit.Now.AddMilliseconds(500);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.Equal(stopX, player.X);
    }

    [Fact]
    public async Task Move_during_cast_is_queued_until_cast_ends()
    {
        var (kit, session, player) = await SetupAsync();
        Assert.Null(await Cast(kit, session));
        long castEnd = player.CastingUntilMs;
        Assert.True(castEnd > Now(kit));

        Assert.Null(await Move(kit, session, player, -300));
        Assert.False(player.MoveActive);
        Assert.NotNull(session.QueuedMove);

        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(castEnd);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.Null(session.QueuedMove);
        Assert.True(player.MoveActive);
    }

    [Fact]
    public async Task Skill_during_cast_is_queued()
    {
        var (kit, session, player) = await SetupAsync();
        Assert.Null(await Cast(kit, session));
        long castEnd = player.CastingUntilMs;
        player.SkillReuseUntilMs.Clear();
        Assert.Null(await Cast(kit, session));
        Assert.Equal(WindStrike, session.PendingCastSkillId);
        Assert.Equal(castEnd, session.PendingCastAtUnixMs);
    }

    [Fact]
    public async Task No_sit_while_casting_and_sit_on_arrival_while_running()
    {
        var (kit, session, player) = await SetupAsync();
        var sit = new CharacterActionRequestMsg { ActionId = ActionProtocol.SitStand };
        Assert.Null(await Cast(kit, session));
        player.CombatUntilUnixMs = 0;
        Assert.Equal(ActionProtocol.Blocked, await kit.World.TryCharacterActionAsync(session, sit, CancellationToken.None));
        Assert.False(player.Sitting);

        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(player.CastingUntilMs + 1);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        player.CombatUntilUnixMs = 0;
        Assert.Null(await Move(kit, session, player, -200));
        Assert.True(player.MoveActive);
        Assert.Null(await kit.World.TryCharacterActionAsync(session, sit, CancellationToken.None));
        Assert.False(player.Sitting);
        Assert.True(session.SitOnArrive);

        kit.Now = kit.Now.AddSeconds(5);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.False(player.MoveActive);
        Assert.True(player.Sitting);
    }
}
