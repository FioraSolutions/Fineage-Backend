using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public sealed partial class OlympiadBoard
{
    void AdvanceMatches(long now, OlympiadStep step, Func<int, OlympiadPresence?>? presence)
    {
        foreach (var match in _matches.Values.ToArray())
            AdvanceMatch(match, now, step, presence);
    }

    void AdvanceMatch(Match match, long now, OlympiadStep step, Func<int, OlympiadPresence?>? presence)
    {
        for (int guard = 0; guard < 12; guard++)
        {
            if (match.Phase == OlympiadMatchPhase.Done || !_matches.ContainsKey(match.Id))
                return;
            if (now < match.PhaseStart)
                return;
            if (match.Ported && !match.Resolved && (match.OneLeft || match.TwoLeft || Offline(match, presence)))
            {
                ResolveDeparture(match, step, presence);
                return;
            }

            EmitCountdown(match, now, step);
            if (now < match.PhaseEnds)
                return;

            switch (match.Phase)
            {
                case OlympiadMatchPhase.Countdown:
                    if (!TryPort(match, now, step, presence))
                        return;
                    break;
                case OlympiadMatchPhase.Pause:
                    EnterBattleCountdown(match, step);
                    break;
                case OlympiadMatchPhase.BattleCountdown:
                    EnterFight(match, step);
                    break;
                case OlympiadMatchPhase.Fighting:
                    if (!match.Resolved)
                    {
                        Commit(match, OlympiadRules.Tie(ScoreOf(match.OneId), ScoreOf(match.TwoId), Settings.MaxPoints), step);
                        BeginReturn(match, match.PhaseEnds);
                        NotePhase(match, step);
                    }
                    break;
                case OlympiadMatchPhase.Returning:
                    SendHome(match, step);
                    Close(match);
                    return;
            }
        }
    }

    bool TryPort(Match match, long now, OlympiadStep step, Func<int, OlympiadPresence?>? presence)
    {
        var one = presence?.Invoke(match.OneId);
        var two = presence?.Invoke(match.TwoId);
        bool oneDefault = match.OneLeft || FailsPort(one);
        bool twoDefault = match.TwoLeft || FailsPort(two);
        if (oneDefault || twoDefault)
        {
            Commit(match, OlympiadRules.Default(ScoreOf(match.OneId), ScoreOf(match.TwoId), oneDefault, twoDefault, Settings.MaxPoints), step);
            Close(match);
            return false;
        }

        match.ReturnOneX = one!.Value.X;
        match.ReturnOneY = one.Value.Y;
        match.ReturnOneZ = one.Value.Z;
        match.ReturnTwoX = two!.Value.X;
        match.ReturnTwoY = two.Value.Y;
        match.ReturnTwoZ = two.Value.Z;
        var site = Stadiums[match.Stadium];
        step.Moves.Add(new OlympiadMove(match.OneId, site.X + OlympiadProtocol.SpawnSeparation, site.Y, site.Z, site.ZoneId, false));
        step.Moves.Add(new OlympiadMove(match.TwoId, site.X - OlympiadProtocol.SpawnSeparation, site.Y, site.Z, site.ZoneId, false));
        match.Ported = true;
        long portAt = match.PhaseEnds;
        match.Phase = OlympiadMatchPhase.Pause;
        match.PhaseStart = portAt;
        match.PhaseEnds = portAt + OlympiadProtocol.ArenaPauseMillis;
        match.Announcements = [];
        NotePhase(match, step);
        return true;
    }

    void EnterBattleCountdown(Match match, OlympiadStep step)
    {
        long start = match.PhaseEnds;
        match.Phase = OlympiadMatchPhase.BattleCountdown;
        match.PhaseStart = start;
        match.PhaseEnds = start + OlympiadProtocol.BattleCountdownMillis;
        match.Announcements = OlympiadSchedule.FightCountdown(start);
        match.Cursor = 0;
        match.CountdownKind = OlympiadProtocol.CountdownFight;
        NotePhase(match, step);
    }

    void EnterFight(Match match, OlympiadStep step)
    {
        long start = match.PhaseEnds;
        match.Phase = OlympiadMatchPhase.Fighting;
        match.PhaseStart = start;
        match.PhaseEnds = start + Settings.BattleMillis;
        match.Announcements = [];
        NotePhase(match, step);
    }

    void BeginReturn(Match match, long at)
    {
        match.Phase = OlympiadMatchPhase.Returning;
        match.PhaseStart = at;
        match.PhaseEnds = at + OlympiadProtocol.ReturnCountdownMillis;
        match.Announcements = OlympiadSchedule.ReturnCountdown(at);
        match.Cursor = 0;
        match.CountdownKind = OlympiadProtocol.CountdownReturn;
    }

    void ResolveDeparture(Match match, OlympiadStep step, Func<int, OlympiadPresence?>? presence)
    {
        if (match.Resolved || match.Phase == OlympiadMatchPhase.Done)
        {
            if (match.Phase == OlympiadMatchPhase.Returning)
                SendHome(match, step, onlyLeft: true);
            return;
        }

        if (!match.Ported)
        {
            bool oneDefault = match.OneLeft || FailsPort(presence?.Invoke(match.OneId));
            bool twoDefault = match.TwoLeft || FailsPort(presence?.Invoke(match.TwoId));
            Commit(match, OlympiadRules.Default(ScoreOf(match.OneId), ScoreOf(match.TwoId), oneDefault, twoDefault, Settings.MaxPoints), step);
            Close(match);
            return;
        }

        bool oneGone = match.OneLeft || Offline(match.OneId, presence);
        bool twoGone = match.TwoLeft || Offline(match.TwoId, presence);
        Commit(match, OlympiadRules.Crash(ScoreOf(match.OneId), ScoreOf(match.TwoId), oneGone, twoGone, match.Classed, Settings.MaxPoints, match.OneId, match.TwoId), step);
        SendHome(match, step);
        Close(match);
    }

    static bool FailsPort(OlympiadPresence? presence) =>
        presence is not { Online: true } ||
        presence.Value.Dead ||
        presence.Value.ClassId != presence.Value.BaseClassId ||
        presence.Value.CursedWeapon ||
        OlympiadRules.InventoryBlocks(presence.Value.InventorySlots, presence.Value.InventoryLimit);

    static bool Offline(int characterId, Func<int, OlympiadPresence?>? presence)
    {
        var found = presence?.Invoke(characterId);
        return found is not { Online: true };
    }

    bool Offline(Match match, Func<int, OlympiadPresence?>? presence) =>
        Offline(match.OneId, presence) || Offline(match.TwoId, presence);

    void Commit(Match match, OlympiadRules.Transfer transfer, OlympiadStep step)
    {
        if (match.Resolved)
            return;
        ApplyScore(match.OneId, transfer.One);
        ApplyScore(match.TwoId, transfer.Two);
        match.Resolved = true;
        if (transfer.RewardCount > 0 && transfer.WinnerObjectId != OlympiadProtocol.NoWinner)
            step.Grants.Add(new OlympiadGrant(transfer.WinnerObjectId, Settings.RewardItemId, transfer.RewardCount));
        var result = new OlympiadResultNote
        {
            MatchId = match.Id,
            Outcome = transfer.Outcome,
            WinnerObjectId = transfer.WinnerObjectId,
            PlayerOneId = match.OneId,
            PlayerTwoId = match.TwoId,
            PointsOne = transfer.One.Points,
            PointsTwo = transfer.Two.Points,
            DeltaOne = transfer.DeltaOne,
            DeltaTwo = transfer.DeltaTwo,
            RewardItemId = transfer.RewardCount > 0 ? Settings.RewardItemId : 0,
            RewardCount = transfer.RewardCount,
            CompetitionsDoneOne = transfer.One.Done,
            CompetitionsDoneTwo = transfer.Two.Done,
            PlayerOneName = NameOf(match.OneId),
            PlayerTwoName = NameOf(match.TwoId)
        };
        step.Results.Add(result);
        Describe(match, result, step);
        step.RefreshStatus.Add(match.OneId);
        step.RefreshStatus.Add(match.TwoId);
        Persist();
    }

    static void Describe(Match match, OlympiadResultNote result, OlympiadStep step)
    {
        switch (result.Outcome)
        {
            case OlympiadProtocol.OutcomeWinOne:
            case OlympiadProtocol.OutcomeWinTwo:
                Tell(match, step, OlympiadLines.Win(result.WinnerObjectId == match.OneId ? result.PlayerOneName : result.PlayerTwoName));
                Tell(match, step, OlympiadLines.Earned(result.WinnerObjectId == match.OneId ? result.PlayerOneName : result.PlayerTwoName, Math.Abs(result.WinnerObjectId == match.OneId ? result.DeltaOne : result.DeltaTwo)));
                Tell(match, step, OlympiadLines.Lost(result.WinnerObjectId == match.OneId ? result.PlayerTwoName : result.PlayerOneName, Math.Abs(result.WinnerObjectId == match.OneId ? result.DeltaTwo : result.DeltaOne)));
                break;
            case OlympiadProtocol.OutcomeTie:
                Tell(match, step, OlympiadLines.Tie());
                Tell(match, step, OlympiadLines.Lost(result.PlayerOneName, Math.Abs(result.DeltaOne)));
                Tell(match, step, OlympiadLines.Lost(result.PlayerTwoName, Math.Abs(result.DeltaTwo)));
                break;
            case OlympiadProtocol.OutcomeCrashOne:
                Tell(match, step, OlympiadLines.Win(result.PlayerTwoName));
                Tell(match, step, OlympiadLines.Earned(result.PlayerTwoName, Math.Abs(result.DeltaTwo)));
                break;
            case OlympiadProtocol.OutcomeCrashTwo:
                Tell(match, step, OlympiadLines.Win(result.PlayerOneName));
                Tell(match, step, OlympiadLines.Earned(result.PlayerOneName, Math.Abs(result.DeltaOne)));
                break;
            case OlympiadProtocol.OutcomeCrashBoth:
                Tell(match, step, OlympiadLines.Lost(result.PlayerOneName, Math.Abs(result.DeltaOne)));
                Tell(match, step, OlympiadLines.Lost(result.PlayerTwoName, Math.Abs(result.DeltaTwo)));
                break;
            default:
                Tell(match, step, OlympiadLines.Cancelled());
                if (result.DeltaOne != 0)
                    Tell(match, step, OlympiadLines.Lost(result.PlayerOneName, Math.Abs(result.DeltaOne)));
                if (result.DeltaTwo != 0)
                    Tell(match, step, OlympiadLines.Lost(result.PlayerTwoName, Math.Abs(result.DeltaTwo)));
                break;
        }
    }

    static void Tell(Match match, OlympiadStep step, OlympiadNotice notice)
    {
        step.Notices.Add(notice with { CharacterId = match.OneId });
        step.Notices.Add(notice with { CharacterId = match.TwoId });
    }

    void SendHome(Match match, OlympiadStep step, bool onlyLeft = false)
    {
        if (match.Ported && !match.HomeOne && (!onlyLeft || match.OneLeft))
        {
            step.Moves.Add(new OlympiadMove(match.OneId, match.ReturnOneX, match.ReturnOneY, match.ReturnOneZ, 0, true));
            match.HomeOne = true;
        }

        if (match.Ported && !match.HomeTwo && (!onlyLeft || match.TwoLeft))
        {
            step.Moves.Add(new OlympiadMove(match.TwoId, match.ReturnTwoX, match.ReturnTwoY, match.ReturnTwoZ, 0, true));
            match.HomeTwo = true;
        }
    }

    void Close(Match match)
    {
        match.Phase = OlympiadMatchPhase.Done;
        if (match.Stadium >= 0 && match.Stadium < _busy.Length)
            _busy[match.Stadium] = false;
        _matches.Remove(match.Id);
    }

    void EmitCountdown(Match match, long now, OlympiadStep step)
    {
        while (match.Cursor < match.Announcements.Count && match.Announcements[match.Cursor].AtUnixMs <= now)
        {
            var announcement = match.Announcements[match.Cursor];
            step.Countdowns.Add(new OlympiadCountdownNote(match.Id, match.CountdownKind, announcement.Seconds, match.OneId, match.TwoId));
            match.Cursor++;
        }
    }

    void NotePhase(Match match, OlympiadStep step)
    {
        var site = Stadiums[match.Stadium];
        step.Matches.Add(new OlympiadMatchNote
        {
            MatchId = match.Id,
            StadiumId = match.Stadium,
            ZoneId = site.ZoneId,
            Classed = match.Classed,
            Phase = (int)match.Phase,
            PlayerOneId = match.OneId,
            PlayerTwoId = match.TwoId,
            PlayerOneName = NameOf(match.OneId),
            PlayerTwoName = NameOf(match.TwoId),
            InstanceId = site.ZoneId
        });
    }

    void Matchmake(long now, OlympiadStep step, Func<int, OlympiadPresence?>? presence)
    {
        if (_nobles.Count == 0)
        {
            _nextMatchmake = now + OlympiadProtocol.IdleMatchmakeMillis;
            return;
        }

        _nextMatchmake = now + OlympiadProtocol.MatchmakeMillis;
        var readyClasses = new List<int>();
        foreach (var (classId, list) in _classed)
        {
            if (list.Count >= Settings.ClassedMin)
                readyClasses.Add(classId);
        }

        bool gateNon = _nonClass.Count >= Settings.NonClassedMin;
        if (readyClasses.Count == 0 && !gateNon)
            return;

        int started = 0;
        for (int stadium = 0; stadium < Stadiums.Count; stadium++)
        {
            if (_busy[stadium])
                continue;
            List<int>? picked = readyClasses.Count == 0 ? null : PickClass(readyClasses);
            bool nonPair = gateNon && _nonClass.Count >= 2;
            if (picked == null && !nonPair)
                break;

            bool firstHalf = stadium < Stadiums.Count / 2;
            bool classedMatch;
            int one;
            int two;
            if (firstHalf)
            {
                if (nonPair)
                {
                    (one, two) = TakePair(_nonClass);
                    classedMatch = false;
                }
                else
                {
                    (one, two) = TakePair(picked!);
                    classedMatch = true;
                }
            }
            else if (picked != null)
            {
                (one, two) = TakePair(picked);
                classedMatch = true;
            }
            else
            {
                (one, two) = TakePair(_nonClass);
                classedMatch = false;
            }

            var match = new Match
            {
                Id = _nextMatchId++,
                Stadium = stadium,
                Classed = classedMatch,
                OneId = one,
                TwoId = two,
                Phase = OlympiadMatchPhase.Countdown,
                PhaseStart = now + started * 1000L,
                CountdownKind = OlympiadProtocol.CountdownStadium
            };
            match.PhaseEnds = match.PhaseStart + Settings.WaitSeconds * 1000L;
            match.Announcements = OlympiadSchedule.StadiumCountdown(match.PhaseStart, Settings.WaitSeconds);
            _busy[stadium] = true;
            _matches[match.Id] = match;
            started++;
            NotePhase(match, step);
            AdvanceMatch(match, now, step, presence);
        }
    }

    List<int>? PickClass(List<int> ready)
    {
        int classId = ready[_random.Next(ready.Count)];
        if (!_classed.TryGetValue(classId, out var list) || list.Count < 2)
            return null;
        return list;
    }

    (int One, int Two) TakePair(List<int> list)
    {
        int first = _random.Next(list.Count);
        int one = list[first];
        list.RemoveAt(first);
        int second = _random.Next(list.Count);
        int two = list[second];
        list.RemoveAt(second);
        return (one, two);
    }
}
