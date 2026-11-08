using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    const string AdminHtmlRoot = "admin/";
    const string AdminGmShopPage = "admin/gmshops.htm";
    const string AdminSetClassPage = "admin/setclass/human_fighter.htm";

    /// <summary>
    /// Mobius admin bypasses (<c>admin_gmshop</c>, <c>admin_buy</c>, …) — no NPC / range.
    /// Returns null when handled; non-null error string when refused.
    /// </summary>
    string? TryAdminBypass(ZoneSession session, ZoneEntity player, string command)
    {
        if (!ZoneAdmin.IsAdminCharacter(player.Name))
            return "forbidden";

        string verb = BypassCommands.Token(command);
        string args = ArgsAfter(command, verb);

        if (verb.Equals("admin_gmshop", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_admin", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_admin7", StringComparison.OrdinalIgnoreCase))
            return SendAdminHtml(session, player, AdminGmShopPage);

        if (verb.Equals("admin_html", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(args))
                return SendAdminHtml(session, player, AdminGmShopPage);
            if (!TryNormalizeAdminHtmlPath(args, out string relative))
                return "bad_request";
            return SendAdminHtml(session, player, relative);
        }

        if (verb.Equals("admin_buy", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args.Trim(), out int listId) || listId <= 0)
                return "bad_request";
            return OpenAdminBuyList(session, player, listId);
        }

        if (verb.Equals("admin_create_item", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_give", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_giveitem", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseItemCount(args, out int itemId, out int count))
                return "bad_request";
            return TryGiveItem(session, itemId, count);
        }

        if (verb.Equals("admin_setlevel", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_set_level", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args.Trim(), out int level))
                return "bad_request";
            return AdminSetLevel(session, player, level);
        }

        if (verb.Equals("admin_addlevel", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_add_level", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args.Trim(), out int delta))
                return "bad_request";
            return AdminSetLevel(session, player, player.Level + delta);
        }

        if (verb.Equals("admin_setclass", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(args))
                return SendAdminHtml(session, player, AdminSetClassPage);
            if (!int.TryParse(args.Trim(), out int classId) || classId < 0)
                return "bad_request";
            return AdminSetClass(session, player, classId);
        }

        if (verb.Equals("admin_buff", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_buffs", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuffRequest(session, player.ObjectId, effectId: 0, remove: false);
        }

        if (verb.Equals("admin_buff_clear", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_clearbuffs", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuffRequest(session, player.ObjectId, effectId: 0, remove: true);
        }

        // Unknown admin_* — show shop hub rather than hard-failing HTML clicks we have not wired yet.
        ZoneLog.Info($"admin unknown object={player.ObjectId} cmd={command}");
        return SendAdminHtml(session, player, AdminGmShopPage);
    }

    string? SendAdminHtml(ZoneSession session, ZoneEntity player, string relativePath)
    {
        if (!_html.TryGet(relativePath, out string html) || string.IsNullOrEmpty(html))
        {
            ZoneLog.Info($"admin html missing path={relativePath}");
            return "bad_request";
        }

        string rendered = NpcHtmlText.Apply(
            html,
            new NpcHtmlText.Tokens(0, 0, "GM Shop", player.Name));
        LastNpcHtmlForTest = rendered;
        if (NetBridge != null && session.ObjectId > 0)
        {
            NetBridge.FanoutNpcHtml([session.ObjectId], new NpcHtmlMsg
            {
                NpcObjectId = 0,
                NpcId = 0,
                Html = rendered,
                ItemId = 0
            });
        }

        return null;
    }

    /// <summary>Fulfill an open admin buylist purchase (free rows + paid rows via ShopTrade).</summary>
    string? CompleteAdminBuy(
        ZoneSession session,
        ZoneEntity player,
        Fineage.Game.Shops.BuyListDefinition list,
        ShopLine[] items)
    {
        var free = new List<ShopLine>();
        var paid = new List<ShopLine>();
        foreach (var row in items)
        {
            if (row.ItemId <= 0 || row.Count <= 0)
                return "bad_request";
            var product = list.Find(row.ItemId);
            if (product == null || !_game.Items.TryGet(row.ItemId, out _))
                return "bad_request";
            if (product.Price <= 0)
                free.Add(row);
            else
                paid.Add(row);
        }

        if (paid.Count > 0)
        {
            var mutation = Fineage.Game.Shops.ShopTrade.Buy(
                player.Inventory!,
                _game.Items,
                list,
                paid,
                baseTaxPercent: 0,
                castleTaxPercent: 0,
                MaxLoadOf(player),
                Fineage.Game.Shops.ShopTrade.SlotLimit(player.Race),
                _shopStock,
                _clock().ToUnixTimeMilliseconds());
            if (!mutation.Ok)
            {
                SendDeny(session, mutation);
                return null;
            }

            SendTouches(session, mutation);
        }

        foreach (var row in free)
        {
            string? err = TryGiveItem(session, row.ItemId, row.Count);
            if (err != null)
                return err;
        }

        SendComplete(session);
        FanNetBuy(session, AdminBuyListMsg(list), AdminBalance(player));
        SaveInventory(session, player.Inventory!);
        return null;
    }

    string? OpenAdminBuyList(ZoneSession session, ZoneEntity player, int listId)
    {
        if (!_buyLists.TryGet(listId, out var list))
        {
            ZoneLog.Info($"admin buy missing list={listId}");
            return "bad_request";
        }

        // Mobius AdminShop: BuyList(buyList, adena, 0) — no NPC tax, empty npc allow-list OK.
        player.Shop = new Fineage.Zone.Shops.ShopSession
        {
            NpcObjectId = 0,
            BuyListId = listId,
            IsAdmin = true,
            SellOpen = false
        };
        var buy = AdminBuyListMsg(list);
        var balance = AdminBalance(player);
        FanNetBuy(session, buy, balance);
        ZoneLog.Info($"admin buy object={player.ObjectId} list={listId}");
        return null;
    }

    BuyListMsg AdminBuyListMsg(Fineage.Game.Shops.BuyListDefinition list)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        var items = new BuyListItemMsg[list.Products.Count];
        for (int i = 0; i < items.Length; i++)
        {
            var product = list.Products[i];
            items[i] = new BuyListItemMsg
            {
                ItemId = product.ItemId,
                Price = Math.Max(0, product.Price),
                Count = product.Limited ? _shopStock.Available(list.Id, product.ItemId, now) : -1
            };
        }

        return new BuyListMsg
        {
            NpcObjectId = 0,
            ListId = list.Id,
            BaseTaxPercent = 0,
            CastleTaxPercent = 0,
            Items = items
        };
    }

    ShopBalanceMsg AdminBalance(ZoneEntity player)
    {
        long weight = player.Inventory!.TotalWeight;
        return new ShopBalanceMsg
        {
            Adena = player.Inventory.CountOf(Fineage.Game.Shops.ShopTrade.AdenaItemId),
            Weight = weight > int.MaxValue ? int.MaxValue : (int)weight,
            MaxWeight = MaxLoadOf(player),
            Slots = player.Inventory.Items.Count,
            MaxSlots = Fineage.Game.Shops.ShopTrade.SlotLimit(player.Race),
            BaseTaxPercent = 0,
            CastleTaxPercent = 0
        };
    }

    string? AdminSetLevel(ZoneSession session, ZoneEntity player, int level)
    {
        var table = _game.Classes?.Experience;
        if (table == null)
            return "bad_request";

        int cap = Math.Min(table.MaxLevel, table.HighestRow);
        if (level < 1)
            level = 1;
        if (level > cap)
            level = cap;

        int previous = player.Level;
        player.Level = level;
        player.Exp = table.ExpToBeLevel(level);
        if (_game.Classes != null && _game.Classes.TryGet(player.ClassId, out var template) && template != null)
        {
            player.MaxHp = template.ModifiedHp(level);
            player.MaxMp = template.ModifiedMp(level);
            player.CurHp = player.MaxHp;
            player.CurMp = player.MaxMp;
        }

        if (level > previous)
            ApplyLevelUp(session, player);
        else
        {
            if (level < previous)
            {
                DecreaseSkillsOnDelevel(player);
                RefreshKnownSkills(player);
                RefreshGradePenalty(player);
                RefreshWeightPenalty(player);
            }

            SelfUserInfo(player);
            if (NetBridge != null)
            {
                NetBridge.FanoutSkillList(
                    [player.ObjectId],
                    Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
            }
        }

        _persist.SaveProgress(
            session.Account,
            session.ObjectId,
            player.Level,
            player.Exp,
            player.Sp,
            player.MaxHp,
            player.CurHp,
            player.MaxMp,
            player.CurMp);
        ZoneLog.Info($"admin setlevel object={player.ObjectId} {previous}->{player.Level}");
        return null;
    }

    string? AdminSetClass(ZoneSession session, ZoneEntity player, int classId)
    {
        if (!PlayerClasses.TryGet(classId, out var template) || template == null)
            return "bad_request";

        int previous = player.ClassId;
        player.ClassId = classId;
        session.ChangeClass(classId);
        player.BaseRunSpeed = template.RunSpeed;
        player.BaseWalkSpeed = template.WalkSpeed;
        player.Dex = template.Dex;
        player.MaxHp = template.ModifiedHp(player.Level);
        player.MaxMp = template.ModifiedMp(player.Level);
        player.CurHp = Math.Min(player.CurHp, player.MaxHp);
        player.CurMp = Math.Min(player.CurMp, player.MaxMp);
        RefreshKnownSkills(player);
        FanoutNetMoveType(player);
        FanoutMoveType(player);
        NetBridge?.FanoutSkillList(
            [player.ObjectId],
            Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        SelfUserInfo(player);

        List<int>? netPeers = null;
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                (netPeers ??= []).Add(id);
        }

        if (netPeers != null)
            NetBridge?.FanoutCharInfo(netPeers, player.ObjectId);

        _persist.SaveClass(session.Account, session.ObjectId, classId);
        ZoneLog.Info($"admin setclass object={player.ObjectId} {previous}->{classId}");
        return null;
    }

    static string ArgsAfter(string command, string verb)
    {
        command = command.Trim();
        if (command.Length <= verb.Length)
            return "";
        return command[verb.Length..].TrimStart();
    }

    static bool TryNormalizeAdminHtmlPath(string raw, out string relative)
    {
        relative = "";
        string path = raw.Trim().Replace('\\', '/');
        if (path.StartsWith("bypass -h ", StringComparison.OrdinalIgnoreCase))
            path = path["bypass -h ".Length..].TrimStart();
        if (path.Contains("..", StringComparison.Ordinal) || path.Length == 0)
            return false;
        relative = path.StartsWith("admin/", StringComparison.OrdinalIgnoreCase)
            ? path
            : AdminHtmlRoot + path;
        return true;
    }

    static bool TryParseItemCount(string args, out int itemId, out int count)
    {
        itemId = 0;
        count = 1;
        string[] parts = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || !int.TryParse(parts[0], out itemId) || itemId <= 0)
            return false;
        if (parts.Length >= 2 && (!int.TryParse(parts[1], out count) || count < 1))
            count = 1;
        return true;
    }
}
