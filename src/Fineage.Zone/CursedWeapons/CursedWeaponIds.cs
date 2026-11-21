using Fineage.Protocol;

namespace Fineage.Zone.CursedWeapons;

/// <summary>Retail Interlude cursed blade item ids (Olympiad gate + future <c>CursedWeaponsManager</c>).</summary>
public static class CursedWeaponIds
{
    public static ReadOnlySpan<int> All => [OlympiadProtocol.Zariche, OlympiadProtocol.Akamanah];

    public static bool IsCursedItem(int itemId) =>
        itemId is OlympiadProtocol.Zariche or OlympiadProtocol.Akamanah;
}