using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Siege;

namespace Fineage.Zone.Clan;

public sealed class ChSiegeBoard
{
    readonly List<ChSiegeHall> _halls;
    readonly ChSiegeSettings _settings;

    public ChSiegeBoard(IEnumerable<SiegableHallTemplate> templates, ChSiegeSettings? settings = null)
    {
        _settings = settings ?? ChSiegeSettings.Default;
        _halls = templates.Select(t => new ChSiegeHall(t)).ToList();
    }

    public ChSiegeSettings Settings => _settings;

    public IReadOnlyList<ChSiegeHall> Halls => _halls;

    public ChSiegeHall? Find(int hallId) => _halls.FirstOrDefault(h => h.Id == hallId);

    public bool IsSiegableHall(int id) => Find(id) != null;

    public void Restore(IReadOnlyList<SiegableClanHallSave> rows, long nowUnixMs)
    {
        var map = rows.ToDictionary(r => r.Id);
        foreach (var hall in _halls)
        {
            if (!map.TryGetValue(hall.Id, out var saved))
            {
                if (hall.NextSiegeUnixMs == 0)
                    hall.NextSiegeUnixMs = ComputeNextSiege(hall, nowUnixMs);
                continue;
            }
            hall.OwnerClanId = saved.OwnerClanId;
            hall.NextSiegeUnixMs = saved.NextSiegeUnixMs > 0 ? saved.NextSiegeUnixMs : ComputeNextSiege(hall, nowUnixMs);
            hall.RegistrationClosed = saved.RegistrationClosed;
            hall.InProgress = saved.InProgress;
            hall.EndsUnixMs = saved.EndsUnixMs;
            hall.PendingWinnerClanId = saved.PendingWinnerClanId;
            hall.Attackers.Clear();
            hall.Attackers.AddRange(saved.Attackers ?? []);
        }
    }

    public List<SiegableClanHallSave> Export() => _halls.Select(h => new SiegableClanHallSave(
        h.Id,
        h.OwnerClanId,
        h.NextSiegeUnixMs,
        h.RegistrationClosed,
        h.InProgress,
        h.EndsUnixMs,
        h.Attackers.Count > 0 ? h.Attackers.ToArray() : null,
        h.PendingWinnerClanId)).ToList();

    public string? Register(SiegeRequester actor, SiegeClanInfo? clan, int hallId, int ownedCastleId, int ownedHallId,
        Func<int, bool> registeredElsewhere, long nowUnixMs)
    {
        var hall = Find(hallId);
        if (hall == null)
            return SiegeProtocol.Castle;
        if (clan == null)
            return SiegeProtocol.Clan;
        if (actor.CharacterId != clan.LeaderId)
            return SiegeProtocol.Leader;
        if (clan.DissolvingExpiryUnixMs > nowUnixMs)
            return SiegeProtocol.Dissolving;
        if (hall.RegistrationClosed || nowUnixMs >= hall.NextSiegeUnixMs - _settings.RegistrationCloseBeforeMs)
            return SiegeProtocol.Closed;
        if (hall.InProgress)
            return SiegeProtocol.Progress;
        if (clan.Level < _settings.MinClanLevel)
            return SiegeProtocol.Level;
        if (hall.OwnerClanId == clan.Id)
            return SiegeProtocol.AutoDefender;
        if (ownedCastleId > 0 || (ownedHallId > 0 && ownedHallId != hallId))
            return SiegeProtocol.Owns;
        if (hall.Attackers.Contains(clan.Id))
            return SiegeProtocol.Duplicate;
        if (registeredElsewhere(clan.Id))
            return SiegeProtocol.SameDay;
        if (hall.Attackers.Count >= _settings.MaxAttackers)
            return SiegeProtocol.Attackers;
        hall.Attackers.Add(clan.Id);
        return null;
    }

    public string? Unregister(SiegeRequester actor, SiegeClanInfo? clan, int hallId, long nowUnixMs)
    {
        var hall = Find(hallId);
        if (hall == null)
            return SiegeProtocol.Castle;
        if (clan == null)
            return SiegeProtocol.Clan;
        if (actor.CharacterId != clan.LeaderId)
            return SiegeProtocol.Leader;
        if (hall.OwnerClanId == clan.Id)
            return SiegeProtocol.AutoDefender;
        if (!hall.Attackers.Contains(clan.Id))
            return SiegeProtocol.None;
        if (hall.RegistrationClosed || hall.InProgress)
            return hall.InProgress ? SiegeProtocol.Progress : SiegeProtocol.Closed;
        hall.Attackers.Remove(clan.Id);
        return null;
    }

    /// <summary>Mobius <c>prepareOwner</c>: registration ends one hour before the siege.</summary>
    public bool CloseRegistration(int hallId, long nowUnixMs)
    {
        var hall = Find(hallId);
        if (hall == null || hall.InProgress || hall.RegistrationClosed)
            return false;
        if (nowUnixMs < hall.NextSiegeUnixMs - _settings.RegistrationCloseBeforeMs)
            return false;
        hall.RegistrationClosed = true;
        if (hall.OwnerClanId > 0 && !hall.Attackers.Contains(hall.OwnerClanId))
            hall.Attackers.Add(hall.OwnerClanId);
        if (hall.OwnerClanId > 0)
            hall.OwnerClanId = 0;
        return true;
    }

    public ChSiegeStartKind StartSiege(int hallId, long nowUnixMs)
    {
        var hall = Find(hallId);
        if (hall == null)
            return ChSiegeStartKind.UnknownHall;
        if (hall.InProgress)
            return ChSiegeStartKind.AlreadyRunning;
        if (hall.Attackers.Count == 0 && !hall.AllowStartWithoutAttackers)
        {
            hall.RegistrationClosed = false;
            hall.Attackers.Clear();
            hall.NextSiegeUnixMs = ComputeNextSiege(hall, nowUnixMs);
            return ChSiegeStartKind.Cancelled;
        }

        hall.InProgress = true;
        hall.RegistrationClosed = true;
        hall.EndsUnixMs = nowUnixMs + hall.SiegeLengthMs;
        hall.PendingWinnerClanId = 0;
        return ChSiegeStartKind.Started;
    }

    public bool EndSiege(int hallId, long nowUnixMs, out int winnerClanId)
    {
        winnerClanId = 0;
        var hall = Find(hallId);
        if (hall == null || !hall.InProgress)
            return false;
        winnerClanId = hall.PendingWinnerClanId;
        hall.InProgress = false;
        hall.EndsUnixMs = 0;
        hall.PendingWinnerClanId = 0;
        if (winnerClanId > 0)
            hall.OwnerClanId = winnerClanId;
        hall.Attackers.Clear();
        hall.RegistrationClosed = false;
        hall.NextSiegeUnixMs = ComputeNextSiege(hall, nowUnixMs);
        return true;
    }

    public bool SetPendingWinner(int hallId, int clanId)
    {
        var hall = Find(hallId);
        if (hall == null || !hall.InProgress || clanId <= 0 || !hall.Attackers.Contains(clanId))
            return false;
        hall.PendingWinnerClanId = clanId;
        return true;
    }

    public bool ClanRegistered(int clanId) =>
        _halls.Any(h => h.Attackers.Contains(clanId));

    public long ComputeNextSiege(ChSiegeHall hall, long anchorUnixMs)
    {
        var baseTime = DateTimeOffset.FromUnixTimeMilliseconds(anchorUnixMs).UtcDateTime;
        var next = baseTime.Date.AddDays(hall.SchedulePeriodDays)
            .AddHours(hall.ScheduleHour)
            .AddMinutes(hall.ScheduleMinute);
        if (next <= baseTime)
            next = next.AddDays(hall.SchedulePeriodDays);
        return new DateTimeOffset(next, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}