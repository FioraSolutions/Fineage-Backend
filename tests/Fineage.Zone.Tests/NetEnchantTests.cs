using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius enchant window over Net: <c>RequestEnchantItem</c> picks the scroll, the confirm rolls against the
/// chosen target and answers with <c>EnchantResult</c> plus the bag <c>InventoryUpdate</c>, and cancel answers
/// with the cancel result. The roll itself is the same V2 code path, so these tests only assert the Net fanout.
/// </summary>
public sealed class NetEnchantTests
{
    // Saber is enchantable; 955 is the normal D weapon scroll from the V2 enchant tests.
    const int Saber = 123;
    const int NormalD = 955;

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, player, rec);
    }

    static EnchantResultMsg LastResult(NetBridgeRecorder rec) =>
        Assert.IsType<EnchantResultMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutEnchantResult)).Last()[1]);

    [Fact]
    public async Task Enchant_request_picks_the_scroll_and_confirm_fans_the_result()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var saber = player.Inventory!.Add(Saber, 1);
        var scroll = player.Inventory!.Add(NormalD, 1);

        Assert.Null(await kit.World.TryEnchantRequestAsync(session, scroll.ObjectId, default));
        Assert.Contains(
            rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            args => args[1] is SystemMessageMsg line && line.Id == EnchantProtocol.SelectItem);

        Assert.Null(await kit.World.TryEnchantConfirmAsync(session, saber.ObjectId, default));

        EnchantResultMsg result = LastResult(rec);
        Assert.Equal(saber.ObjectId, result.ObjectId);
        Assert.NotEqual(EnchantProtocol.ResultCancel, result.Result);
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)));
    }

    [Fact]
    public async Task Enchant_cancel_fans_the_cancel_result()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var scroll = player.Inventory!.Add(NormalD, 1);

        Assert.Null(await kit.World.TryEnchantRequestAsync(session, scroll.ObjectId, default));
        Assert.Null(await kit.World.TryEnchantCancelAsync(session, default));

        EnchantResultMsg result = LastResult(rec);
        Assert.Equal(EnchantProtocol.ResultCancel, result.Result);
        Assert.Equal(0, result.ObjectId);
    }

    [Fact]
    public async Task Enchant_confirm_without_a_request_is_refused_and_fans_nothing()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var saber = player.Inventory!.Add(Saber, 1);

        Assert.Equal(EnchantProtocol.NoRequest, await kit.World.TryEnchantConfirmAsync(session, saber.ObjectId, default));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutEnchantResult)));
    }

    [Fact]
    public async Task Enchant_request_on_a_non_scroll_is_refused_and_fans_nothing()
    {
        var (kit, session, player, rec) = await SetupAsync();
        var saber = player.Inventory!.Add(Saber, 1);

        Assert.Equal(EnchantProtocol.NotScroll, await kit.World.TryEnchantRequestAsync(session, saber.ObjectId, default));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutEnchantResult)));
    }
}
