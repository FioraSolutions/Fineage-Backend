using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? WithdrawPrivate(ZoneSession session, int npcObjectId, WarehouseLine[]? lines, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Inventory == null || player.Warehouse == null)
            return "world";
        if (WarehouseSessionRules.NpcWindowError(IsWarehouse(npc)) is { } withdrawNpcError)
        {
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return withdrawNpcError;
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        if (!TryReadLines(lines, out var request, out error))
            return error;

        var planned = new List<(WarehouseStack Stack, int Count, bool Stackable)>();
        var seen = new HashSet<int>();
        int slots = 0;
        int incomingWeight = 0;
        foreach (var line in request)
        {
            if (WarehouseSessionRules.LineDuplicateError(!seen.Add(line.ObjectId)) is { } withdrawDup)
                return withdrawDup;
            var stack = player.Warehouse.Find(line.ObjectId);
            if (stack == null || stack.Count < line.Count)
                return "bad_request";
            var template = _game.Items.Get(stack.ItemId);
            if (WarehouseSessionRules.NonStackableCountError(template.Stackable, line.Count) is { } withdrawCount)
                return withdrawCount;
            planned.Add((stack, line.Count, template.Stackable));
            slots += WarehouseRules.SlotsFor(template.Stackable, player.Inventory.FindByItemId(stack.ItemId) != null, line.Count);
            incomingWeight = checked(incomingWeight + _weights.Of(stack.ItemId) * line.Count);
        }

        if (player.Inventory.Items.Count + slots > WarehouseRules.InventorySlots(player.Race))
        {
            Notice(session, NpcServiceProtocol.InventoryFull, NpcServiceProtocol.InventoryFullText);
            return null;
        }

        int carried = _weights.Load(player.Inventory.Items.Select(item => (item.ItemId, item.Count)));
        if ((long)carried + incomingWeight > MaxLoadOf(player))
        {
            Notice(session, NpcServiceProtocol.WeightLimit, NpcServiceProtocol.WeightLimitText);
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        foreach (var (stack, count, stackable) in planned)
        {
            if (!player.Warehouse.TryTake(stack.ObjectId, count, out _, out _))
                return "bad_request";
            bool existed = stackable && player.Inventory.FindByItemId(stack.ItemId) != null;
            var added = player.Inventory.Add(stack.ItemId, count, stack.EnchantLevel);
            changes.Add(InventoryMessages.ChangeRow(existed ? InventoryChange.Modify : InventoryChange.Add, added));
        }

        FanInventory(session, Collapse(changes));
        SaveInventory(session, player.Inventory);
        SaveWarehouse(session, player);
        Notice(session, NpcServiceProtocol.TransactionComplete, NpcServiceProtocol.TransactionCompleteText);
        return null;
    }

    string? ApplySupportMagic(ZoneSession session, int npcObjectId, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;

        var refusal = _newbieBuffs.Evaluate(player.Newbie, player.ClassId, ClassLevelOf(player), player.Level, out var buffs);
        if (refusal != NewbieBuffRefusal.None)
        {
            var (id, text, code) = Refusal(refusal);
            Notice(session, id, text);
            if (html)
            {
                // Mobius SupportMagic shows default/SupportMagicHighLevel.htm / SupportMagicLowLevel.htm.
                string? page = refusal switch
                {
                    NewbieBuffRefusal.LevelHigh => "default/SupportMagicHighLevel.htm",
                    NewbieBuffRefusal.LevelLow => "default/SupportMagicLowLevel.htm",
                    _ => null
                };
                SendHtml(session, player, npc, page != null && _html.TryGet(page, out var mobius)
                    ? mobius
                    : "<html><body>%npcname%:<br>" + text + "</body></html>");
            }

            return null;
        }

        var ids = new int[buffs.Count];
        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            ids[i] = buff.SkillId;
            CastBuff(npc, player, buff.SkillId, buff.SkillLevel, buff.AbnormalLevel, buff.DurationSeconds);
        }

        FanoutAbnormal(player);
        if (html)
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>Supplemental magic has been granted.</body></html>");
        return null;
    }

    string? ShowBlessingPage(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        string page = _html.TryGet("default/BlessingOfProtection.htm", out var html)
            ? html
            : "<html><body>%npcname%:<br><a action=\"bypass -h npc_%objectId%_GiveBlessing\">Receive the Blessing of Protection.</a></body></html>";
        SendHtml(session, player, npc, page);
        return null;
    }

    string? ApplyBlessing(ZoneSession session, int npcObjectId, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;

        var blessing = _newbieBuffs.Blessing;
        if (!_newbieBuffs.CanBless(player.Level, ClassLevelOf(player)))
        {
            Notice(session, NpcServiceProtocol.BlessingRefused, NpcServiceProtocol.BlessingRefusedText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.BlessingRefusedText + "</body></html>");
            return null;
        }

        CastBuff(npc, player, blessing.SkillId, blessing.SkillLevel, blessing.AbnormalLevel, blessing.DurationSeconds);
        FanoutAbnormal(player);
        if (html)
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>The Blessing of Protection is upon you.</body></html>");
        return null;
    }
}
