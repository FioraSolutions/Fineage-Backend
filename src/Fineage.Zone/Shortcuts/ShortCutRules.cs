using Fineage.Accounts;

namespace Fineage.Zone.Shortcuts;

/// <summary>
/// Pure Mobius hotbar slot checks. World owns bag/skill lookups and persistence under the gate.
/// </summary>
public static class ShortCutRules
{
    public static int SlotCapacity => StoredShortcut.SlotsPerPage * StoredShortcut.MaxPages;

    public static string? SlotError(int worldSlot) =>
        worldSlot < 0 || worldSlot >= SlotCapacity ? "shortcut_slot" : null;

    /// <summary>
    /// Refusal after a <see cref="StoredShortcut"/> row is built. Caller supplies ownership / known-skill flags.
    /// </summary>
    public static string? RegisterError(bool rowValid, int type, bool itemOwned, bool skillKnown)
    {
        if (!rowValid)
            return "shortcut_invalid";
        if (type == StoredShortcut.TypeItem && !itemOwned)
            return Fineage.Protocol.InventoryProtocol.NotOwned;
        if (type == StoredShortcut.TypeSkill && !skillKnown)
            return "skill_unknown";
        return null;
    }
}
