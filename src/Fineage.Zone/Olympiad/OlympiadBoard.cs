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
}
