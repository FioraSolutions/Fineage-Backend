namespace Fineage.Zone.SevenSigns;

/// <summary>Mobius <c>SevenSignsFestival</c> lifecycle hook: resets per competition and records high scores.</summary>
public sealed class SevenSignsFestivalRuntime : ISevenSignsFestivalHook
{
    readonly SevenSignsFestivalBoard _board;
    SevenSignsEngine? _engine;

    public SevenSignsFestivalRuntime(SevenSignsFestivalCatalog catalog)
        : this(new SevenSignsFestivalBoard(catalog))
    {
    }

    public SevenSignsFestivalRuntime(SevenSignsFestivalBoard board)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    public SevenSignsFestivalBoard Board => _board;

    internal SevenSignsEngine? Engine => _engine;

    public void BindEngine(SevenSignsEngine engine) => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public void OnCompetitionStarted() => _board.ResetCycle();

    public void OnCompetitionEnded(SevenSignsCabal winnerBeforeReward)
    {
        _ = winnerBeforeReward;
    }

    public void OnCycleReset() => _board.ResetCycle();

    public bool TryRecordInstanceScore(SevenSignsCabal cabal, int tierId, long offeringScore)
    {
        if (_engine == null)
            return false;
        return _board.RecordFinalScore(cabal, tierId, offeringScore, _engine);
    }
}