namespace Fineage.Zone.SevenSigns;

/// <summary>
/// Period machine, cabal membership, seal-stone scores, and seal ownership.
/// Festival rooms are not run here; <see cref="AddFestivalScore"/> is the score hook.
/// </summary>
public sealed partial class SevenSignsEngine
{
    readonly SevenSignsOptions _options;
    readonly ISevenSignsStore _store;
    readonly ISevenSignsFestivalHook? _festival;
    readonly object _gate = new();
    readonly Dictionary<int, PlayerRecord> _players = [];

    int _cycle = 1;
    SevenSignsPeriod _period = SevenSignsPeriod.Recruiting;
    SevenSignsCabal _previousWinner = SevenSignsCabal.None;
    long _dawnStones;
    long _duskStones;
    int _dawnFestival;
    int _duskFestival;
    DateTimeOffset _nextChange;
    SevenSignsCabal _avarice = SevenSignsCabal.None;
    SevenSignsCabal _gnosis = SevenSignsCabal.None;
    SevenSignsCabal _strife = SevenSignsCabal.None;

    /// <summary>
    /// Restores the saved cycle; a snapshot without a next boundary gets the one after <paramref name="loadedAt"/>.
    /// The engine holds no clock: <see cref="SevenSignsActor"/> advances it at the step's wall sample.
    /// </summary>
    public SevenSignsEngine(
        DateTimeOffset loadedAt,
        SevenSignsOptions? options = null,
        ISevenSignsStore? store = null,
        ISevenSignsFestivalHook? festival = null)
    {
        _options = options ?? new SevenSignsOptions();
        _store = store ?? new MemorySevenSignsStore();
        _festival = festival;
        Load(_store.Load(), loadedAt);
    }

    public SevenSignsOptions Options => _options;

    public SevenSignsPeriod Period
    {
        get { lock (_gate) return _period; }
    }

    public int Cycle
    {
        get { lock (_gate) return _cycle; }
    }

    public DateTimeOffset NextChange
    {
        get { lock (_gate) return _nextChange; }
    }

    /// <summary>Opens <c>seven-signs.json</c> inside <paramref name="stateDirectory"/> (default <c>data</c>).</summary>
    public static SevenSignsEngine OpenInStateDirectory(string stateDirectory, DateTimeOffset loadedAt, ISevenSignsFestivalHook? festival = null)
    {
        string path = Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory, "seven-signs.json");
        return new SevenSignsEngine(
            loadedAt,
            SevenSignsOptions.FromEnvironment(),
            new JsonSevenSignsStore(path),
            festival);
    }
}
