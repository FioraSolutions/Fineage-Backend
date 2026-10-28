namespace Fineage.Zone.World;

/// <summary>
/// Social state equip conditions read. Ordinary characters leave every flag off.
/// Pledge class, castle, and clan hall follow the Interlude condition checks.
/// </summary>
public sealed record PlayerEquipStanding(
    bool Hero = false,
    bool InClan = false,
    bool ClanLeader = false,
    int PledgeClass = 0,
    int CastleId = 0,
    int HideoutId = 0,
    bool PvpFlagged = false,
    int PkKills = 0,
    int ExpertiseBonus = 0);
