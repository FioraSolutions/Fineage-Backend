using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

/// <summary>
/// Competition period, noble points, queues, and match flow.
/// Combat reports the winner through <see cref="Report"/>. Disconnect and leave use the Mobius loss rules.
/// A battle that ends with no report uses the HP-less tie.
/// </summary>
public sealed partial class OlympiadBoard
{
    readonly IOlympiadStore _store;
    readonly Random _random;
    readonly object _gate = new();
    readonly Dictionary<int, Noble> _nobles = [];
    readonly HashSet<int> _granted = [];
    readonly Dictionary<int, List<int>> _classed = [];
    readonly List<int> _nonClass = [];
    readonly Dictionary<int, Match> _matches = [];
    readonly bool[] _busy;

    int _cycle = 1;
    int _period;
    long _olympiadEnd;
    long _validationEnd;
    long _nextWeekly;
    long _compStart;
    long _compEnd;
    long _nextMatchmake;
    int _nextMatchId = 1;
    bool _inComp;
    bool _compAnnounced;
    bool _registrationAnnounced;
    bool _announcePeriodStart;
    bool _announcePeriodEnd;

    public OlympiadBoard(
        DateTimeOffset now,
        OlympiadSettings? settings = null,
        IOlympiadStore? store = null,
        OlympiadStadiumCatalog? stadiums = null,
        Random? random = null)
    {
        Settings = settings ?? OlympiadSettings.Mobius;
        _store = store ?? new MemoryOlympiadStore();
        Stadiums = stadiums ?? OlympiadStadiumCatalog.LoadDefault();
        _random = random ?? Random.Shared;
        _busy = new bool[Stadiums.Count];
        Load(_store.Load(), now.ToUnixTimeMilliseconds());
    }

    public OlympiadSettings Settings { get; }

    public OlympiadStadiumCatalog Stadiums { get; }

    /// <summary>Opens <c>olympiad.json</c> inside <paramref name="stateDirectory"/> (default <c>data</c>).</summary>
    public static OlympiadBoard OpenInStateDirectory(string stateDirectory, DateTimeOffset now, OlympiadSettings? settings = null)
    {
        string path = Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory, "olympiad.json");
        return new OlympiadBoard(now, settings ?? OlympiadSettings.FromEnvironment(), new JsonOlympiadStore(path));
    }

    public bool IsNoble(int characterId)
    {
        lock (_gate)
            return _granted.Contains(characterId);
    }

    public bool GrantNoblesse(int characterId)
    {
        if (characterId <= 0)
            return false;
        lock (_gate)
        {
            _granted.Add(characterId);
            Persist();
            return true;
        }
    }

    public int PointsOf(int characterId)
    {
        lock (_gate)
            return _nobles.TryGetValue(characterId, out var noble) ? noble.Points : 0;
    }

    public int ClassedWaiting(int classId)
    {
        lock (_gate)
            return _classed.TryGetValue(classId, out var list) ? list.Count : 0;
    }

    public int NonClassedWaiting
    {
        get
        {
            lock (_gate)
                return _nonClass.Count;
        }
    }

    public int LiveMatchCount
    {
        get
        {
            lock (_gate)
                return _matches.Count;
        }
    }

    public int MatchIdFor(int characterId)
    {
        lock (_gate)
            return MatchOf(characterId)?.Id ?? 0;
    }

    public OlympiadPersonal View(int characterId, long nowUnixMs)
    {
        lock (_gate)
        {
            _nobles.TryGetValue(characterId, out var noble);
            var match = MatchOf(characterId);
            int queue = _nonClass.Contains(characterId)
                ? OlympiadProtocol.QueueNonClassed
                : OnClassedList(characterId) ? OlympiadProtocol.QueueClassed : OlympiadProtocol.QueueNone;
            return new OlympiadPersonal
            {
                Period = _period,
                Cycle = _cycle,
                InCompetition = _inComp,
                CompetitionStartUnixMs = _compStart,
                CompetitionEndUnixMs = _compEnd,
                OlympiadEndUnixMs = _olympiadEnd,
                ValidationEndUnixMs = _validationEnd,
                NextWeeklyUnixMs = _nextWeekly,
                RegistrationOpen = RegistrationOpen(nowUnixMs),
                Noble = _granted.Contains(characterId),
                Points = noble?.Points ?? 0,
                CompetitionsDone = noble?.Done ?? 0,
                Won = noble?.Won ?? 0,
                Lost = noble?.Lost ?? 0,
                Drawn = noble?.Drawn ?? 0,
                Queue = queue,
                MatchId = match?.Id ?? 0,
                StadiumId = match?.Stadium ?? -1,
                ClassId = noble?.ClassId ?? 0
            };
        }
    }

    public OlympiadStep Advance(long nowUnixMs, Func<int, OlympiadPresence?>? presence, bool announce = true)
    {
        var step = new OlympiadStep();
        lock (_gate)
        {
            if (!Settings.Enabled)
                return step;
            CatchUp(nowUnixMs, step, announce);
            AdvanceMatches(nowUnixMs, step, presence);
            if (_inComp && nowUnixMs >= _nextMatchmake)
                Matchmake(nowUnixMs, step, presence);
        }

        return step;
    }

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
