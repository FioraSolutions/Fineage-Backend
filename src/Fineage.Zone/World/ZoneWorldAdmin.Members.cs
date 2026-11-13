using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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

    /// <summary>Apply a chat GM buff pack (<c>//mbuff</c> / <c>//fbuff</c>) at each skill's max level.</summary>
    string? AdminApplyBuffSet(ZoneSession session, ZoneEntity player, int[] skillIds)
    {
        if (skillIds == null || skillIds.Length == 0)
            return "bad_request";

        long now = _clock().ToUnixTimeMilliseconds();
        int applied = 0;
        foreach (int skillId in skillIds)
        {
            if (skillId <= 0)
                continue;
            var skill = _game.Skills.Find(skillId)
                ?? throw new InvalidOperationException($"admin buffset missing skillId={skillId}");
            int level = Math.Max(1, skill.MaxLevel);
            if (!skill.TryLevelData(level, out var data))
                throw new InvalidOperationException($"admin buffset skill={skillId} missing level={level}");
            ApplyAbnormalFromSkill(
                skill, data, player, now, force: true,
                durationSeconds: Fineage.Zone.Combat.ZoneBuffs.AdminPackDurationSeconds);
            applied++;
        }

        if (applied == 0)
            throw new InvalidOperationException("admin buffset applied zero skills");

        ZoneLog.Info($"admin buffset object={player.ObjectId} applied={applied}");
        return null;
    }
}
