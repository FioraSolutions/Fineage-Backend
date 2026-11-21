using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

/// <summary>Per-cycle high offering scores (Mobius <c>setFinalScore</c>) and active instances.</summary>
public sealed class SevenSignsFestivalBoard
{
    public const int MaxPartySlots = 4096;

    readonly SevenSignsFestivalCatalog _catalog;
    readonly SevenSignsFestivalInstance?[] _activeByParty = new SevenSignsFestivalInstance?[MaxPartySlots];
    readonly int[] _dawnHighScore = new int[5];
    readonly int[] _duskHighScore = new int[5];
    readonly long[] _accumulatedBonus = new long[5];

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
        Array.Clear(_accumulatedBonus);
        for (int i = 0; i < _activeByParty.Length; i++)
            _activeByParty[i] = null;
    }

    public long AccumulatedBonus(int tierId) =>
        tierId is >= 0 and < 5 ? _accumulatedBonus[tierId] : 0;

    public void AddAccumulatedBonus(int tierId, int stoneItemId, int stoneAmount)
    {
        if (tierId is < 0 or >= 5 || stoneAmount <= 0)
            return;
        int each = stoneItemId switch
        {
            SevenSignsProtocol.BlueSealStoneId => SevenSignsProtocol.BlueSealStoneValue,
            SevenSignsProtocol.GreenSealStoneId => SevenSignsProtocol.GreenSealStoneValue,
            SevenSignsProtocol.RedSealStoneId => SevenSignsProtocol.RedSealStoneValue,
            _ => 0
        };
        if (each <= 0)
            return;
        _accumulatedBonus[tierId] += (long)stoneAmount * each;
    }

    public long TakeAccumulatedShare(int tierId, int memberCount)
    {
        if (tierId is < 0 or >= 5 || memberCount <= 0)
            return 0;
        long pool = _accumulatedBonus[tierId];
        if (pool <= 0)
            return 0;
        long share = pool / memberCount;
        _accumulatedBonus[tierId] = Math.Max(0, pool - share);
        return share;
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