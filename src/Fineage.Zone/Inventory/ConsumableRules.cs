using Fineage.Protocol;

namespace Fineage.Zone.Inventory;

/// <summary>
/// Pure Mobius consumable prechecks (known skill list / item reuse / escape cast gate).
/// World owns effect application and consume on the simulation thread.
/// </summary>
public static class ConsumableRules
{
    /// <summary>Returned when the item's own reuse delay has not expired yet.</summary>
    public const string InventoryReuse = "item_reuse";

    public static string? UsePrecheckError(bool skillListKnown, bool onItemReuse) =>
        !skillListKnown
            ? InventoryProtocol.NotEquippable
            : onItemReuse
                ? InventoryReuse
                : null;

    public const string EscapeNoClanHall = "escape_no_clan_hall";

    public static string? EscapeTownError(bool dead, bool casting) =>
        dead ? InventoryProtocol.Dead : casting ? "busy" : null;

    public static string? EscapeClanHallError(bool dead, bool casting, bool hasHall) =>
        dead ? InventoryProtocol.Dead : casting ? "busy" : !hasHall ? EscapeNoClanHall : null;

    /// <summary>Mobius SOE hit times: blessed (2036 / 2177) is near-instant; normal SOE is 20s.</summary>
    public static int EscapeHitTimeMs(int skillId) => skillId is 2036 or 2177 ? 200 : 20_000;

    public static int PercentHeal(int current, int max, int percent)
    {
        if (max <= 0 || percent <= 0)
            return 0;
        int room = Math.Max(0, max - Math.Clamp(current, 0, max));
        return (int)Math.Min((long)max * percent / 100, room);
    }
}
