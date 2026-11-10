using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Player.setTarget</c> over Net: a click on a creature answers with <c>MyTargetSelected</c>
/// (<c>target.isAutoAttackable(player) ? player.getLevel() - target.getLevel() : 0</c>) plus the target's
/// <c>StatusUpdate</c> MAX_HP/CUR_HP, and the action table's next-target / panel-attack rows reach the world.
/// </summary>
public sealed class NetTargetSelectTests
{
    const int MobId = 9_300_001;
    const int GuideId = 9_300_002;

    static SpawnCatalog Map(int x, int y, int z) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["targets"] = new("targets", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 1, "Gremlin", x + 250, y, z, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []),
                new SpawnNpcDefinition(GuideId, 90000 + 2, "Guide", x + 300, y, z, 0, false, 50_000, 1f, 1f, 60, 0, "", "Folk",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit(Map(NetTestKit.ElfX, NetTestKit.ElfY, NetTestKit.ElfZ), "targets");
        var (session, player) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Contains(MobId, player.Knownlist);
        return (kit, session, player, rec);
    }

    [Fact]
    public async Task Attackable_target_gets_the_level_delta_colour_and_its_hp()
    {
        var (kit, session, player, rec) = await SetupAsync();
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.Level = player.Level - 3;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, default));

        Assert.Equal(MobId, session.SelectedTargetId);
        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(MobId, (int)selected[1]!);
        Assert.Equal(3, (int)selected[2]!);

        // Mobius setTarget also sends the target's HP so the target window can draw a bar.
        var hp = Assert.IsType<StatusUpdateMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)))[1]);
        Assert.Equal(MobId, hp.ObjectId);
        Assert.Contains(InterludeStatus.MaxHp, hp.AttributeIds);
        Assert.Contains(InterludeStatus.CurHp, hp.AttributeIds);
    }

    [Fact]
    public async Task Friendly_npc_gets_colour_zero_not_a_level_delta()
    {
        var (kit, session, player, rec) = await SetupAsync();
        Assert.True(kit.World.TryGetEntityById(GuideId, out var guide));
        guide.Level = player.Level + 10;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, GuideId, default));

        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(GuideId, (int)selected[1]!);
        Assert.Equal(0, (int)selected[2]!);
    }

    /// <summary>
    /// Players are selectable like any other creature in Mobius. Blocking them left the client unable to
    /// target another player, so their nameplate, level and HP window never opened in multiplayer.
    /// </summary>
    [Fact]
    public async Task A_pvp_flagged_player_is_selectable_with_the_level_delta_colour()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var (otherSession, otherPlayer) = await kit.EnterAsync();
        otherPlayer.Level = player.Level + 4;
        otherPlayer.PvpFlagged = true;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, otherSession.ObjectId, default));

        Assert.Equal(otherSession.ObjectId, session.SelectedTargetId);
        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(otherSession.ObjectId, (int)selected[1]!);
        // A flagged player is auto-attackable, so the colour is the level delta rather than the friendly 0.
        Assert.Equal(-4, (int)selected[2]!);

        var hp = Assert.IsType<StatusUpdateMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)))[1]);
        Assert.Equal(otherSession.ObjectId, hp.ObjectId);
    }

    [Fact]
    public async Task An_unflagged_player_is_selectable_and_white()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var (otherSession, otherPlayer) = await kit.EnterAsync();
        otherPlayer.Level = player.Level + 4;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, otherSession.ObjectId, default));

        Assert.Equal(otherSession.ObjectId, session.SelectedTargetId);
        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(0, (int)selected[2]!);
    }

    [Fact]
    public async Task Selecting_yourself_is_colour_zero()
    {
        var (kit, session, player, rec) = await SetupAsync();

        Assert.Null(await kit.World.TrySelectTargetAsync(session, session.ObjectId, default));

        Assert.Equal(session.ObjectId, session.SelectedTargetId);
        // Own pawn: Mobius sends colour 0 and no HP block, so a self-target draws as white without a bar.
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
    }

    [Fact]
    public async Task Next_target_action_selects_the_nearest_hostile()
    {
        var (kit, session, player, rec) = await SetupAsync();

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.NextTarget, 0, default));

        Assert.Equal(MobId, session.SelectedTargetId);
        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(MobId, (int)selected[1]!);
    }

    [Fact]
    public async Task Panel_attack_action_reaches_the_world()
    {
        var (kit, session, player, rec) = await SetupAsync();

        // Action 2 with nothing selected is the same "no target" answer as AttackRequest.
        string? error = await kit.World.TryCharacterActionAsync(session, ActionProtocol.Attack, 0, default);

        Assert.NotNull(error);
    }

    /// <summary>
    /// Mobius <c>RequestTargetCanceld</c>. Before this the client cleared its own selection and the zone
    /// kept the old one, so a skill or attack with no explicit target still aimed at whatever was clicked last.
    /// </summary>
    [Fact]
    public async Task Cancelling_the_target_clears_the_zone_selection_and_announces_it()
    {
        var (kit, session, player, rec) = await SetupAsync();
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, default));
        Assert.Equal(MobId, session.SelectedTargetId);

        Assert.Null(await kit.World.TryCancelTargetAsync(session, default));

        Assert.Equal(0, session.SelectedTargetId);
        Assert.Equal(0, session.AutoAttackTargetId);
        var unselected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetUnselected)));
        Assert.Equal(MobId, (int)unselected[1]!);

        // A second cancel has nothing to unselect and must not upset the client with a stale id.
        Assert.Null(await kit.World.TryCancelTargetAsync(session, default));
        var again = rec.Of(nameof(IWorldEventPublisher.FanoutTargetUnselected)).Last();
        Assert.Equal(0, (int)again[1]!);
    }

    /// <summary>Cancelling also stops the auto-attack, or the next swing would re-target the dropped mob.</summary>
    [Fact]
    public async Task Cancelling_the_target_stops_the_running_auto_attack()
    {
        var (kit, session, player, rec) = await SetupAsync();

        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, default));
        session.AutoAttackTargetId = MobId;

        Assert.Null(await kit.World.TryCancelTargetAsync(session, default));

        Assert.Equal(0, session.AutoAttackTargetId);
    }

    /// <summary>
    /// Mobius NpcAction still <c>setTarget</c>s a corpse; only the second click (attack) checks AlikeDead.
    /// Rejecting dead NPCs caused <c>[Net] zone reject target</c> when clicking a kill for spoil/look.
    /// </summary>
    [Fact]
    public async Task A_dead_npc_is_still_selectable()
    {
        var (kit, session, player, rec) = await SetupAsync();
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.CurHp = 0;
        mob.Dead = true;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, default));

        Assert.Equal(MobId, session.SelectedTargetId);
        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(MobId, (int)selected[1]!);
        var hp = Assert.IsType<StatusUpdateMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)))[1]);
        Assert.Equal(0, hp.Values[Array.IndexOf(hp.AttributeIds, InterludeStatus.CurHp)]);
    }

    /// <summary>
    /// Mobius ItemAction targets ground drops. SelectTarget for a bag drop must not Reject —
    /// that was the spam after inventory drop when the client fell through to SelectTarget.
    /// </summary>
    [Fact]
    public async Task A_ground_item_is_selectable_with_colour_zero_and_no_hp_block()
    {
        var (kit, session, player, rec) = await SetupAsync();
        const int adena = 57;
        player.Inventory!.Add(adena, 1000);
        var stack = player.Inventory.FindByItemId(adena)!;
        Assert.Null(await kit.World.TryDropItemAsync(
            session, stack.ObjectId, 100, player.X + 40, player.Y, player.Z, default));
        int groundId = ((DropItemMsg)Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDropItem)))[1]!).ObjectId;
        Assert.Contains(groundId, player.Knownlist);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySelectTargetAsync(session, groundId, default));

        Assert.Equal(groundId, session.SelectedTargetId);
        var selected = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
        Assert.Equal(groundId, (int)selected[1]!);
        Assert.Equal(0, (int)selected[2]!);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)));
    }

    /// <summary>Mobius Action on a missing objectId is ActionFailed — never a protocol Reject.</summary>
    [Fact]
    public async Task A_missing_object_is_a_soft_miss_not_a_reject()
    {
        var (kit, session, player, rec) = await SetupAsync();
        session.SelectedTargetId = MobId;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, 9_999_999, default));

        Assert.Equal(MobId, session.SelectedTargetId);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutTargetSelected)));
    }
}
