using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius PvP over Net (audit <c>D4</c>). <c>TryAttack</c>, the attack skills and the debuffs used to reject every
/// non-NPC target, so two players could select each other and never land a point of damage. Mobius'
/// <c>isAutoAttackable(Player)</c> is the real gate: a player is attackable while PvP-flagged or carrying karma, and
/// the swing rolls against that player's own P.Def / evasion / shield.
/// </summary>
public sealed class NetPvpTests
{
    const int MobId = 9_400_001;

    static SpawnCatalog Map(int x, int y, int z) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["pvp"] = new("pvp", true,
            [
                new SpawnNpcDefinition(MobId, 90001, "Gremlin", x + 250, y, z, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneSession Other, ZoneEntity OtherPlayer,
        NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit(Map(NetTestKit.ElfX, NetTestKit.ElfY, NetTestKit.ElfZ), "pvp");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        var (other, otherPlayer) = await kit.EnterAsync(classId: 0, race: 0);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        // Same tile: both in melee reach, and each knows the other.
        Assert.Contains(other.ObjectId, player.Knownlist);
        Assert.Contains(player.ObjectId, otherPlayer.Knownlist);
        return (kit, session, player, other, otherPlayer, rec);
    }

    /// <summary>Resolve the queued swing: it lands at hitTime, so step the clock and run one combat tick.</summary>
    static async Task SwingAsync(NetTestKit kit)
    {
        kit.Now = kit.Now.AddMilliseconds(3_000);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
    }

    /// <summary>
    /// Swing until <paramref name="done"/> holds. Combat rolls hit chance, so a single swing can miss by design;
    /// the tests assert the outcome, not that the first roll connected.
    /// </summary>
    static async Task SwingUntilAsync(NetTestKit kit, ZoneSession session, int targetId, Func<bool> done)
    {
        for (int i = 0; i < 20 && !done(); i++)
        {
            Assert.Null(await kit.World.TryAttackAsync(
                session, new AttackRequestMsg { TargetObjectId = targetId }, CancellationToken.None));
            await SwingAsync(kit);
        }
    }

    [Fact]
    public async Task A_flagged_player_can_be_hit_and_loses_hp()
    {
        var (kit, session, player, other, otherPlayer, rec) = await SetupAsync();
        otherPlayer.PvpFlagged = true;
        otherPlayer.MaxHp = 100_000;
        otherPlayer.CurHp = 100_000;

        await SwingUntilAsync(kit, session, other.ObjectId, () => otherPlayer.CurHp < 100_000);

        Assert.True(otherPlayer.CurHp < 100_000,
            $"a flagged player must take damage, HP stayed at {otherPlayer.CurHp}");

        // The victim's HP reaches the wire, or its bar would never move on screen.
        var hp = LatestNetHp(rec, other.ObjectId);
        Assert.NotNull(hp);
        Assert.True(hp!.Value.CurHp < 100_000, $"wire HP was {hp.Value.CurHp}");
    }

    [Fact]
    public async Task An_unflagged_player_is_not_attackable()
    {
        var (kit, session, player, other, otherPlayer, rec) = await SetupAsync();
        otherPlayer.MaxHp = 100_000;
        otherPlayer.CurHp = 100_000;

        // Mobius isAutoAttackable(Player): white name, no karma → the request is refused, not merely harmless.
        string? error = await kit.World.TryAttackAsync(
            session, new AttackRequestMsg { TargetObjectId = other.ObjectId }, CancellationToken.None);

        Assert.NotNull(error);
        await SwingAsync(kit);
        await SwingAsync(kit);
        Assert.Equal(100_000, otherPlayer.CurHp);
    }

    [Fact]
    public async Task A_player_with_karma_is_attackable_without_a_flag()
    {
        var (kit, session, player, other, otherPlayer, rec) = await SetupAsync();
        otherPlayer.PkKills = 1;
        otherPlayer.MaxHp = 100_000;
        otherPlayer.CurHp = 100_000;

        await SwingUntilAsync(kit, session, other.ObjectId, () => otherPlayer.CurHp < 100_000);

        Assert.True(otherPlayer.CurHp < 100_000, $"a red player must take damage, HP stayed at {otherPlayer.CurHp}");
    }

    [Fact]
    public async Task You_cannot_attack_yourself()
    {
        var (kit, session, player, _, _, _) = await SetupAsync();
        player.PvpFlagged = true;

        Assert.NotNull(await kit.World.TryAttackAsync(
            session, new AttackRequestMsg { TargetObjectId = session.ObjectId }, CancellationToken.None));
    }

    [Fact]
    public async Task Killing_a_flagged_player_runs_the_normal_death_path()
    {
        var (kit, session, player, other, otherPlayer, rec) = await SetupAsync();
        otherPlayer.PvpFlagged = true;
        otherPlayer.MaxHp = 100_000;
        otherPlayer.CurHp = 1; // any hit kills

        await SwingUntilAsync(kit, session, other.ObjectId, () => otherPlayer.Dead);

        Assert.True(otherPlayer.Dead, $"victim HP={otherPlayer.CurHp} should have died");
        Assert.Equal(0, other.AutoAttackTargetId);
    }

    /// <summary>The last HP/MaxHP pair the zone sent for <paramref name="objectId"/>.</summary>
    static (int CurHp, int MaxHp)? LatestNetHp(NetBridgeRecorder rec, int objectId)
    {
        (int CurHp, int MaxHp)? last = null;
        foreach (var args in rec.Of(nameof(INetPresenceBridge.FanoutStatusUpdate)))
        {
            if (args[1] is not StatusUpdateNetMsg msg || msg.EntityValue != (ulong)(uint)objectId)
                continue;
            var ids = msg.AttributeIds ?? [];
            var vals = msg.Values ?? [];
            int curHp = -1, maxHp = -1;
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                if (ids[i] == InterludeStatus.CurHp) curHp = vals[i];
                if (ids[i] == InterludeStatus.MaxHp) maxHp = vals[i];
            }
            if (curHp >= 0)
                last = (curHp, maxHp);
        }
        return last;
    }
}
