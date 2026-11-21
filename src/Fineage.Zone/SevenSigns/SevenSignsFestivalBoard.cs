namespace Fineage.Zone.SevenSigns;

/// <summary>Per-cycle high offering scores (Mobius <c>setFinalScore</c>) and active instances.</summary>
public sealed class SevenSignsFestivalBoard
{
    public const int MaxPartySlots = 4096;

    readonly SevenSignsFestivalCatalog _catalog;
    readonly SevenSignsFestivalInstance?[] _activeByParty = new SevenSignsFestivalInstance?[MaxPartySlots];
    readonly int[] _dawnHighScore = new int[5];
    readonly int[] _duskHighScore = new int[5];

    public SevenSignsFestivalBoard(SevenSignsFestivalCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public SevenSignsFestivalCatalog Catalog => _catalog;

    public int HighScore(SevenSignsCabal cabal, int tierId) =>
        cabal == SevenSignsCabal.Dawn ? _dawnHighScore[tierId] : _duskHighScore[tierId];

    public void ResetCycle()
    {
        Array.Clear(_dawnHighScore);
        Array.Clear(_duskHighScore);
        for (int i = 0; i < _activeByParty.Length; i++)
            _activeByParty[i] = null;
    }

    public bool PartyInFestival(int partyId) =>
        partyId is > 0 and < MaxPartySlots && _activeByParty[partyId] != null;

    public SevenSignsFestivalInstance? OfParty(int partyId) =>
        partyId is > 0 and < MaxPartySlots ? _activeByParty[partyId] : null;

    public void Register(SevenSignsFestivalInstance instance)
    {
        if (instance.PartyId is <= 0 or >= MaxPartySlots)
            throw new ArgumentOutOfRangeException(nameof(instance.PartyId));
        _activeByParty[instance.PartyId] = instance;
    }

    public void Remove(int partyId)
    {
        if (partyId is > 0 and < MaxPartySlots)
            _activeByParty[partyId] = null;
    }

    /// <returns>True when this cabal claimed <see cref="SevenSignsEngine.AddFestivalScore"/> for the tier.</returns>
    public bool RecordFinalScore(SevenSignsCabal cabal, int tierId, long offeringScore, SevenSignsEngine engine)
    {
        if (tierId is < 0 or > 4 || offeringScore < 0)
            return false;
        int thisHigh = HighScore(cabal, tierId);
        int otherHigh = cabal == SevenSignsCabal.Dawn
            ? _duskHighScore[tierId]
            : _dawnHighScore[tierId];
        if (offeringScore <= thisHigh)
            return false;
        if (cabal == SevenSignsCabal.Dawn)
            _dawnHighScore[tierId] = (int)Math.Min(int.MaxValue, offeringScore);
        else
            _duskHighScore[tierId] = (int)Math.Min(int.MaxValue, offeringScore);

        if (thisHigh > otherHigh)
            return false;
        if (offeringScore <= otherHigh)
            return false;
        int contrib = _catalog.LevelScores[tierId];
        engine.AddFestivalScore(cabal, contrib);
        return true;
    }
}