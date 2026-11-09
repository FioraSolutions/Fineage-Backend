using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Shortcuts;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ShortCutRulesTests
{
    [Fact]
    public void Slot_capacity_is_pages_times_twelve()
    {
        Assert.Equal(StoredShortcut.SlotsPerPage * StoredShortcut.MaxPages, ShortCutRules.SlotCapacity);
        Assert.Equal("shortcut_slot", ShortCutRules.SlotError(-1));
        Assert.Equal("shortcut_slot", ShortCutRules.SlotError(ShortCutRules.SlotCapacity));
        Assert.Null(ShortCutRules.SlotError(0));
        Assert.Null(ShortCutRules.SlotError(ShortCutRules.SlotCapacity - 1));
    }

    [Fact]
    public void Register_rejects_invalid_unowned_item_and_unknown_skill()
    {
        Assert.Equal("shortcut_invalid", ShortCutRules.RegisterError(false, StoredShortcut.TypeAction, true, true));
        Assert.Equal(
            InventoryProtocol.NotOwned,
            ShortCutRules.RegisterError(true, StoredShortcut.TypeItem, false, true));
        Assert.Equal(
            "skill_unknown",
            ShortCutRules.RegisterError(true, StoredShortcut.TypeSkill, true, false));
        Assert.Null(ShortCutRules.RegisterError(true, StoredShortcut.TypeAction, false, false));
        Assert.Null(ShortCutRules.RegisterError(true, StoredShortcut.TypeItem, true, false));
        Assert.Null(ShortCutRules.RegisterError(true, StoredShortcut.TypeSkill, false, true));
    }
}
