using Fineage.Game;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryEnchantRequestAsync(ZoneSession session, EnchantRequestMsg request, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryEnchantRequest(session, request.ObjectId, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryEnchantConfirmAsync(ZoneSession session, EnchantConfirmMsg request, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryEnchantConfirm(session, request.ObjectId, toSelf, toOthers);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryEnchantCancelAsync(ZoneSession session, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryEnchantCancel(session, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    string? TryEnchantRequest(ZoneSession session, int objectId, List<Pending> toSelf)
    {
        if (!TryLivePlayer(session, out var player, out var error))
        {
            ClearEnchantIfDead(session, error);
            return error;
        }

        var scroll = player.Inventory!.Find(objectId);
        if (scroll == null)
            return InventoryProtocol.NotOwned;
        if (scroll.Template.Scroll == null)
            return EnchantProtocol.NotScroll;

        player.ActiveEnchantObjectId = scroll.ObjectId;
        toSelf.Add(Pack(session, MsgType.SystemMessage, EnchantLines.Select(scroll)));
        ZoneLog.Info($"enchant object={session.ObjectId} request scroll={scroll.ObjectId} item={scroll.ItemId}");
        return null;
    }

    string? TryEnchantCancel(ZoneSession session, List<Pending> toSelf)
    {
        if (!TryLivePlayer(session, out var player, out var error))
        {
            ClearEnchantIfDead(session, error);
            return error;
        }
        if (player.ActiveEnchantObjectId == 0)
            return EnchantProtocol.NoRequest;

        player.ActiveEnchantObjectId = 0;
        toSelf.Add(Pack(session, MsgType.EnchantResult, new EnchantResultMsg { Result = EnchantProtocol.ResultCancel }));
        toSelf.Add(Pack(session, MsgType.SystemMessage, EnchantLines.Cancelled()));
        ZoneLog.Info($"enchant object={session.ObjectId} cancel");
        return null;
    }

    string? TryEnchantConfirm(ZoneSession session, int objectId, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (!TryLivePlayer(session, out var player, out var error))
        {
            ClearEnchantIfDead(session, error);
            return error;
        }

        var inventory = player.Inventory!;
        if (player.ActiveEnchantObjectId == 0)
            return EnchantProtocol.NoRequest;

        var scroll = inventory.Find(player.ActiveEnchantObjectId);
        player.ActiveEnchantObjectId = 0;
        if (scroll?.Template.Scroll is not { } definition)
            return scroll == null ? InventoryProtocol.NotOwned : EnchantProtocol.NotScroll;

        var target = inventory.Find(objectId);
        if (target == null)
            return InventoryProtocol.NotOwned;
        if (EnchantRules.Reject(target.Template, target.EnchantLevel, definition) is { } denied)
            return DenyCode(denied);

        var before = PaperdollSnapshot(inventory);
        bool wasEquipped = target.IsEquipped;
        int oldLevel = target.EnchantLevel;
        string targetName = target.Template.Name;
        int scrollSlot = SlotOf(scroll);

        int scrollLeft = inventory.TakeOne(scroll);
        var changes = new List<InventoryUpdateEntryMsg>
        {
            scrollLeft == 0
                ? InventoryMessages.RemoveRow(scroll, scrollSlot)
                : InventoryMessages.ChangeRow(InventoryChange.Modify, scroll)
        };

        var roll = EnchantRules.Roll(target.Template, oldLevel, definition, _enchantRates, _enchantRandom);
        int resultCode;
        int reportedLevel;
        int crystalId = 0;
        int crystalCount = 0;
        var lines = new List<SystemMessageMsg>();

        if (roll.Success)
        {
            bool increased = roll.BaseChance > 0;
            if (increased)
                inventory.SetEnchantLevel(target, oldLevel + 1);
            reportedLevel = target.EnchantLevel;
            resultCode = EnchantProtocol.ResultSuccess;
            changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, target));
            lines.Add(oldLevel == 0
                ? EnchantLines.SuccessPlain(targetName)
                : EnchantLines.SuccessLevel(oldLevel, targetName));
        }
        else if (definition.Fail == EnchantFail.Keep)
        {
            reportedLevel = oldLevel;
            resultCode = EnchantProtocol.ResultKeep;
            lines.Add(EnchantLines.Kept());
        }
        else if (definition.Fail == EnchantFail.Reset)
        {
            if (wasEquipped)
                lines.Add(oldLevel > 0
                    ? EnchantLines.EquipmentRemoved(oldLevel, targetName)
                    : EnchantLines.Disarmed(targetName));
            if (wasEquipped)
                inventory.Unequip(target);
            inventory.SetEnchantLevel(target, 0);
            reportedLevel = 0;
            resultCode = EnchantProtocol.ResultReset;
            changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, target));
            lines.Add(EnchantLines.Reset());
        }
        else
        {
            if (wasEquipped)
                lines.Add(oldLevel > 0
                    ? EnchantLines.EquipmentRemoved(oldLevel, targetName)
                    : EnchantLines.Disarmed(targetName));
            crystalCount = EnchantRules.CrystallizedCount(target.Template, oldLevel);
            crystalId = crystalCount > 0 ? EnchantRules.CrystalItemId(target.Template.Crystal) : 0;
            ItemTemplate? crystalTemplate = null;
            if (crystalId == 0 || !_game.Items.TryGet(crystalId, out crystalTemplate))
            {
                crystalId = 0;
                crystalCount = 0;
                crystalTemplate = null;
            }

            int wornSlot = SlotOf(target);
            if (wasEquipped)
                inventory.Unequip(target);
            changes.Add(InventoryMessages.RemoveRow(target, wasEquipped ? -1 : wornSlot));
            inventory.Remove(target);
            reportedLevel = 0;
            resultCode = EnchantProtocol.ResultCrystallize;
            lines.Add(oldLevel > 0
                ? EnchantLines.CrystallizedLevel(oldLevel, targetName)
                : EnchantLines.CrystallizedPlain(targetName));

            if (crystalCount > 0 && crystalTemplate != null)
            {
                bool stacked = inventory.FindByItemId(crystalId) != null;
                var stack = inventory.Add(crystalId, crystalCount);
                changes.Add(InventoryMessages.ChangeRow(stacked ? InventoryChange.Modify : InventoryChange.Add, stack));
                lines.Add(EnchantLines.Earned(crystalCount, crystalTemplate.Name));
            }
        }

        foreach (var other in PaperdollChanges(inventory, before))
        {
            if (other.ObjectId == target.ObjectId || other.ObjectId == scroll.ObjectId)
                continue;
            changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, other));
        }

        toSelf.Add(Pack(session, MsgType.EnchantResult, new EnchantResultMsg
        {
            Result = resultCode,
            EnchantLevel = reportedLevel,
            ObjectId = target.ObjectId,
            CrystalItemId = crystalId,
            CrystalCount = crystalCount
        }));
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg { Changes = [.. changes] }));
        foreach (var line in lines)
            toSelf.Add(Pack(session, MsgType.SystemMessage, line));

        bool levelChangedWhileWorn = target.IsEquipped && target.EnchantLevel != oldLevel;
        bool destroyedWhileWorn = wasEquipped && inventory.Find(target.ObjectId) == null;
        var slotChanges = PaperdollChanges(inventory, before);
        if (levelChangedWhileWorn || destroyedWhileWorn || slotChanges.Count > 0)
        {
            RefreshGradePenalty(player);
            toSelf.Add(Pack(session, MsgType.UserInfo, UserInfo(player)));
            byte[] body = MessagePackSerializer.Serialize(CharInfo(player));
            foreach (int id in player.Knownlist)
            {
                if (_byId.TryGetValue(id, out var other) && other.Session != null)
                    toOthers.Add(new Pending(other.Session, MsgType.CharInfo, body));
            }
        }

        SaveInventory(session, inventory);
        ZoneLog.Info(
            $"enchant object={session.ObjectId} result={resultCode} item={target.ItemId} {oldLevel}->{reportedLevel} crystals={crystalCount}");
        return null;
    }

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
