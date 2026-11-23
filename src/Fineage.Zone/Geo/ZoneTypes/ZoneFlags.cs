namespace Fineage.Zone.Geo.ZoneTypes;

/// <summary>Flags gathered by every zone-type profile for one point. Profiles only set their own fields.</summary>
public sealed class ZoneFlags
{
    public bool BlocksStore { get; set; }
    public bool BlocksSummon { get; set; }
    public bool BlocksRestart { get; set; }
    public bool BlocksBookmark { get; set; }
    public bool BlocksItemDrop { get; set; }
    public bool InDerby { get; set; }
    public bool InDanger { get; set; }
    public bool InBoss { get; set; }
    public int TaxCastleId { get; set; }
    public int TownId { get; set; }
    public int TownTaxCastleId { get; set; }
    public int RestartTimeMs { get; set; }
    public int RestartAllowedMs { get; set; }
    public string? ScriptZone { get; set; }
    public string? RespawnPoint { get; set; }
    public string? RespawnZone { get; set; }
    public bool RespawnRaceMissing { get; set; }
}

public readonly record struct ZoneRacePoint(string Race, string Point);

public readonly record struct ZoneSpawn(int X, int Y, int Z);
