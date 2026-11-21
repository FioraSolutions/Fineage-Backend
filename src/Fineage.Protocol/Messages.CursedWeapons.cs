namespace Fineage.Protocol;

/// <summary>Opcode <see cref="CursedWeaponProtocol.ListOpcode"/>.</summary>
public sealed class CursedWeaponListMsg
{
    public int[] ItemIds { get; set; } = [];
}

/// <summary>Opcode <see cref="CursedWeaponProtocol.LocationOpcode"/>.</summary>
public sealed class CursedWeaponLocationMsg
{
    public CursedWeaponLocationRowMsg[] Rows { get; set; } = [];
}

public sealed class CursedWeaponLocationRowMsg
{
    public int ItemId { get; set; }
    /// <summary>1 when a player wields the blade; 0 when it lies on the ground.</summary>
    public int Activated { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}