using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

/// <summary>
/// Competition period, noble points, queues, and match flow.
/// Combat reports the winner through <see cref="Report"/>. Disconnect and leave use the Mobius loss rules.
/// A battle that ends with no report uses the HP-less tie.
/// </summary>
public sealed class OlympiadBoard
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

    public static OlympiadBoard OpenBesideAccounts(string accountStorePath, DateTimeOffset now, OlympiadSettings? settings = null)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(accountStorePath));
        string path = Path.Combine(string.IsNullOrEmpty(dir) ? "data" : dir, "olympiad.json");
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

    void CatchUp(long now, OlympiadStep step, bool announce)
    {
        for (int i = 0; i < 8; i++)
        {
            FlushPeriodNotes(step, announce);
            bool moved = false;
            if (_period == OlympiadProtocol.PeriodValidation)
            {
                if (_inComp)
                    EndCompetition(step, announce);
                if (now >= _validationEnd)
                {
                    FinishValidation(now);
                    moved = true;
                }
            }
            else if (now >= _olympiadEnd)
            {
                if (_inComp)
                    EndCompetition(step, announce);
                BeginValidation(now);
                moved = true;
            }
            else
            {
                if (_nextWeekly > 0 && now >= _nextWeekly)
                {
                    ApplyWeekly();
                    _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
                    Persist();
                    moved = true;
                }

                RefreshCompetition(now, step, announce);
            }

            FlushPeriodNotes(step, announce);
            if (!moved)
                return;
        }
    }

    void FlushPeriodNotes(OlympiadStep step, bool announce)
    {
        if (!announce)
            return;
        if (_announcePeriodStart)
        {
            step.Notices.Add(OlympiadLines.PeriodStarted(_cycle));
            step.RefreshAll = true;
            _announcePeriodStart = false;
        }

        if (_announcePeriodEnd)
        {
            step.Notices.Add(OlympiadLines.PeriodEnded(_cycle));
            step.RefreshAll = true;
            _announcePeriodEnd = false;
        }
    }

    void RefreshCompetition(long now, OlympiadStep step, bool announce)
    {
        if (_period != OlympiadProtocol.PeriodCompetition)
            return;
        var window = OlympiadSchedule.At(now, Settings);
        _compStart = window.StartUnixMs;
        _compEnd = window.EndUnixMs;
        if (window.Open)
        {
            if (!_inComp)
            {
                _inComp = true;
                _compAnnounced = false;
                _registrationAnnounced = false;
                _nextMatchmake = now;
            }

            if (announce && !_compAnnounced)
            {
                _compAnnounced = true;
                step.Notices.Add(OlympiadLines.CompetitionStarted());
                step.RefreshAll = true;
            }

            if (announce && !_registrationAnnounced && _compEnd - now < Settings.RegistrationCloseMillis)
            {
                _registrationAnnounced = true;
                step.Notices.Add(OlympiadLines.RegistrationEnded());
                step.RefreshAll = true;
            }
        }
        else if (_inComp)
        {
            EndCompetition(step, announce);
        }
    }

    void EndCompetition(OlympiadStep step, bool announce)
    {
        _inComp = false;
        _compAnnounced = false;
        _classed.Clear();
        _nonClass.Clear();
        if (announce)
        {
            step.Notices.Add(OlympiadLines.CompetitionEnded());
            step.RefreshAll = true;
        }
    }

    void BeginValidation(long now)
    {
        _period = OlympiadProtocol.PeriodValidation;
        _inComp = false;
        _compAnnounced = false;
        _classed.Clear();
        _nonClass.Clear();
        _validationEnd = now + Settings.ValidationMillis;
        _announcePeriodEnd = true;
        Persist();
    }

    void FinishValidation(long now)
    {
        _cycle++;
        _period = OlympiadProtocol.PeriodCompetition;
        _nobles.Clear();
        _validationEnd = 0;
        _olympiadEnd = OlympiadSchedule.MonthEnd(now, Settings);
        _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
        _announcePeriodStart = true;
        Persist();
    }

    void ApplyWeekly()
    {
        if (_period == OlympiadProtocol.PeriodValidation)
            return;
        foreach (var noble in _nobles.Values)
            noble.Points = OlympiadRules.AddWeekly(noble.Points, noble.Done, Settings.MaxPoints, Settings.WeeklyPoints);
    }

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

    void ApplyScore(int characterId, OlympiadRules.Score score)
    {
        if (!_nobles.TryGetValue(characterId, out var noble))
            return;
        noble.Points = score.Points;
        noble.Done = score.Done;
        noble.Won = score.Won;
        noble.Lost = score.Lost;
        noble.Drawn = score.Drawn;
    }

    OlympiadRules.Score ScoreOf(int characterId)
    {
        if (!_nobles.TryGetValue(characterId, out var noble))
            return new OlympiadRules.Score(Settings.StartPoints, 0, 0, 0, 0);
        return new OlympiadRules.Score(noble.Points, noble.Done, noble.Won, noble.Lost, noble.Drawn);
    }

    string NameOf(int characterId) =>
        _nobles.TryGetValue(characterId, out var noble) ? noble.Name : "";

    bool RegistrationOpen(long now) =>
        Settings.Enabled &&
        _inComp &&
        _period == OlympiadProtocol.PeriodCompetition &&
        _compEnd - now >= Settings.RegistrationCloseMillis;

    bool OnClassedList(int characterId)
    {
        foreach (var list in _classed.Values)
        {
            if (list.Contains(characterId))
                return true;
        }

        return false;
    }

    void RemoveFromQueues(int characterId)
    {
        _nonClass.Remove(characterId);
        foreach (var list in _classed.Values)
            list.Remove(characterId);
    }

    Match? MatchOf(int characterId)
    {
        foreach (var match in _matches.Values)
        {
            if (match.OneId == characterId || match.TwoId == characterId)
                return match;
        }

        return null;
    }

    static void Fail(OlympiadStep step, int characterId, string code, OlympiadNotice notice)
    {
        step.Error = code;
        step.Notices.Add(notice);
        _ = characterId;
    }

    static OlympiadNotice NoticeFor(string code, int characterId, bool classed) => code switch
    {
        OlympiadProtocol.Disabled => OlympiadLines.NotInProgress(characterId),
        OlympiadProtocol.Period => OlympiadLines.NotInProgress(characterId),
        OlympiadProtocol.Noble => OlympiadLines.NoblesOnly(characterId),
        OlympiadProtocol.Subclass => OlympiadLines.Subclass(characterId),
        OlympiadProtocol.Cursed => OlympiadLines.Cursed(characterId),
        OlympiadProtocol.Inventory => OlympiadLines.Inventory(characterId),
        OlympiadProtocol.Late => OlympiadLines.TooLate(characterId),
        OlympiadProtocol.Classed => OlympiadLines.AlreadyClassed(characterId),
        OlympiadProtocol.Registered => OlympiadLines.AlreadyRegistered(characterId),
        OlympiadProtocol.Points => OlympiadLines.NeedPoints(characterId, classed ? OlympiadProtocol.ClassedPointFloor : OlympiadProtocol.NonClassedPointFloor),
        _ => OlympiadLines.NotInProgress(characterId)
    };

    void Load(OlympiadSnapshot snap, long now)
    {
        _cycle = snap.Cycle < 1 ? 1 : snap.Cycle;
        _period = snap.Period == OlympiadProtocol.PeriodValidation
            ? OlympiadProtocol.PeriodValidation
            : OlympiadProtocol.PeriodCompetition;
        _olympiadEnd = snap.OlympiadEndUnixMs;
        _validationEnd = snap.ValidationEndUnixMs;
        _nextWeekly = snap.NextWeeklyUnixMs;
        foreach (int id in snap.Granted)
        {
            if (id > 0)
                _granted.Add(id);
        }

        foreach (var row in snap.Nobles)
        {
            if (row.CharacterId <= 0 || _nobles.ContainsKey(row.CharacterId))
                continue;
            _nobles[row.CharacterId] = new Noble
            {
                CharacterId = row.CharacterId,
                ClassId = row.ClassId,
                Name = row.Name ?? "",
                Points = OlympiadRules.ClampStored(row.Points, Math.Max(0, row.Done), Settings.MaxPoints, Settings.WeeklyPoints),
                Done = Math.Max(0, row.Done),
                Won = Math.Max(0, row.Won),
                Lost = Math.Max(0, row.Lost),
                Drawn = Math.Max(0, row.Drawn)
            };
        }

        bool fresh = snap.OlympiadEndUnixMs == 0;
        if (fresh)
        {
            _period = OlympiadProtocol.PeriodCompetition;
            _olympiadEnd = OlympiadSchedule.MonthEnd(now, Settings);
            _validationEnd = 0;
            _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
            _announcePeriodStart = true;
            Persist();
            return;
        }

        if (_period == OlympiadProtocol.PeriodValidation)
        {
            if (now >= _validationEnd)
                FinishValidation(now);
            return;
        }

        if (now >= _olympiadEnd)
        {
            _olympiadEnd = OlympiadSchedule.MonthEnd(now, Settings);
            _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
            _announcePeriodStart = true;
            Persist();
            return;
        }

        if (_nextWeekly > 0 && now >= _nextWeekly)
        {
            ApplyWeekly();
            _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
            Persist();
        }
    }

    bool Persist()
    {
        try
        {
            _store.Save(Export());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    OlympiadSnapshot Export()
    {
        var nobles = new List<OlympiadNobleRow>(_nobles.Count);
        foreach (var noble in _nobles.Values.OrderBy(row => row.CharacterId))
        {
            nobles.Add(new OlympiadNobleRow
            {
                CharacterId = noble.CharacterId,
                ClassId = noble.ClassId,
                Name = noble.Name,
                Points = noble.Points,
                Done = noble.Done,
                Won = noble.Won,
                Lost = noble.Lost,
                Drawn = noble.Drawn
            });
        }

        return new OlympiadSnapshot
        {
            Version = 1,
            Cycle = _cycle,
            Period = _period,
            OlympiadEndUnixMs = _olympiadEnd,
            ValidationEndUnixMs = _validationEnd,
            NextWeeklyUnixMs = _nextWeekly,
            Granted = _granted.OrderBy(id => id).ToList(),
            Nobles = nobles
        };
    }

    sealed class Noble
    {
        public int CharacterId;
        public int ClassId;
        public string Name = "";
        public int Points;
        public int Done;
        public int Won;
        public int Lost;
        public int Drawn;
    }

    sealed class Match
    {
        public int Id;
        public int Stadium;
        public bool Classed;
        public int OneId;
        public int TwoId;
        public OlympiadMatchPhase Phase;
        public long PhaseStart;
        public long PhaseEnds;
        public bool Ported;
        public bool Resolved;
        public bool OneLeft;
        public bool TwoLeft;
        public bool HomeOne;
        public bool HomeTwo;
        public int ReturnOneX;
        public int ReturnOneY;
        public int ReturnOneZ;
        public int ReturnTwoX;
        public int ReturnTwoY;
        public int ReturnTwoZ;
        public int CountdownKind;
        public int Cursor;
        public List<OlympiadSchedule.Announcement> Announcements = [];
    }
}
