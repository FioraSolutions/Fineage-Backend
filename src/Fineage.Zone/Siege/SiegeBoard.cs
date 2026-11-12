using Fineage.Accounts;
using Fineage.Protocol;

namespace Fineage.Zone.Siege;

/// <summary>
/// Castle catalog, registration, and the siege lifecycle. Combat does not live here.
/// <see cref="CaptureCastle(int, long)"/> is the admin and test hook that transfers ownership.
/// </summary>
public sealed partial class SiegeBoard
{
    readonly List<CastleSiege> _castles = [];

    public SiegeBoard(long nowUnixMs, SiegeSettings? settings = null)
    {
        Settings = settings ?? SiegeSettings.Mobius;
        foreach (var template in CastleCatalog.Interlude)
            _castles.Add(new CastleSiege(template));
        ScheduleAll(nowUnixMs);
    }

    public SiegeSettings Settings { get; }

    public IReadOnlyList<CastleSiege> Castles => _castles;

    public CastleSiege? Find(int castleId)
    {
        foreach (var castle in _castles)
        {
            if (castle.Id == castleId)
                return castle;
        }

        return null;
    }

    public int CastleOwnedBy(int clanId)
    {
        if (clanId <= 0)
            return 0;
        foreach (var castle in _castles)
        {
            if (castle.OwnerClanId == clanId)
                return castle.Id;
        }

        return 0;
    }

    /// <summary>
    /// Closes clan registration inside the 24 hour window and drops defender requests that were never approved.
    /// Returns castle ids whose window closed on this call.
    /// </summary>
    public List<int> Refresh(long nowUnixMs)
    {
        var closed = new List<int>();
        foreach (var castle in _castles)
        {
            if (castle.InProgress)
            {
                castle.RegistrationClosed = true;
                continue;
            }

            bool timeClosed = !castle.ScheduleAccepted || WindowClosed(castle, nowUnixMs);
            if (timeClosed && !castle.RegistrationClosed)
                closed.Add(castle.Id);
            if (timeClosed)
                ClearPending(castle);
            castle.RegistrationClosed = timeClosed;
        }

        return closed;
    }

    public void Restore(IReadOnlyList<CastleSave> castles, IReadOnlyList<SiegeClanSave> clans, long nowUnixMs)
    {
        foreach (var saved in castles)
        {
            var castle = Find(saved.Id);
            if (castle == null)
                continue;
            castle.OwnerClanId = saved.OwnerClanId;
            castle.TaxPercent = saved.TaxPercent;
            castle.SiegeUnixMs = saved.SiegeUnixMs;
            castle.ScheduleAccepted = saved.ScheduleAccepted;
            castle.InProgress = saved.InProgress;
            castle.EndsUnixMs = saved.EndsUnixMs;
            castle.RegistrationClosed = saved.RegistrationClosed;
            castle.Registrations.Clear();
        }

        foreach (var row in clans)
        {
            var castle = Find(row.CastleId);
            if (castle == null || row.ClanId <= 0)
                continue;
            if (row.Side is not (SiegeProtocol.Defender or SiegeProtocol.Attacker or SiegeProtocol.DefenderPending))
                continue;
            if (castle.Registrations.Any(existing => existing.ClanId == row.ClanId))
                continue;
            castle.Registrations.Add(new SiegeRegistration(row.ClanId, row.Side));
        }

        foreach (var castle in _castles)
        {
            if (castle.SiegeUnixMs == 0)
                ScheduleOne(castle, nowUnixMs);
            else if (!castle.InProgress && castle.SiegeUnixMs <= nowUnixMs)
                ScheduleOne(castle, nowUnixMs);
            else if (castle.InProgress && castle.EndsUnixMs != 0 && castle.EndsUnixMs <= nowUnixMs)
                Finish(castle, nowUnixMs);
        }

        Refresh(nowUnixMs);
    }

    public bool TryAdoptOwner(int castleId, int clanId)
    {
        var castle = Find(castleId);
        if (castle == null || clanId <= 0 || castle.OwnerClanId != 0 || CastleOwnedBy(clanId) != 0)
            return false;
        castle.OwnerClanId = clanId;
        return true;
    }
    public List<CastleSave> ExportCastles() => _castles.Select(castle => new CastleSave(
        castle.Id,
        castle.OwnerClanId,
        castle.TaxPercent,
        castle.SiegeUnixMs,
        castle.RegistrationClosed,
        castle.ScheduleAccepted,
        castle.InProgress,
        castle.EndsUnixMs)).ToList();

    public List<SiegeClanSave> ExportClans()
    {
        var rows = new List<SiegeClanSave>();
        foreach (var castle in _castles)
        {
            foreach (var row in castle.Registrations)
                rows.Add(new SiegeClanSave(castle.Id, row.ClanId, row.Side));
        }

        return rows;
    }

    internal void ScheduleAll(long nowUnixMs)
    {
        foreach (var castle in _castles)
            castle.ScheduleAccepted = false;
        foreach (var castle in _castles)
            ScheduleOne(castle, nowUnixMs);
        Refresh(nowUnixMs);
    }
}
