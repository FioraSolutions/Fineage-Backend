using Fineage.Zone.Persistence;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.PcCafe;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius PC Cafe points / coupon redeem (#933).</summary>
public sealed class PcCafeTests
{
    const string CouponCode = "FINEAGE-TEST-ADENA";

    static PcCafeCouponCatalog TestCatalog() => PcCafeCouponCatalog.Parse(
        """
        {
          "coupons": [
            {
              "code": "FINEAGE-TEST-ADENA",
              "points": 100,
              "items": [ { "itemId": 57, "count": 1000 } ]
            }
          ]
        }
        """);

    static (WorldBench Kit, string Dir) BenchWithPcCafe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-pc-cafe-" + Guid.NewGuid().ToString("N"));
        var kit = new WorldBench();
        kit.World.UsePcCafe(new PcCafePointsManager(new StoredPcCafeStore(kit.World.WorldState)), TestCatalog());
        return (kit, dir);
    }

    [Fact]
    public async Task Enter_starts_at_zero_points()
    {
        var (kit, dir) = BenchWithPcCafe();
        try
        {
            // WorldBench clears enter fanouts after TryEnterAsync; balance is the enter-state check.
            BenchPlayer p = await kit.EnterAsync("cafe", "Cafe");
            Assert.Equal(0, kit.World.PcCafe.PointsOf(p.ObjectId));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task Grant_points_caps_at_max_and_fans_info()
    {
        var (kit, dir) = BenchWithPcCafe();
        try
        {
            BenchPlayer p = await kit.EnterAsync("cafe2", "CafeTwo");
            kit.Rec.Calls.Clear();
            Assert.Equal(50, kit.World.GrantPcCafePoints(p.ObjectId, 50));
            Assert.Equal(50, kit.World.PcCafe.PointsOf(p.ObjectId));

            var info = Assert.Single(
                kit.Rec.Of(nameof(IWorldEventPublisher.FanoutPcCafePointInfo))
                    .Select(a => Assert.IsType<PcCafePointInfoMsg>(a[1])));
            Assert.Equal(50, info.Points);
            Assert.Equal(50, info.PointsToAdd);
            Assert.Equal(1, info.PeriodType);

            Assert.Equal(PcCafeProtocol.MaxPoints - 50,
                kit.World.GrantPcCafePoints(p.ObjectId, PcCafeProtocol.MaxPoints));
            Assert.Equal(PcCafeProtocol.MaxPoints, kit.World.PcCafe.PointsOf(p.ObjectId));
            Assert.Equal(0, kit.World.GrantPcCafePoints(p.ObjectId, 10));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task Coupon_redeems_items_through_ledger_and_grants_points()
    {
        var (kit, dir) = BenchWithPcCafe();
        try
        {
            BenchPlayer p = await kit.EnterAsync("cafe3", "CafeThree");
            int before = p.Player.Inventory!.CountOf(57);
            kit.Rec.Calls.Clear();

            Assert.Null(await kit.World.TryPcCafeCouponAsync(p.Session, CouponCode, CancellationToken.None));
            Assert.Equal(before + 1000, p.Player.Inventory.CountOf(57));
            Assert.Equal(100, kit.World.PcCafe.PointsOf(p.ObjectId));
            Assert.True(kit.World.PcCafe.WasRedeemed(p.ObjectId, CouponCode));

            Assert.Contains(
                kit.Ledger.Entries,
                e => e.Transaction.Kind == LedgerTxKind.PcCafeCoupon && e.Transaction.Reference == 57);

            var info = Assert.Single(
                kit.Rec.Of(nameof(IWorldEventPublisher.FanoutPcCafePointInfo))
                    .Select(a => Assert.IsType<PcCafePointInfoMsg>(a[1])));
            Assert.Equal(100, info.Points);
            Assert.Equal(100, info.PointsToAdd);

            // Second redeem is a no-op (already used).
            Assert.Null(await kit.World.TryPcCafeCouponAsync(p.Session, CouponCode, CancellationToken.None));
            Assert.Equal(before + 1000, p.Player.Inventory.CountOf(57));
            Assert.Equal(100, kit.World.PcCafe.PointsOf(p.ObjectId));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task Unknown_coupon_is_logged_and_grants_nothing()
    {
        var (kit, dir) = BenchWithPcCafe();
        try
        {
            BenchPlayer p = await kit.EnterAsync("cafe4", "CafeFour");
            int before = p.Player.Inventory!.CountOf(57);
            kit.Rec.Calls.Clear();

            Assert.Null(await kit.World.TryPcCafeCouponAsync(p.Session, "NO-SUCH-CODE", CancellationToken.None));
            Assert.Equal(before, p.Player.Inventory.CountOf(57));
            Assert.Equal(0, kit.World.PcCafe.PointsOf(p.ObjectId));
            Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutPcCafePointInfo)));
            Assert.DoesNotContain(kit.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.PcCafeCoupon);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Codec_roundtrips_point_info_and_coupon()
    {
        var info = new Fineage.Net.Contract.PcCafePointInfoNetMsg
        {
            Points = 1234,
            PointsToAdd = 10,
            PeriodType = 1,
            RemainTime = 42,
            PointType = 0,
            Time = 5
        };
        var decoded = Fineage.Net.Contract.NetCodec.DecodePcCafePointInfo(
            Fineage.Net.Contract.NetCodec.EncodePcCafePointInfo(info));
        Assert.Equal(info.Points, decoded.Points);
        Assert.Equal(info.PointsToAdd, decoded.PointsToAdd);
        Assert.Equal(info.PeriodType, decoded.PeriodType);
        Assert.Equal(info.RemainTime, decoded.RemainTime);
        Assert.Equal(info.PointType, decoded.PointType);
        Assert.Equal(info.Time, decoded.Time);

        var coupon = Fineage.Net.Contract.NetCodec.DecodePCCafeCouponUse(
            Fineage.Net.Contract.NetCodec.EncodePCCafeCouponUse(
                new Fineage.Net.Contract.PCCafeCouponUseNetMsg { Code = CouponCode }));
        Assert.Equal(CouponCode, coupon.Code);
    }

    static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
