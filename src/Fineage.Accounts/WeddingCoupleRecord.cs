namespace Fineage.Accounts;

/// <summary>One row in <c>mods_wedding</c> / Mobius <c>Couple</c>.</summary>
public sealed record WeddingCoupleRecord(
    int Id,
    int Player1Id,
    int Player2Id,
    bool Married,
    long AffianceDateUnixMs,
    long WeddingDateUnixMs);