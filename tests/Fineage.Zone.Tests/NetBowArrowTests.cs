using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius bow ammunition (audit <c>D12</c>): <c>Creature.canUseRangeWeapon</c> equips an arrow of the bow's grade from
/// the bag or refuses the shot with SysMsg 112, and <c>doAttackHitByBow</c> burns one arrow per shot, hit or miss.
/// </summary>
public sealed class NetBowArrowTests
{
    const int MobId = 9_400_001;
    const int ShortBow = 13;
    const int WoodenArrow = 17;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["bow"] = new("bow", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 1, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync(int arrows)
    {
        var kit = new NetTestKit(Map(), "bow");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        var bag = player.Inventory!;
        var bow = bag.Add(ShortBow);
        bag.Equip(bow);
        if (arrows > 0)
            bag.Add(WoodenArrow, arrows);
        Assert.True(bag.GetEquipped(PaperdollSlot.RHand)?.ItemId == ShortBow);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, player, rec);
    }

    static bool RanOutOfArrows(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Any(m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgRunOutOfArrows);

    [Fact]
    public async Task Bow_without_arrows_refuses_the_shot()
    {
        var (kit, session, player, rec) = await SetupAsync(arrows: 0);

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        Assert.True(RanOutOfArrows(rec));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Null(player.Inventory!.GetEquipped(PaperdollSlot.LHand));
    }

    [Fact]
    public async Task Arrow_is_equipped_from_the_bag_on_the_first_shot()
    {
        var (kit, session, player, rec) = await SetupAsync(arrows: 3);

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        // checkAndEquipArrows moved one arrow into the left hand (paperdoll change fanned as InventoryUpdate).
        Assert.Equal(WoodenArrow, player.Inventory!.GetEquipped(PaperdollSlot.LHand)?.ItemId);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)), u =>
            ((InventoryUpdateNetMsg)u[1]!).Changes.Length is 1 or 2);
    }

    [Fact]
    public async Task Each_shot_spends_exactly_one_arrow()
    {
        var (kit, session, player, rec) = await SetupAsync(arrows: 3);

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        var arrow = player.Inventory!.GetEquipped(PaperdollSlot.LHand);
        Assert.NotNull(arrow);
        Assert.Equal(2, arrow!.Count);

        // Free the attack timer between swings (Mobius Pspd / bow reuse).
        session.NextAutoAttackUnixMs = 0;
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.Equal(1, player.Inventory.GetEquipped(PaperdollSlot.LHand)?.Count);

        session.NextAutoAttackUnixMs = 0;
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        // The last arrow destroys the stack and frees the hand: ItemList is re-sent, the next shot is refused.
        Assert.Null(player.Inventory.GetEquipped(PaperdollSlot.LHand));
        Assert.DoesNotContain(player.Inventory.Items, i => i.ItemId == WoodenArrow);

        session.NextAutoAttackUnixMs = 0;
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.True(RanOutOfArrows(rec));
    }

    [Fact]
    public async Task Wrong_grade_arrow_does_not_load_the_bow()
    {
        var kit = new NetTestKit(Map(), "bow");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        var bag = player.Inventory!;
        bag.Equip(bag.Add(271));            // Hunting Bow (no grade)
        bag.Add(1341, 5);                   // Bone Arrow (D grade)
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        Assert.True(RanOutOfArrows(rec));
        Assert.Null(bag.GetEquipped(PaperdollSlot.LHand));
        Assert.Equal(5, bag.Items.Single(i => i.ItemId == 1341).Count);
    }

    [Fact]
    public async Task Melee_weapon_never_consumes_arrows()
    {
        var kit = new NetTestKit(Map(), "bow");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        var bag = player.Inventory!;
        bag.Add(WoodenArrow, 5);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        Assert.False(RanOutOfArrows(rec));
        Assert.Equal(5, bag.Items.Single(i => i.ItemId == WoodenArrow).Count);
        Assert.Null(bag.GetEquipped(PaperdollSlot.LHand));
    }
}
