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

    public bool PvpFlagged
    {
        get => Identity?.PvpFlagged ?? false;
        set => EnsureIdentity().PvpFlagged = value;
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

    ZonePlayerIdentityPayload EnsureIdentity() => Identity ??= new ZonePlayerIdentityPayload();

}
