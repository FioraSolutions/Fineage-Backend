namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    public IReadOnlyList<WeddingCoupleRecord> ListWeddingCouples()
    {
        using (EnterStore())
            return _data.WeddingCouples.OrderBy(c => c.Id).ToList();
    }

    public bool SaveWeddingCouples(IReadOnlyList<WeddingCoupleRecord> couples, int nextCoupleId)
    {
        ArgumentNullException.ThrowIfNull(couples);
        if (nextCoupleId < 1)
            return false;
        using (EnterStore())
        {
            _data.WeddingCouples = couples.OrderBy(c => c.Id).ToList();
            _data.NextWeddingCoupleId = nextCoupleId;
            Save();
            return true;
        }
    }

    public int NextWeddingCoupleId
    {
        get
        {
            using (EnterStore())
                return Math.Max(1, _data.NextWeddingCoupleId);
        }
    }

    public bool PurgeWeddingCouplesForCharacter(int characterId)
    {
        if (characterId <= 0)
            return false;
        using (EnterStore())
        {
            int before = _data.WeddingCouples.Count;
            _data.WeddingCouples.RemoveAll(c => c.Player1Id == characterId || c.Player2Id == characterId);
            if (_data.WeddingCouples.Count == before)
                return true;
            Save();
            return true;
        }
    }
}