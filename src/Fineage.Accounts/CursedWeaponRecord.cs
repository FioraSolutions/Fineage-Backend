namespace Fineage.Accounts;

/// <summary>Mobius <c>cursed_weapons</c> SQL row plus optional ground object id for a dropped blade.</summary>
public sealed class CursedWeaponRecord
{
    public int ItemId { get; set; }
    public int CharacterId { get; set; }
    public int PlayerReputation { get; set; }
    public int PlayerPkKills { get; set; }
    public int NbKills { get; set; }
    public long EndTimeUnixMs { get; set; }
    public bool Activated { get; set; }
    public bool Dropped { get; set; }
    public int GroundObjectId { get; set; }
}