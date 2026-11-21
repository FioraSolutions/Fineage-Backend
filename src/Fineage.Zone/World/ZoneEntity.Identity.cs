namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    /// <summary>Player identity payload; null until first race/clan/PvP access (#353 composition).</summary>
    public ZonePlayerIdentityPayload? Identity { get; set; }

    public int Race
    {
        get => Identity?.Race ?? 0;
        init => EnsureIdentity().Race = value;
    }

    public int Sex
    {
        get => Identity?.Sex ?? 0;
        init => EnsureIdentity().Sex = value;
    }

    public bool Hero
    {
        get => Identity?.Hero ?? false;
        set => EnsureIdentity().Hero = value;
    }

    public bool Noble
    {
        get => Identity?.Noble ?? false;
        set => EnsureIdentity().Noble = value;
    }

    /// <summary>Mobius <c>getBaseClass</c>: the class the character was created into and advanced.</summary>
    public int BaseClassId
    {
        get => Identity is { BaseClassId: > 0 } i ? i.BaseClassId : ClassId;
        set => EnsureIdentity().BaseClassId = value;
    }

    public int ClassIndex
    {
        get => Identity?.ClassIndex ?? 0;
        set => EnsureIdentity().ClassIndex = value;
    }

    /// <summary>Mobius <c>isSubClassActive</c>.</summary>
    public bool SubClassActive => ClassIndex > 0;

    /// <summary>Parked class slots (Mobius <c>getSubClasses</c> plus the base while a subclass is active).</summary>
    public List<Fineage.Accounts.StoredClassSlot> ClassSlots => EnsureIdentity().ClassSlots;

    public bool InClan
    {
        get => Identity?.InClan ?? false;
        set => EnsureIdentity().InClan = value;
    }

    public bool ClanLeader
    {
        get => Identity?.ClanLeader ?? false;
        set => EnsureIdentity().ClanLeader = value;
    }

    public int PledgeClass
    {
        get => Identity?.PledgeClass ?? 0;
        set => EnsureIdentity().PledgeClass = value;
    }

    public int CastleId
    {
        get => Identity?.CastleId ?? 0;
        set => EnsureIdentity().CastleId = value;
    }

    public int HideoutId
    {
        get => Identity?.HideoutId ?? 0;
        set => EnsureIdentity().HideoutId = value;
    }

    public double HallRegenHpMult
    {
        get => Identity?.HallRegenHpMult ?? 1d;
        set => EnsureIdentity().HallRegenHpMult = value;
    }

    public double HallRegenMpMult
    {
        get => Identity?.HallRegenMpMult ?? 1d;
        set => EnsureIdentity().HallRegenMpMult = value;
    }

    public int PledgeType
    {
        get => Identity?.PledgeType ?? 0;
        set => EnsureIdentity().PledgeType = value;
    }

    public int PowerGrade
    {
        get => Identity?.PowerGrade ?? 0;
        set => EnsureIdentity().PowerGrade = value;
    }

    public int ClanPrivileges
    {
        get => Identity?.ClanPrivileges ?? 0;
        set => EnsureIdentity().ClanPrivileges = value;
    }

    public int ClanCrestId
    {
        get => Identity?.ClanCrestId ?? 0;
        set => EnsureIdentity().ClanCrestId = value;
    }

    public int LargeCrestId
    {
        get => Identity?.LargeCrestId ?? 0;
        set => EnsureIdentity().LargeCrestId = value;
    }

    public int AllyId
    {
        get => Identity?.AllyId ?? 0;
        set => EnsureIdentity().AllyId = value;
    }

    public int AllyCrestId
    {
        get => Identity?.AllyCrestId ?? 0;
        set => EnsureIdentity().AllyCrestId = value;
    }

    public bool ClanWarehouseOpen
    {
        get => Identity?.ClanWarehouseOpen ?? false;
        set => EnsureIdentity().ClanWarehouseOpen = value;
    }

    public bool PvpFlagged
    {
        get => Identity?.PvpFlagged ?? false;
        set => EnsureIdentity().PvpFlagged = value;
    }

    /// <summary>Mobius <c>updatePvPFlag(2)</c>: the flag is in its last 20 s and the name blinks.</summary>
    public bool PvpFlagBlink
    {
        get => Identity?.PvpFlagBlink ?? false;
        set => EnsureIdentity().PvpFlagBlink = value;
    }

    /// <summary>Unix ms when the PvP flag expires (Mobius <c>_pvpFlagLasts</c>).</summary>
    public long PvpFlagUntilMs
    {
        get => Identity?.PvpFlagUntilMs ?? 0;
        set => EnsureIdentity().PvpFlagUntilMs = value;
    }

    public int PkKills
    {
        get => Identity?.PkKills ?? 0;
        set => EnsureIdentity().PkKills = value;
    }

    public int PvpKills
    {
        get => Identity?.PvpKills ?? 0;
        set => EnsureIdentity().PvpKills = value;
    }

    /// <summary>Mobius karma (Chaotic); death item drop uses karma rates when &gt; 0 and PK ≥ limit.</summary>
    public int Karma
    {
        get => Identity?.Karma ?? 0;
        set => EnsureIdentity().Karma = value;
    }

    public int CoupleId
    {
        get => Identity?.CoupleId ?? 0;
        set => EnsureIdentity().CoupleId = value;
    }

    public int PartnerId
    {
        get => Identity?.PartnerId ?? 0;
        set => EnsureIdentity().PartnerId = value;
    }

    public bool Married
    {
        get => Identity?.Married ?? false;
        set => EnsureIdentity().Married = value;
    }

    ZonePlayerIdentityPayload EnsureIdentity() => Identity ??= new ZonePlayerIdentityPayload();

}
