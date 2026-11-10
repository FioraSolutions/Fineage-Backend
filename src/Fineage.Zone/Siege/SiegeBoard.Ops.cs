using Fineage.Protocol;

namespace Fineage.Zone.Siege;

public sealed partial class SiegeBoard
{
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
}
