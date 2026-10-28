using Fineage.Login;
using Fineage.Protocol;

namespace Fineage.Zone.Siege;

/// <summary>
/// Castle catalog, registration, and the siege lifecycle. Combat does not live here.
/// <see cref="CaptureCastle(int, long)"/> is the admin and test hook that transfers ownership.
/// </summary>
public sealed class SiegeBoard
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

    public string? Register(SiegeActor actor, SiegeClanInfo? clan, int castleId, bool asAttacker, long nowUnixMs)
    {
        Refresh(nowUnixMs);
        var castle = Find(castleId);
        if (castle == null)
            return SiegeProtocol.Castle;
        var block = SiegeRules.JoinBlock(Check(actor, clan, castle, asAttacker, nowUnixMs));
        if (block != null)
            return block;
        castle.Registrations.Add(new SiegeRegistration(
            clan!.Id,
            asAttacker ? SiegeProtocol.Attacker : SiegeProtocol.DefenderPending));
        return null;
    }

    public string? Unregister(SiegeActor actor, SiegeClanInfo? clan, int castleId, long nowUnixMs)
    {
        Refresh(nowUnixMs);
        var castle = Find(castleId);
        if (castle == null)
            return SiegeProtocol.Castle;
        bool owner = clan != null && castle.OwnerClanId == clan.Id;
        bool registered = clan != null && castle.Registrations.Any(row => row.ClanId == clan.Id);
        var block = SiegeRules.LeaveBlock(
            clan != null,
            clan != null && actor.CharacterId == clan.LeaderId,
            owner,
            registered,
            castle.RegistrationClosed,
            castle.InProgress);
        if (block != null)
            return block;
        castle.Registrations.RemoveAll(row => row.ClanId == clan!.Id);
        return null;
    }

    /// <summary>Mobius <c>RequestConfirmSiegeWaitingList</c>. <paramref name="approved"/> 1 accepts. Any other value refuses.</summary>
    public string? AnswerDefender(SiegeActor actor, SiegeClanInfo? clan, int castleId, int targetClanId, int approved, long nowUnixMs)
    {
        Refresh(nowUnixMs);
        var castle = Find(castleId);
        if (castle == null)
            return SiegeProtocol.Castle;
        if (clan == null)
            return SiegeProtocol.Clan;
        if (castle.OwnerClanId != clan.Id)
            return SiegeProtocol.NotOwner;
        if (actor.CharacterId != clan.LeaderId)
            return SiegeProtocol.Leader;
        if (castle.RegistrationClosed)
            return SiegeProtocol.Closed;
        if (castle.InProgress)
            return SiegeProtocol.Progress;

        var row = castle.Registrations.FirstOrDefault(entry => entry.ClanId == targetClanId);
        if (approved == 1)
        {
            if (row == null || row.Side != SiegeProtocol.DefenderPending)
                return SiegeProtocol.Pending;
            if (DefenderCount(castle) >= Settings.MaxDefenders)
                return SiegeProtocol.Defenders;
            row.Side = SiegeProtocol.Defender;
            return null;
        }

        if (row == null || row.Side is not (SiegeProtocol.Defender or SiegeProtocol.DefenderPending))
            return SiegeProtocol.None;
        castle.Registrations.Remove(row);
        return null;
    }

    public SiegeStartKind StartSiege(int castleId, long nowUnixMs)
    {
        Refresh(nowUnixMs);
        var castle = Find(castleId);
        if (castle == null)
            return SiegeStartKind.UnknownCastle;
        if (castle.InProgress)
            return SiegeStartKind.AlreadyRunning;
        if (!castle.Registrations.Any(row => row.Side == SiegeProtocol.Attacker))
        {
            ScheduleOne(castle, nowUnixMs);
            Refresh(nowUnixMs);
            return SiegeStartKind.Cancelled;
        }

        ClearPending(castle);
        castle.RegistrationClosed = true;
        castle.InProgress = true;
        castle.EndsUnixMs = nowUnixMs + Settings.LengthMinutes * 60_000L;
        return SiegeStartKind.Started;
    }

    public string? EndSiege(int castleId, long nowUnixMs)
    {
        var castle = Find(castleId);
        if (castle == null)
            return SiegeProtocol.Castle;
        if (!castle.InProgress)
            return SiegeProtocol.Idle;
        Finish(castle, nowUnixMs);
        return null;
    }

    /// <summary>
    /// Transfers the single in-progress siege this clan is attacking.
    /// Combat and artifact casting stay outside this method.
    /// </summary>
    public string? CaptureCastle(int clanId, long nowUnixMs) =>
        CaptureCastle(clanId, 0, nowUnixMs);

    public string? CaptureCastle(int clanId, int castleId, long nowUnixMs)
    {
        CastleSiege? castle;
        if (castleId == 0)
        {
            CastleSiege? match = null;
            foreach (var candidate in _castles)
            {
                if (!candidate.InProgress || !IsAttacker(candidate, clanId))
                    continue;
                if (match != null)
                    return SiegeProtocol.Castle;
                match = candidate;
            }

            if (match == null)
                return _castles.Any(candidate => candidate.InProgress) ? SiegeProtocol.NotAttacker : SiegeProtocol.Idle;
            castle = match;
        }
        else
        {
            castle = Find(castleId);
            if (castle == null)
                return SiegeProtocol.Castle;
            if (!castle.InProgress)
                return SiegeProtocol.Idle;
            if (!IsAttacker(castle, clanId))
                return SiegeProtocol.NotAttacker;
        }

        int previous = castle.OwnerClanId;
        var defenders = castle.Registrations.Where(row => row.Side == SiegeProtocol.Defender).Select(row => row.ClanId).ToList();
        int attackers = castle.Registrations.Count(row => row.Side == SiegeProtocol.Attacker);
        bool defendersEmpty = previous == 0 && defenders.Count == 0;

        RemoveClanEverywhere(clanId);
        foreach (var other in _castles)
        {
            if (other.OwnerClanId == clanId)
                other.OwnerClanId = 0;
        }

        castle.OwnerClanId = clanId;
        if (defendersEmpty && attackers == 1)
        {
            Finish(castle, nowUnixMs);
            return null;
        }

        foreach (int defender in defenders)
        {
            var row = castle.Registrations.FirstOrDefault(entry => entry.ClanId == defender);
            if (row != null)
                row.Side = SiegeProtocol.Attacker;
        }

        if (previous > 0 && previous != clanId && castle.Registrations.All(row => row.ClanId != previous))
            castle.Registrations.Add(new SiegeRegistration(previous, SiegeProtocol.Attacker));
        return null;
    }

    public void ReleaseClan(int clanId)
    {
        if (clanId <= 0)
            return;
        foreach (var castle in _castles)
        {
            if (castle.OwnerClanId == clanId)
                castle.OwnerClanId = 0;
            castle.Registrations.RemoveAll(row => row.ClanId == clanId);
        }
    }

    public SiegeList? List(int castleId, long nowUnixMs)
    {
        Refresh(nowUnixMs);
        var castle = Find(castleId);
        if (castle == null)
            return null;
        var rows = new List<SiegeListClan>();
        if (castle.OwnerClanId > 0)
            rows.Add(new SiegeListClan(castle.OwnerClanId, SiegeProtocol.Owner));
        foreach (var row in castle.Registrations.OrderBy(entry => entry.ClanId))
            rows.Add(new SiegeListClan(row.ClanId, row.Side));
        long deadline = castle.ScheduleAccepted ? castle.SiegeUnixMs - Settings.RegistrationCloseMillis : 0;
        return new SiegeList(
            castle.Id,
            castle.Name,
            castle.OwnerClanId,
            castle.TaxPercent,
            castle.SiegeUnixMs,
            deadline,
            castle.RegistrationClosed,
            castle.InProgress,
            castle.InProgress ? castle.EndsUnixMs : 0,
            rows);
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

    void ScheduleOne(CastleSiege castle, long nowUnixMs)
    {
        castle.InProgress = false;
        castle.EndsUnixMs = 0;
        long next = SiegeSchedule.NextFrom(castle.SiegeUnixMs, nowUnixMs, castle.Day, castle.Hour, Settings.CycleWeeks);
        int taken = 0;
        foreach (var other in _castles)
        {
            if (other.Id != castle.Id && other.ScheduleAccepted && other.SiegeUnixMs == next)
                taken++;
        }

        castle.SiegeUnixMs = next;
        castle.ScheduleAccepted = taken < castle.MaxConcurrent;
        castle.RegistrationClosed = !castle.ScheduleAccepted || WindowClosed(castle, nowUnixMs);
        if (castle.RegistrationClosed)
            ClearPending(castle);
    }

    void Finish(CastleSiege castle, long nowUnixMs)
    {
        castle.Registrations.Clear();
        ScheduleOne(castle, nowUnixMs);
        Refresh(nowUnixMs);
    }

    void RemoveClanEverywhere(int clanId)
    {
        foreach (var castle in _castles)
            castle.Registrations.RemoveAll(row => row.ClanId == clanId);
    }

    bool WindowClosed(CastleSiege castle, long nowUnixMs) =>
        SiegeRules.WindowClosed(castle.SiegeUnixMs, nowUnixMs, Settings.RegistrationCloseMillis);

    SiegeJoinCheck Check(SiegeActor actor, SiegeClanInfo? clan, CastleSiege castle, bool asAttacker, long nowUnixMs)
    {
        int owned = clan == null ? 0 : CastleOwnedBy(clan.Id);
        return new SiegeJoinCheck(
            clan != null,
            clan != null && actor.CharacterId == clan.LeaderId,
            clan != null && clan.DissolvingExpiryUnixMs > nowUnixMs,
            asAttacker,
            clan?.Id ?? 0,
            clan?.Level ?? 0,
            castle.OwnerClanId,
            owned,
            castle.RegistrationClosed,
            castle.InProgress,
            clan != null && castle.Registrations.Any(row => row.ClanId == clan.Id),
            clan != null && SameDay(clan.Id, castle),
            castle.Registrations.Count(row => row.Side == SiegeProtocol.Attacker),
            DefenderCount(castle),
            Settings.MinClanLevel,
            Settings.MaxAttackers,
            Settings.MaxDefenders);
    }

    int DefenderCount(CastleSiege castle)
    {
        int count = castle.OwnerClanId > 0 ? 1 : 0;
        foreach (var row in castle.Registrations)
        {
            if (row.Side is SiegeProtocol.Defender or SiegeProtocol.DefenderPending)
                count++;
        }

        return count;
    }

    bool SameDay(int clanId, CastleSiege castle)
    {
        var day = Day(castle.SiegeUnixMs);
        foreach (var other in _castles)
        {
            if (other.Id == castle.Id || Day(other.SiegeUnixMs) != day)
                continue;
            if (other.OwnerClanId == clanId)
                return true;
            if (other.Registrations.Any(row => row.ClanId == clanId))
                return true;
        }

        return false;
    }

    static bool IsAttacker(CastleSiege castle, int clanId) =>
        castle.Registrations.Any(row => row.ClanId == clanId && row.Side == SiegeProtocol.Attacker);

    static void ClearPending(CastleSiege castle) =>
        castle.Registrations.RemoveAll(row => row.Side == SiegeProtocol.DefenderPending);

    static DayOfWeek Day(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.DayOfWeek;
}

public enum SiegeStartKind
{
    Started,
    Cancelled,
    AlreadyRunning,
    UnknownCastle
}

public sealed class CastleSiege
{
    internal CastleSiege(CastleTemplate template)
    {
        Id = template.Id;
        Name = template.Name;
        Day = template.Day;
        Hour = template.Hour;
        MaxConcurrent = template.MaxConcurrent;
        TaxPercent = SiegeProtocol.DefaultTaxPercent;
    }

    public int Id { get; }
    public string Name { get; }
    public DayOfWeek Day { get; }
    public int Hour { get; }
    public int MaxConcurrent { get; internal set; }
    public int OwnerClanId { get; internal set; }
    public int TaxPercent { get; internal set; }
    public long SiegeUnixMs { get; internal set; }
    public bool RegistrationClosed { get; internal set; }
    public bool ScheduleAccepted { get; internal set; }
    public bool InProgress { get; internal set; }
    public long EndsUnixMs { get; internal set; }
    internal List<SiegeRegistration> Registrations { get; } = [];
}

public sealed class SiegeRegistration
{
    public SiegeRegistration(int clanId, int side)
    {
        ClanId = clanId;
        Side = side;
    }

    public int ClanId { get; }
    public int Side { get; set; }
}

public readonly record struct SiegeActor(int CharacterId);

public sealed class SiegeClanInfo
{
    public int Id { get; init; }
    public int Level { get; init; }
    public int LeaderId { get; init; }
    public long DissolvingExpiryUnixMs { get; init; }
}

public sealed record SiegeList(
    int CastleId,
    string Name,
    int OwnerClanId,
    int TaxPercent,
    long SiegeUnixMs,
    long RegistrationDeadlineUnixMs,
    bool RegistrationClosed,
    bool InProgress,
    long EndsUnixMs,
    IReadOnlyList<SiegeListClan> Clans);

public readonly record struct SiegeListClan(int ClanId, int Side);
