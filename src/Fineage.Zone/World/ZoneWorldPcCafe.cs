using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.PcCafe;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    PcCafePointsManager _pcCafe = null!;
    PcCafeCouponCatalog _pcCafeCoupons = PcCafeCouponCatalog.Empty;

    void InitPcCafe(PcCafePointsManager? manager, PcCafeCouponCatalog? coupons)
    {
        _pcCafe = manager ?? new PcCafePointsManager(Path.Combine(Path.GetTempPath(), "fineage-pc-cafe-test"));
        _pcCafeCoupons = coupons ?? PcCafeCouponCatalog.LoadDefault();
    }

    public void UsePcCafe(PcCafePointsManager manager, PcCafeCouponCatalog? coupons = null)
    {
        _pcCafe = manager ?? throw new ArgumentNullException(nameof(manager));
        if (coupons != null)
            _pcCafeCoupons = coupons;
    }

    internal PcCafePointsManager PcCafe => _pcCafe;

    /// <summary>Mobius <c>RequestPCCafeCouponUse</c>.</summary>
    public Task<string?> TryPcCafeCouponAsync(ZoneSession session, string code, CancellationToken ct)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player || player.Inventory == null)
            return Task.FromResult<string?>("world");

        code = (code ?? "").Trim();
        if (code.Length == 0)
            return Task.FromResult<string?>("bad_request");

        if (!_pcCafeCoupons.TryGet(code, out var def))
        {
            ZoneLog.Warn($"[pc-cafe] unknown coupon code='{code}' characterId={player.ObjectId}");
            return Task.FromResult<string?>(null);
        }

        if (_pcCafe.WasRedeemed(player.ObjectId, code))
        {
            ZoneLog.Info($"pc-cafe coupon already used object={player.ObjectId} code={code}");
            FanPcCafePoints(session, player.ObjectId, pointsToAdd: 0, time: 0);
            return Task.FromResult<string?>(null);
        }

        foreach (var item in def.Items)
        {
            if (!_game.Items.TryGet(item.ItemId, out _))
            {
                ZoneLog.Error($"[pc-cafe] coupon {code} missing item template id={item.ItemId}");
                return Task.FromResult<string?>("bad_request");
            }
        }

        foreach (var item in def.Items)
        {
            int itemId = item.ItemId;
            int count = item.Count;
            RecordBagChange(
                player,
                LedgerTxKind.PcCafeCoupon,
                LedgerAccount.Of(SystemAccount.PcCafe),
                () =>
                {
                    GiveItem(session, player.Inventory!, itemId, count);
                    return true;
                },
                _ => itemId);
        }

        int applied = 0;
        if (def.Points > 0)
            applied = _pcCafe.AddPoints(player.ObjectId, def.Points);

        _pcCafe.MarkRedeemed(player.ObjectId, code);
        FanPcCafePoints(session, player.ObjectId, applied, time: 0);
        ZoneLog.Info($"pc-cafe coupon ok object={player.ObjectId} code={code} points=+{applied}");
        return Task.FromResult<string?>(null);
    }

    /// <summary>Test / admin accrual seam (Mobius <c>giveRetailPcCafePont</c> / <c>givePcCafePoint</c>).</summary>
    public int GrantPcCafePoints(int characterId, int points)
    {
        int applied = _pcCafe.AddPoints(characterId, points);
        if (applied != 0 && _byId.TryGetValue(characterId, out var player) && player.Session != null)
            FanPcCafePoints(player.Session, characterId, applied, time: 1);
        return applied;
    }

    void FanPcCafePoints(ZoneSession session, int characterId, int pointsToAdd, int time)
    {
        int balance = _pcCafe.PointsOf(characterId);
        NetBridge?.FanoutPcCafePointInfo([session.ObjectId], new PcCafePointInfoMsg
        {
            Points = balance,
            PointsToAdd = pointsToAdd,
            PeriodType = pointsToAdd != 0 ? 1 : 0,
            RemainTime = PcCafeProtocol.RemainTimeRetail,
            PointType = pointsToAdd < 0 ? 3 : 0,
            Time = time
        });
    }

    void SendPcCafeOnEnter(ZoneSession session) =>
        FanPcCafePoints(session, session.ObjectId, pointsToAdd: 0, time: 0);
}
