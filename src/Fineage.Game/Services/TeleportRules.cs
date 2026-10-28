namespace Fineage.Game.Services;

/// <summary>
/// Gatekeeper price. Mobius <c>TeleportHolder.calculateFee</c> and <c>PlayerConfig.MAX_FREE_TELEPORT_LEVEL</c>
/// (default 40). A normal list is free at that level and below. Monday and Tuesday from 20:00
/// through 23:59 on the zone clock charge half, truncated toward zero. Other lists, including
/// <c>OTHER</c>, always use the listed fee. Subclass characters are not free; this server has no subclasses.
/// </summary>
public static class TeleportRules
{
    public const int FreeLevel = 40;
    public const int DiscountHour = 20;
    public const string Normal = "NORMAL";
    public const string NoblesAdena = "NOBLES_ADENA";
    public const string NoblesToken = "NOBLES_TOKEN";

    public static bool IsNormal(string? type) =>
        string.Equals(type, Normal, StringComparison.OrdinalIgnoreCase);

    public static bool IsNobles(string? type) =>
        string.Equals(type, NoblesAdena, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, NoblesToken, StringComparison.OrdinalIgnoreCase);

    public static bool DiscountWindow(DateTimeOffset when) =>
        when.Hour >= DiscountHour && when.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday;

    /// <summary>Adena (or the list's fee item) the player pays. 0 is free.</summary>
    public static int Quote(string? type, int level, int feeCount, DateTimeOffset when, bool subClassActive = false)
    {
        if (feeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(feeCount));
        if (!IsNormal(type))
            return feeCount;
        if (!subClassActive && level <= FreeLevel)
            return 0;
        if (DiscountWindow(when))
            return feeCount / 2;
        return feeCount;
    }
}
