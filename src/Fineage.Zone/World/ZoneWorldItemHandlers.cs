using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Items;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>Book.useItem</c>.</summary>
    internal void UseBookItem(ZoneSession session, ZoneEntity player, int itemId)
    {
        _ = player;
        string path = $"help/{itemId}.htm";
        string html;
        if (_html.TryGet(path, out var content) && !string.IsNullOrWhiteSpace(content))
        {
            html = content;
        }
        else
        {
            ZoneLog.Error($"[item-handler] Book help page missing data/html/{path}");
            html = string.Format(ItemHandlerProtocol.BookMissingHtml, itemId);
        }

        NetBridge?.FanoutNpcHtml([session.ObjectId], new NpcHtmlMsg
        {
            NpcObjectId = 0,
            ItemId = itemId,
            Html = html
        });
    }

    /// <summary>Mobius <c>Maps.useItem</c>.</summary>
    internal void UseMapItem(ZoneSession session, int itemId)
    {
        NetBridge?.FanoutShowMiniMap([session.ObjectId], new ShowMiniMapMsg
        {
            MapId = itemId,
            SevenSignsPeriod = (int)_sevenSigns.Engine.Period
        });
    }

    /// <summary>Mobius <c>Calculator.useItem</c>.</summary>
    internal void UseCalculatorItem(ZoneSession session, int itemId) =>
        NetBridge?.FanoutShowCalculator([session.ObjectId], new ShowCalculatorMsg { ItemId = itemId });

    /// <summary>Mobius <c>SpecialXMas.useItem</c>.</summary>
    internal void UseXMasSealItem(ZoneSession session, int itemId) =>
        NetBridge?.FanoutShowXMasSeal([session.ObjectId], new ShowXMasSealMsg { ItemId = itemId });

    /// <summary>Mobius <c>RollingDice.useItem</c>.</summary>
    internal void UseDiceItem(ZoneSession session, ZoneEntity player, int itemId)
    {
        if (PlayerInOlympiad(player))
        {
            SelfLine(session, ItemHandlerProtocol.OlympiadRefuse, ItemHandlerProtocol.OlympiadRefuseText);
            return;
        }

        long now = NowUnixMs();
        if (player.DiceReadyAtMs > now)
        {
            SelfLine(session, ItemHandlerProtocol.DiceNotReady, ItemHandlerProtocol.DiceNotReadyText);
            return;
        }

        int number = 1 + _serviceRandom.For(_regions.RegionOf(player.InstanceId, player.X, player.Y)).Next(6);
        player.DiceReadyAtMs = now + ItemHandlerProtocol.DiceCooldownMs;

        double angle = player.Heading * 360.0 / 65536.0;
        double radian = angle * Math.PI / 180.0;
        double course = Math.PI;
        int x1 = (int)(Math.Cos(Math.PI + radian + course) * 40);
        int y1 = (int)(Math.Sin(Math.PI + radian + course) * 40);
        int tx = player.X + x1;
        int ty = player.Y + y1;
        var (dx, dy, dz) = _geo.MoveCheck(player.X, player.Y, player.Z, tx, ty, player.Z, out _);

        var dice = new DiceMsg
        {
            ObjectId = player.ObjectId,
            ItemId = itemId,
            Number = number,
            X = dx,
            Y = dy,
            Z = dz
        };
        NetBridge?.FanoutDice(WatchersOf(player), dice);

        string text = string.Format(ItemHandlerProtocol.DiceRolledText, player.Name, number);
        SelfLine(session, ItemHandlerProtocol.DiceRolled, text, player.Name, number.ToString());

        var line = new SystemMessageMsg
        {
            Id = ItemHandlerProtocol.DiceRolled,
            Params = [player.Name, number.ToString()],
            Text = text
        };
        // Mobius: peace zone → known players; otherwise party members.
        if (Peace.Contains(player.X, player.Y, player.Z))
        {
            var peers = WatchersOf(player).Where(id => id != player.ObjectId).ToArray();
            if (peers.Length > 0)
                NetBridge?.FanoutSystemMessage(peers, line);
        }
        else if (_party.Of(player.ObjectId) is { } party)
        {
            var members = party.MemberIds
                .Where(id => id != player.ObjectId && _byId.ContainsKey(id))
                .ToArray();
            if (members.Length > 0)
                NetBridge?.FanoutSystemMessage(members, line);
        }
    }

    string? TryNamedItemHandler(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        var call = new ItemHandlerCall(session, player, item, this);
        if (!_itemHandlers.TryDispatch(call, out var result))
            return null;
        return result.Error;
    }
}
