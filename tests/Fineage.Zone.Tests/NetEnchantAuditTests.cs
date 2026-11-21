using Fineage.Accounts;
using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Gaps closed by the F1 enchant audit against Mobius <c>RequestEnchantItem</c> / <c>EnchantScrolls</c>:
/// the +6 armor / +7 / +15 weapon broadcast with the Firework cast, the weapon glow in CharInfo, one scroll at a
/// time, no enchanting from a private store, and the "Inappropriate enchant conditions" refusal.
/// </summary>
public sealed class NetEnchantAuditTests
{
    const int Saber = 123;        // D one-hand sword
    const int Necklace = 911;     // D jewelry, an armor piece for Mobius isArmor
    const int NormalD = 955, ArmorD = 956, NormalC = 951;
    const int RHand = (int)PaperdollSlot.RHand;

    static List<(IReadOnlyList<int> To, SystemMessageMsg Line)> Lines(WorldBench bench) =>
        bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Select(a => ((IReadOnlyList<int>)a[0]!, (SystemMessageMsg)a[1]!)).ToList();

    static List<(IReadOnlyList<int> To, MagicSkillUseMsg Cast)> Casts(WorldBench bench) =>
        bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse))
            .Select(a => ((IReadOnlyList<int>)a[0]!, (MagicSkillUseMsg)a[1]!)).ToList();

    static async Task<string?> EnchantAsync(WorldBench bench, BenchPlayer p, int scrollId, int targetObjectId)
    {
        var scroll = p.Inventory.FindByItemId(scrollId)!;
        string? request = await bench.World.TryEnchantRequestAsync(p.Session, scroll.ObjectId, default);
        if (request != null)
            return request;
        return await bench.World.TryEnchantConfirmAsync(p.Session, targetObjectId, default);
    }

    [Fact]
    public async Task Weapon_plus_seven_is_announced_with_a_firework_and_the_glow_reaches_watchers()
    {
        var bench = new WorldBench(enchantRandom: new UnitSamples(0.0));
        var smith = await bench.EnterAsync("smith", "Smith",
            [new StoredItem(Saber, 1, RHand, 6), new StoredItem(NormalD, 1, -1)], clearRecorder: false);
        var watcher = await bench.EnterAsync("watch", "Watcher", x: 46045 + 50, clearRecorder: false);
        Assert.Contains(watcher.ObjectId, smith.Player.Knownlist);
        bench.Rec.Calls.Clear();

        int saber = smith.Slot(Saber);
        Assert.Null(await EnchantAsync(bench, smith, NormalD, saber));
        Assert.Equal(7, smith.Inventory.Find(saber)!.EnchantLevel);

        var announce = Assert.Single(Lines(bench), l => l.Line.Text.Contains("has successfully enchanted a +7"));
        Assert.Equal("Smith has successfully enchanted a +7 Saber.", announce.Line.Text);
        Assert.Contains(smith.ObjectId, announce.To);
        Assert.Contains(watcher.ObjectId, announce.To);
        var firework = Assert.Single(Casts(bench));
        Assert.Equal(EnchantProtocol.FireworkSkillId, firework.Cast.SkillId);
        Assert.Contains(watcher.ObjectId, firework.To);

        // Mobius broadcastUserInfo → CharInfo; the remote pawn carries getEnchantEffect for the glow.
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCharInfo)),
            a => ((IReadOnlyList<int>)a[0]!).Contains(watcher.ObjectId) && (int)a[1]! == smith.ObjectId);
        var spawn = NetEnterWorldProjector.BuildRemoteSpawn(smith.Player, EntityId.FromObjectId(smith.ObjectId, 1));
        Assert.Equal(7, NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn)).WeaponEnchant);
    }

    [Theory]
    [InlineData(Saber, NormalD, 5, false)]   // weapon +6: quiet
    [InlineData(Saber, NormalD, 14, true)]   // weapon +15: announced
    [InlineData(Necklace, ArmorD, 5, true)]  // armor / jewelry +6: announced
    [InlineData(Necklace, ArmorD, 6, false)] // armor +7: quiet
    public async Task Only_the_mobius_milestones_are_announced(int itemId, int scrollId, int from, bool announced)
    {
        var bench = new WorldBench(enchantRandom: new UnitSamples(0.0));
        var smith = await bench.EnterAsync("ann", "Annie", [new StoredItem(itemId, 1, -1, from), new StoredItem(scrollId, 1, -1)]);
        int target = smith.Slot(itemId);
        Assert.Null(await EnchantAsync(bench, smith, scrollId, target));
        Assert.Equal(from + 1, smith.Inventory.Find(target)!.EnchantLevel);
        Assert.Equal(announced, Lines(bench).Any(l => l.Line.Text.Contains("has successfully enchanted")));
        Assert.Equal(announced, Casts(bench).Any(c => c.Cast.SkillId == EnchantProtocol.FireworkSkillId));
    }

    [Fact]
    public async Task A_second_scroll_waits_for_the_first_and_the_same_scroll_may_be_picked_again()
    {
        var bench = new WorldBench(enchantRandom: new UnitSamples(0.0));
        var smith = await bench.EnterAsync("two", "Twice", [new StoredItem(NormalD, 1, -1), new StoredItem(ArmorD, 1, -1)]);
        int weapon = smith.Slot(NormalD), armor = smith.Slot(ArmorD);
        Assert.Null(await bench.World.TryEnchantRequestAsync(smith.Session, weapon, default));
        Assert.Null(await bench.World.TryEnchantRequestAsync(smith.Session, weapon, default));
        Assert.Equal(EnchantProtocol.Busy, await bench.World.TryEnchantRequestAsync(smith.Session, armor, default));
        Assert.Contains(Lines(bench), l => l.Line.Text.StartsWith("Another enchantment is in progress"));
        Assert.Null(await bench.World.TryEnchantCancelAsync(smith.Session, default));
        Assert.Null(await bench.World.TryEnchantRequestAsync(smith.Session, armor, default));
    }

    [Fact]
    public async Task No_enchanting_from_a_private_store_and_the_scroll_stays()
    {
        var bench = new WorldBench(enchantRandom: new UnitSamples(0.0));
        var smith = await bench.EnterAsync("shop", "Shopper", [new StoredItem(Saber, 1, -1), new StoredItem(NormalD, 1, -1)]);
        Assert.Null(await bench.World.TryEnchantRequestAsync(smith.Session, smith.Slot(NormalD), default));
        smith.Player.PrivateStoreType = PrivateStoreProtocol.TypeSell;
        Assert.Equal(EnchantProtocol.Store, await bench.World.TryEnchantConfirmAsync(smith.Session, smith.Slot(Saber), default));
        Assert.Equal(1, smith.Inventory.CountOf(NormalD));
        Assert.Equal(0, smith.Inventory.Find(smith.Slot(Saber))!.EnchantLevel);
        Assert.Equal(EnchantProtocol.ResultCancel,
            Assert.IsType<EnchantResultMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutEnchantResult)).Last()[1]).Result);
        Assert.Equal(EnchantProtocol.StoreOpen, Lines(bench).Last().Line.Id);
        Assert.Equal(0, smith.Player.ActiveEnchantObjectId);
    }

    [Fact]
    public async Task A_wrong_grade_closes_the_window_with_inappropriate_conditions()
    {
        var bench = new WorldBench(enchantRandom: new UnitSamples(0.0));
        var smith = await bench.EnterAsync("grade", "Grader", [new StoredItem(Saber, 1, -1), new StoredItem(NormalC, 1, -1)]);
        Assert.Equal(EnchantProtocol.GradeMismatch, await EnchantAsync(bench, smith, NormalC, smith.Slot(Saber)));
        Assert.Equal(1, smith.Inventory.CountOf(NormalC));
        Assert.Equal(EnchantProtocol.ResultCancel,
            Assert.IsType<EnchantResultMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutEnchantResult)).Last()[1]).Result);
        Assert.Equal(EnchantProtocol.InappropriateConditions, Lines(bench).Last().Line.Id);
    }

    [Fact]
    public void Weapon_glow_rides_an_optional_spawn_tail()
    {
        var full = NetCodec.EncodeEntitySpawn(new EntitySpawnMsg { Name = "A", Kind = EntityKindNet.Player, WeaponEnchant = 16 });
        Assert.Equal(16, NetCodec.DecodeEntitySpawn(full).WeaponEnchant);
        // An old server stops after PledgeClass (glow int, noble/hero bytes, summon tail, cubic list gone): no glow.
        Assert.Equal(0, NetCodec.DecodeEntitySpawn(full.AsSpan(0, full.Length - 4 - 2 - 5 - 2 - 1 - 8)).WeaponEnchant);
    }

    [Fact]
    public async Task Glow_follows_the_right_hand_weapon_only_and_caps_at_127()
    {
        var bench = new WorldBench();
        var p = await bench.EnterAsync("glow", "Glow", [new StoredItem(Saber, 1, RHand, 200), new StoredItem(Necklace, 1, (int)PaperdollSlot.Neck, 9)]);
        Assert.Equal(127, NetEnterWorldProjector.BuildRemoteSpawn(p.Player, EntityId.FromObjectId(p.ObjectId, 1)).WeaponEnchant);
        Assert.Null(await p.Unequip(RHand));
        Assert.Equal(0, NetEnterWorldProjector.BuildRemoteSpawn(p.Player, EntityId.FromObjectId(p.ObjectId, 1)).WeaponEnchant);
    }
}
