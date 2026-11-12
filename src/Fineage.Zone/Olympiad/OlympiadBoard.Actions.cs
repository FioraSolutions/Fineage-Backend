using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public sealed partial class OlympiadBoard
{
    public OlympiadStep Register(OlympiadActor actor, bool classed, long nowUnixMs)
    {
        var step = new OlympiadStep();
        lock (_gate)
        {
            if (!Settings.Enabled)
            {
                Fail(step, actor.CharacterId, OlympiadProtocol.Disabled, OlympiadLines.NotInProgress(actor.CharacterId));
                return step;
            }

            CatchUp(nowUnixMs, step, announce: true);
            bool onClassed = OnClassedList(actor.CharacterId);
            bool inComp = onClassed || _nonClass.Contains(actor.CharacterId) || MatchOf(actor.CharacterId) != null;
            int points = _nobles.TryGetValue(actor.CharacterId, out var existing) ? existing.Points : Settings.StartPoints;
            string? blocked = OlympiadRules.RegisterBlock(new OlympiadRegisterCheck(
                true,
                _inComp && _period == OlympiadProtocol.PeriodCompetition,
                _granted.Contains(actor.CharacterId),
                actor.ClassId,
                actor.BaseClassId,
                actor.CursedWeapon,
                actor.InventorySlots,
                actor.InventoryLimit,
                _compEnd - nowUnixMs,
                Settings.RegistrationCloseMillis,
                onClassed,
                inComp,
                classed,
                int.MaxValue,
                Settings.ClassedPointFloor,
                Settings.NonClassedPointFloor));
            if (blocked != null)
            {
                Fail(step, actor.CharacterId, blocked, NoticeFor(blocked, actor.CharacterId, classed));
                return step;
            }

            if (existing == null)
            {
                existing = new Noble
                {
                    CharacterId = actor.CharacterId,
                    ClassId = actor.ClassId,
                    Name = actor.Name,
                    Points = Settings.StartPoints
                };
                _nobles[actor.CharacterId] = existing;
                points = existing.Points;
            }
            else if (!string.IsNullOrWhiteSpace(actor.Name))
            {
                existing.Name = actor.Name;
            }

            int floor = classed ? Settings.ClassedPointFloor : Settings.NonClassedPointFloor;
            if (points < floor)
            {
                Persist();
                Fail(step, actor.CharacterId, OlympiadProtocol.Points, OlympiadLines.NeedPoints(actor.CharacterId, floor));
                return step;
            }

            if (classed)
            {
                if (!_classed.TryGetValue(actor.ClassId, out var list))
                {
                    list = [];
                    _classed[actor.ClassId] = list;
                }

                list.Add(actor.CharacterId);
            }
            else
            {
                _nonClass.Add(actor.CharacterId);
            }

            if (!Persist())
            {
                RemoveFromQueues(actor.CharacterId);
                step.Error = OlympiadProtocol.Store;
                return step;
            }

            step.Notices.Add(classed
                ? OlympiadLines.RegisteredClassed(actor.CharacterId)
                : OlympiadLines.RegisteredNonClassed(actor.CharacterId));
            step.RefreshStatus.Add(actor.CharacterId);
            return step;
        }
    }

    public OlympiadStep Unregister(OlympiadActor actor, long nowUnixMs)
    {
        var step = new OlympiadStep();
        lock (_gate)
        {
            CatchUp(nowUnixMs, step, announce: true);
            if (!_inComp || _period != OlympiadProtocol.PeriodCompetition)
            {
                Fail(step, actor.CharacterId, OlympiadProtocol.Period, OlympiadLines.NotInProgress(actor.CharacterId));
                return step;
            }

            if (!_granted.Contains(actor.CharacterId))
            {
                Fail(step, actor.CharacterId, OlympiadProtocol.Noble, OlympiadLines.NoblesOnly(actor.CharacterId));
                return step;
            }

            if (MatchOf(actor.CharacterId) != null)
            {
                Fail(step, actor.CharacterId, OlympiadProtocol.Selected, OlympiadLines.Selected(actor.CharacterId));
                return step;
            }

            if (!OnClassedList(actor.CharacterId) && !_nonClass.Contains(actor.CharacterId))
            {
                Fail(step, actor.CharacterId, OlympiadProtocol.None, OlympiadLines.NotRegistered(actor.CharacterId));
                return step;
            }

            RemoveFromQueues(actor.CharacterId);
            Persist();
            step.Notices.Add(OlympiadLines.Unregistered(actor.CharacterId));
            step.RefreshStatus.Add(actor.CharacterId);
            return step;
        }
    }

    public OlympiadStep Leave(int characterId, long nowUnixMs, Func<int, OlympiadPresence?>? presence)
    {
        var step = new OlympiadStep();
        lock (_gate)
        {
            var match = MatchOf(characterId);
            if (match == null)
            {
                step.Error = OlympiadProtocol.MatchMissing;
                return step;
            }

            if (match.OneId == characterId)
                match.OneLeft = true;
            else
                match.TwoLeft = true;
            ResolveDeparture(match, step, presence);
            return step;
        }
    }

    public OlympiadStep Disconnect(int characterId, long nowUnixMs, Func<int, OlympiadPresence?>? presence)
    {
        var step = new OlympiadStep();
        lock (_gate)
        {
            RemoveFromQueues(characterId);
            var match = MatchOf(characterId);
            if (match == null)
                return step;
            if (match.OneId == characterId)
                match.OneLeft = true;
            else
                match.TwoLeft = true;
            ResolveDeparture(match, step, presence);
            return step;
        }
    }

    /// <summary>
    /// Combat hook. <paramref name="winnerObjectId"/> 0 is a draw and uses the tie formula.
    /// A participant id is a decided win and pays the gate pass. Accepted only while the fight is running.
    /// </summary>
    public OlympiadStep Report(int matchId, int winnerObjectId, long nowUnixMs)
    {
        var step = new OlympiadStep();
        lock (_gate)
        {
            if (!_matches.TryGetValue(matchId, out var match) || match.Phase == OlympiadMatchPhase.Done)
            {
                step.Error = OlympiadProtocol.MatchMissing;
                return step;
            }

            if (match.Resolved || match.Phase != OlympiadMatchPhase.Fighting)
            {
                step.Error = OlympiadProtocol.Phase;
                return step;
            }

            if (winnerObjectId != OlympiadProtocol.NoWinner && winnerObjectId != match.OneId && winnerObjectId != match.TwoId)
            {
                step.Error = OlympiadProtocol.Winner;
                return step;
            }

            var transfer = winnerObjectId == OlympiadProtocol.NoWinner
                ? OlympiadRules.Tie(ScoreOf(match.OneId), ScoreOf(match.TwoId), Settings.MaxPoints)
                : OlympiadRules.Win(
                    ScoreOf(match.OneId),
                    ScoreOf(match.TwoId),
                    winnerObjectId == match.OneId,
                    match.Classed,
                    Settings.MaxPoints,
                    match.Classed ? Settings.ClassedRewardCount : Settings.NonClassedRewardCount,
                    winnerObjectId);
            Commit(match, transfer, step);
            BeginReturn(match, nowUnixMs);
            NotePhase(match, step);
            return step;
        }
    }
}
