namespace Fineage.Net.Contract;

public sealed class CursedWeaponListNetMsg
{
    public int[] ItemIds { get; set; } = [];
}

public sealed class CursedWeaponLocationNetMsg
{
    public CursedWeaponLocationRowNet[] Rows { get; set; } = [];
}

public sealed class CursedWeaponLocationRowNet
{
    public int ItemId { get; set; }
    public int Activated { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}