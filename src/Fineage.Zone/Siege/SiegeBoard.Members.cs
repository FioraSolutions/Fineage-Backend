using Fineage.Protocol;

namespace Fineage.Zone.Siege;

public sealed partial class SiegeBoard
{
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
