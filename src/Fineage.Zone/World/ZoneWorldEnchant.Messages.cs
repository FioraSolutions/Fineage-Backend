using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void ClearEnchantIfDead(ZoneSession session, string? error)
    {
        if (error != InventoryProtocol.Dead)
            return;
        if (_byId.TryGetValue(session.ObjectId, out var dead))
            dead.ActiveEnchantObjectId = 0;
    }

    static int SlotOf(ItemInstance item) => item.EquippedSlot is { } slot ? (int)slot : -1;

    static string DenyCode(EnchantDeny denied) => denied switch
    {
        EnchantDeny.NotEnchantable => EnchantProtocol.NotEnchantable,
        EnchantDeny.SlotMismatch => EnchantProtocol.SlotMismatch,
        EnchantDeny.MaxEnchant => EnchantProtocol.MaxEnchant,
        EnchantDeny.GradeMismatch => EnchantProtocol.GradeMismatch,
        _ => EnchantProtocol.NotEnchantable
    };
}

static class EnchantLines
{
    public static SystemMessageMsg Select(ItemInstance scroll) => new()
    {
        Id = EnchantProtocol.SelectItem,
        Params = [scroll.ObjectId.ToString(), scroll.ItemId.ToString()],
        Text = "Select an item to enchant."
    };

    public static SystemMessageMsg Cancelled() => new()
    {
        Id = EnchantProtocol.Cancelled,
        Params = [],
        Text = "You have cancelled the enchanting process."
    };

    public static SystemMessageMsg SuccessPlain(string name) => new()
    {
        Id = EnchantProtocol.SuccessPlain,
        Params = [name],
        Text = $"Your {name} has been successfully enchanted."
    };

    public static SystemMessageMsg SuccessLevel(int level, string name) => new()
    {
        Id = EnchantProtocol.SuccessLevel,
        Params = [level.ToString(), name],
        Text = $"Your +{level} {name} has been successfully enchanted."
    };

    public static SystemMessageMsg Kept() => new()
    {
        Id = EnchantProtocol.Kept,
        Params = [],
        Text = "Enchant failed. The enchant value of the item will be retained."
    };

    public static SystemMessageMsg Reset() => new()
    {
        Id = EnchantProtocol.Reset,
        Params = [],
        Text = "Blessed enchant failed. The enchant value of the item became 0."
    };

    public static SystemMessageMsg EquipmentRemoved(int level, string name) => new()
    {
        Id = EnchantProtocol.EquipmentRemoved,
        Params = [level.ToString(), name],
        Text = $"The equipment +{level} {name} has been removed."
    };

    public static SystemMessageMsg Disarmed(string name) => new()
    {
        Id = EnchantProtocol.Disarmed,
        Params = [name],
        Text = $"{name} has been disarmed."
    };

    public static SystemMessageMsg CrystallizedLevel(int level, string name) => new()
    {
        Id = EnchantProtocol.CrystallizedLevel,
        Params = [level.ToString(), name],
        Text = $"The enchantment has failed. Your +{level} {name} has been crystallized."
    };

    public static SystemMessageMsg CrystallizedPlain(string name) => new()
    {
        Id = EnchantProtocol.CrystallizedPlain,
        Params = [name],
        Text = $"The enchantment has failed. Your {name} has been crystallized."
    };

    public static SystemMessageMsg Earned(int count, string crystalName) => new()
    {
        Id = EnchantProtocol.Earned,
        Params = [count.ToString(), crystalName],
        Text = $"You have earned {count} {crystalName}."
    };
}
