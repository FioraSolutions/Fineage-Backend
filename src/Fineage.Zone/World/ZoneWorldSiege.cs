using Fineage.Protocol;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public SiegeBoard Sieges => _siege.Board;

    /// <summary>
    /// Admin and test hook. The clan must be an attacker on the one siege that is in progress.
    /// </summary>
    public string? CaptureCastle(int clanId, DateTimeOffset? now = null)
    {
        return CaptureLocked(clanId, 0, (now ?? WallNow()).ToUnixTimeMilliseconds());
    }

    public SiegeStartKind StartSiege(int castleId, DateTimeOffset? now = null)
    {
        long ms = (now ?? WallNow()).ToUnixTimeMilliseconds();
        return StartSiegeLocked(castleId, ms);
    }

    public string? EndSiege(int castleId, DateTimeOffset? now = null)
    {
        long ms = (now ?? WallNow()).ToUnixTimeMilliseconds();
        string? error = _siege.Board.EndSiege(castleId, ms);
        if (error == null)
        {
            SyncSiegeRuntime(ms);
            PersistSiege();
        }
        return error;
    }

    public Task<string?> TrySiegeRegisterAsync(ZoneSession session, SiegeRegisterRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        long now = NowUnixMs();
        AnnounceClosed(now);
        bool joining = request.Joining == 1;
        bool attacker = request.Attacker == 1;
        var clan = ClanInfoFor(session.ObjectId);
        error = joining
            ? _siege.Board.Register(new SiegeRequester(session.ObjectId), clan, request.CastleId, attacker, now)
            : _siege.Board.Unregister(new SiegeRequester(session.ObjectId), clan, request.CastleId, now);
        if (error == null)
        {
            PersistSiege();
            var castle = _siege.Board.Find(request.CastleId);
            string name = castle?.Name ?? "";
            var line = joining
                ? attacker ? SiegeLines.RegisteredAttacker(name) : SiegeLines.RegisteredDefender(name)
                : SiegeLines.Unregistered(name);
            FanSystemLine(session, line);
            QueueSiegeInfo(session, request.CastleId, now);
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TrySiegeAnswerAsync(ZoneSession session, SiegeDefenderAnswerRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        long now = NowUnixMs();
        AnnounceClosed(now);
        error = _siege.Board.AnswerDefender(
            new SiegeRequester(session.ObjectId),
            ClanInfoFor(session.ObjectId),
            request.CastleId,
            request.ClanId,
            request.Approved,
            now);
        if (error == null)
        {
            PersistSiege();
            string name = _siege.Board.Find(request.CastleId)?.Name ?? "";
            var line = request.Approved == 1
                ? SiegeLines.DefenderApproved(name)
                : SiegeLines.DefenderRefused(name);
            FanSystemLine(session, line);
            QueueSiegeInfo(session, request.CastleId, now);
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TrySiegeListAsync(ZoneSession session, int castleId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        long now = NowUnixMs();
        AnnounceClosed(now);
        if (_siege.Board.List(castleId, now) == null)
            error = SiegeProtocol.Castle;
        else
        {
            error = null;
            QueueSiegeInfo(session, castleId, now);
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAdminCaptureAsync(ZoneSession session, int castleId, int clanId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error = CaptureLocked(clanId, castleId, NowUnixMs(), session);

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

}
