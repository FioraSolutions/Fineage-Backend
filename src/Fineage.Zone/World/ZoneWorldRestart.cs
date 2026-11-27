using Fineage.Accounts;
using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Null when the player may leave for the lobby. A code names the gate that refused.
    /// A session with no account is logged and refused. A session already out of the world may still
    /// receive the roster.
    /// </summary>
    internal async Task<string?> RestartRefusalAsync(ZoneSession session, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.Account))
        {
            ZoneLog.Error("[restart] session has no account");
            return RestartProtocol.NoAccount;
        }
        if (!session.InWorld)
            return null;

        return await OnSimulationAsync(session, "restart", () =>
        {
            if (!TryGetLivePlayer(session, out var player))
            {
                ZoneLog.Error($"[restart] account {session.Account} is in the world with no live player");
                return RestartProtocol.NotInWorld;
            }

            long now = NowUnixMs();
            bool festival = PlayerInFestivalArena(player.ObjectId)
                && (_festivalRuntime.Board.GlobalFestivalActive || _festivalRuntime.Board.FestivalSliceActive(now));
            return RestartRules.Refuse(new RestartFacts(
                Enchanting: player.ActiveEnchantObjectId != 0,
                StoreOpen: player.PrivateStoreType != PrivateStoreProtocol.TypeNone,
                InCombat: player.CombatUntilUnixMs > now,
                Festival: festival));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The account's character rows after the world leave has persisted. An empty list is logged
    /// with the account id. A missing account store is logged and returns no rows.
    /// </summary>
    internal LobbyCharacterRow[] RosterAfterRestart(ZoneSession session)
    {
        if (string.IsNullOrEmpty(session.Account))
        {
            ZoneLog.Error("[restart] roster requested with no account");
            return [];
        }
        if (_accounts == null)
        {
            ZoneLog.Error($"[restart] account store is missing for account {session.Account}");
            return [];
        }

        var list = _accounts.List(session.Account);
        if (list.Count == 0)
            ZoneLog.Error($"[restart] account {session.Account} has no character roster");
        return LobbyRoster.Rows(list, _clock.UtcNow);
    }
}
