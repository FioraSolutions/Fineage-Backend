using Fineage.Protocol;

namespace Fineage.Zone.Inventory;

/// <summary>
/// Pure Mobius soulshot / spiritshot charge and auto-enable prechecks.
/// World owns consume, charge state, glow fanout, and auto list mutation.
/// </summary>
public static class ShotRules
{
    public const string NotAShot = "not_a_shot";

    public enum ChargeDeny
    {
        None,
        CannotUse,
        GradeMismatch,
        AlreadyCharged,
        NotEnough,
    }

    /// <summary>
    /// <paramref name="shotsNeeded"/> is the weapon's soulshots/spiritshots count (0 when no usable weapon).
    /// <paramref name="alreadyCharged"/> is true when the matching charge is already set.
    /// <paramref name="haveCount"/> is the inventory stack count for the shot item.
    /// </summary>
    public static ChargeDeny ChargeError(
        int shotsNeeded,
        bool gradeMatches,
        bool alreadyCharged,
        int haveCount)
    {
        if (shotsNeeded <= 0)
            return ChargeDeny.CannotUse;
        if (!gradeMatches)
            return ChargeDeny.GradeMismatch;
        if (alreadyCharged)
            return ChargeDeny.AlreadyCharged;
        if (haveCount < shotsNeeded)
            return ChargeDeny.NotEnough;
        return ChargeDeny.None;
    }

    public static string? AutoEnableError(bool owned, bool isShot) =>
        !owned ? InventoryProtocol.NotOwned : !isShot ? NotAShot : null;
}
