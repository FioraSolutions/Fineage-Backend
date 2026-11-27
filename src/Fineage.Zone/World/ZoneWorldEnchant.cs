using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryEnchantRequestAsync(ZoneSession session, int scrollObjectId, CancellationToken ct)
    {
        // Enchant confirm saves inventory; request/cancel stay under the same deferred pattern (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => TryEnchantRequest(session, scrollObjectId)));
    }

    public Task<string?> TryEnchantConfirmAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        return Task.FromResult(WithDeferredInventoryPersist(() => TryEnchantConfirm(session, targetObjectId)));
    }

    public Task<string?> TryEnchantCancelAsync(ZoneSession session, CancellationToken ct)
    {
        return Task.FromResult(WithDeferredInventoryPersist(() => TryEnchantCancel(session)));
    }

    string? TryEnchantRequest(ZoneSession session, int objectId)
    {
        if (!TryLivePlayer(session, out var player, out var error))
        {
            ClearEnchantIfDead(session, error);
            return error;
        }

        var scroll = player.Inventory!.Find(objectId);
        if (EnchantSessionRules.RequestError(scroll != null, scroll?.Template.Scroll != null) is { } requestError)
            return requestError;
        // Mobius EnchantScrolls.onItemUse: not while casting, and one scroll at a time.
        if (IsCasting(player, NowUnixMs()))
            return EnchantProtocol.Casting;
        if (player.ActiveEnchantObjectId != 0 && player.ActiveEnchantObjectId != scroll!.ObjectId &&
            player.Inventory.Find(player.ActiveEnchantObjectId) != null)
        {
            NetBridge?.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
            {
                Id = 0,
                Text = "Another enchantment is in progress. Please complete the previous task, then try again.",
                Params = []
            });
            return EnchantProtocol.Busy;
        }

        player.ActiveEnchantObjectId = scroll!.ObjectId;
        var select = EnchantLines.Select(scroll);
        NetBridge?.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
        {
            Id = select.Id,
            Text = select.Text ?? "",
            Params = select.Params ?? []
        });
        ZoneLog.Info($"enchant object={session.ObjectId} request scroll={scroll.ObjectId} item={scroll.ItemId}");
        return null;
    }

    string? TryEnchantCancel(ZoneSession session)
    {
        if (!TryLivePlayer(session, out var player, out var error))
        {
            ClearEnchantIfDead(session, error);
            return error;
        }
        if (EnchantSessionRules.CancelError(player.ActiveEnchantObjectId != 0) is { } cancelError)
            return cancelError;

        player.ActiveEnchantObjectId = 0;
        var cancelled = EnchantLines.Cancelled();
        NetBridge?.FanoutEnchantResult([session.ObjectId], new EnchantResultMsg
        {
            Result = EnchantProtocol.ResultCancel
        });
        NetBridge?.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
        {
            Id = cancelled.Id,
            Text = cancelled.Text ?? "",
            Params = cancelled.Params ?? []
        });
        ZoneLog.Info($"enchant object={session.ObjectId} cancel");
        return null;
    }

    string? TryEnchantConfirm(ZoneSession session, int objectId)
    {
        if (!TryLivePlayer(session, out var player, out var error))
        {
            ClearEnchantIfDead(session, error);
            return error;
        }

        var inventory = player.Inventory!;
        var scroll = inventory.Find(player.ActiveEnchantObjectId);
        var target = inventory.Find(objectId);
        if (EnchantSessionRules.ConfirmError(
                player.ActiveEnchantObjectId != 0,
                scroll != null,
                scroll?.Template.Scroll != null,
                target != null) is { } confirmError)
        {
            // Mobius clears the pending request when confirm is answered (even on reject).
            player.ActiveEnchantObjectId = 0;
            return confirmError;
        }

        player.ActiveEnchantObjectId = 0;
        // Mobius RequestEnchantItem: no enchanting from a private store / workshop or an open trade.
        if (player.PrivateStoreType != PrivateStoreProtocol.TypeNone || _tradeByPlayer.ContainsKey(player.ObjectId))
        {
            EnchantRefused(session, EnchantProtocol.StoreOpen, "You cannot enchant while operating a Private Store or Private Workshop.");
            return EnchantProtocol.Store;
        }

        var definition = scroll!.Template.Scroll!;
        if (EnchantRules.Reject(target!.Template, target.EnchantLevel, definition) is { } denied)
        {
            EnchantRefused(session, EnchantProtocol.InappropriateConditions, "Inappropriate enchant conditions.");
            return DenyCode(denied);
        }

        var before = PaperdollSnapshot(inventory);
        bool wasEquipped = target.IsEquipped;
        int oldLevel = target.EnchantLevel;
        string targetName = target.Template.Name;
        int scrollSlot = SlotOf(scroll);
        int scrollId = scroll.ItemId;
        int resultCode = 0;
        int reportedLevel = 0;
        int crystalId = 0;
        int crystalCount = 0;

        RecordPlayerBagChange(
            player,
            LedgerTxKind.Enchant,
            LedgerAccount.Of(SystemAccount.Destroyed),
            () =>
            {
                int scrollLeft = inventory.TakeOne(scroll);
                var changes = new List<InventoryUpdateEntryMsg>
        {
            scrollLeft == 0
                ? InventoryMessages.RemoveRow(scroll, scrollSlot)
                : InventoryMessages.ChangeRow(InventoryChange.Modify, scroll)
        };

                var roll = EnchantRules.Roll(
                    target.Template, oldLevel, definition, _enchantRates, new StreamEnchantRandom(Rng(_enchantRandom, player)));
                crystalId = 0;
                crystalCount = 0;
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
                    // No-grade gear (crystalId 0) yields nothing; a graded crystal id must exist in items.json.
                    ItemTemplate? crystalTemplate = crystalId == 0 ? null : _game.Items.Require(crystalId);
                    if (crystalTemplate == null)
                        crystalCount = 0;

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

                NetBridge?.FanoutEnchantResult([session.ObjectId], new EnchantResultMsg
                {
                    Result = resultCode,
                    EnchantLevel = reportedLevel,
                    ObjectId = target.ObjectId,
                    CrystalItemId = crystalId,
                    CrystalCount = crystalCount
                });
                foreach (var line in lines)
                {
                    NetBridge?.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
                    {
                        Id = line.Id,
                        Text = line.Text ?? "",
                        Params = line.Params ?? []
                    });
                }

                bool levelChangedWhileWorn = target.IsEquipped && target.EnchantLevel != oldLevel;
                bool destroyedWhileWorn = wasEquipped && inventory.Find(target.ObjectId) == null;
                var slotChanges = PaperdollChanges(inventory, before);
                if (levelChangedWhileWorn || destroyedWhileWorn || slotChanges.Count > 0)
                {
                    RefreshGradePenalty(player);
                    SelfUserInfo(player);
                    // Mobius broadcastUserInfo: peers must see the new paperdoll after an equip change, so this has to
                    // reach Net too — the V2 Pending fanout alone left Net peers looking at the pre-enchant gear.
                    List<int>? netPeers = null;
                    foreach (int id in player.Knownlist)
                    {
                        if (_byId.TryGetValue(id, out var other) && other.Session != null)
                        {
                            (netPeers ??= []).Add(id);
                        }
                    }

                    if (netPeers != null && NetBridge != null)
                        NetBridge.FanoutCharInfo(netPeers, player.ObjectId);
                }

                SaveInventory(session, inventory);
                return true;
            },
            _ => scrollId);

        if (resultCode == EnchantProtocol.ResultSuccess && inventory.Find(target.ObjectId) is { } enchanted &&
            enchanted.EnchantLevel != oldLevel &&
            EnchantProtocol.Announced(enchanted.Template.Kind == ItemKind.Weapon, enchanted.EnchantLevel))
            AnnounceEnchant(player, enchanted);

        ZoneLog.Info(
            $"enchant object={session.ObjectId} result={resultCode} item={target.ItemId} {oldLevel}->{reportedLevel} crystals={crystalCount}");
        return null;
    }

    /// <summary>Mobius: <c>EnchantResult(0)</c> closes the window, then the reason line.</summary>
    void EnchantRefused(ZoneSession session, int lineId, string text)
    {
        NetBridge?.FanoutEnchantResult([session.ObjectId], new EnchantResultMsg { Result = EnchantProtocol.ResultCancel });
        NetBridge?.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg { Id = lineId, Text = text, Params = [] });
    }

    /// <summary>
    /// Mobius <c>broadcastMessage</c> + Firework (2024) <c>MagicSkillUse</c> for armor +6 and weapon +7 / +15:
    /// the enchanter and every player who knows them.
    /// </summary>
    void AnnounceEnchant(ZoneEntity player, ItemInstance item)
    {
        var watchers = new List<int> { player.ObjectId };
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Kind == ZoneEntityKind.Player && other.Session != null)
                watchers.Add(id);
        }

        NetBridge?.FanoutSystemMessage(watchers, new SystemMessageMsg
        {
            Id = 0,
            Text = $"{player.Name} has successfully enchanted a +{item.EnchantLevel} {item.Template.Name}.",
            Params = []
        });
        var cast = BuildCast(player, player, EnchantProtocol.FireworkSkillId, 1, 0, 0);
        NetBridge?.FanoutMagicSkillUse(watchers, cast);
    }
}
