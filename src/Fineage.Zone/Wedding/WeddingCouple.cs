using Fineage.Accounts;

namespace Fineage.Zone.Wedding;

/// <summary>Runtime couple state (Mobius <c>Couple</c>).</summary>
public sealed class WeddingCouple
{
    public WeddingCouple(WeddingCoupleRecord record)
    {
        Id = record.Id;
        Player1Id = record.Player1Id;
        Player2Id = record.Player2Id;
        Married = record.Married;
        AffianceDateUnixMs = record.AffianceDateUnixMs;
        WeddingDateUnixMs = record.WeddingDateUnixMs;
    }

    public WeddingCouple(int id, int player1Id, int player2Id, long nowUnixMs)
    {
        Id = id;
        Player1Id = player1Id;
        Player2Id = player2Id;
        AffianceDateUnixMs = nowUnixMs;
        WeddingDateUnixMs = nowUnixMs;
    }

    public int Id { get; }
    public int Player1Id { get; }
    public int Player2Id { get; }
    public bool Married { get; private set; }
    public long AffianceDateUnixMs { get; private set; }
    public long WeddingDateUnixMs { get; private set; }

    public int PartnerOf(int objectId) =>
        objectId == Player1Id ? Player2Id : objectId == Player2Id ? Player1Id : 0;

    public bool Contains(int objectId) => objectId == Player1Id || objectId == Player2Id;

    public void Marry(long nowUnixMs)
    {
        Married = true;
        WeddingDateUnixMs = nowUnixMs;
    }

    public WeddingCoupleRecord ToRecord() => new(
        Id,
        Player1Id,
        Player2Id,
        Married,
        AffianceDateUnixMs,
        WeddingDateUnixMs);
}