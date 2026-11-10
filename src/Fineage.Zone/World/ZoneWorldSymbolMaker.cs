using System.Text;
using Fineage.Game;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>ai.others.SymbolMaker</c>: HTML pages + Draw/Remove wear list (Net has no HennaEquipList yet).
    /// </summary>
    string? SymbolMakerBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string evt)
    {
        string e = (evt ?? "").Trim();
        if (e.Length == 0 || e.Equals("symbol_maker.htm", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(session, player, npc, ReadSymbolMakerHtml("symbol_maker.htm"));
            return null;
        }

        if (e.Equals("symbol_maker-1.htm", StringComparison.OrdinalIgnoreCase) ||
            e.Equals("symbol_maker-2.htm", StringComparison.OrdinalIgnoreCase) ||
            e.Equals("symbol_maker-3.htm", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(session, player, npc, ReadSymbolMakerHtml(e));
            return null;
        }

        if (e.Equals("Draw", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(session, player, npc, BuildHennaDrawHtml(player));
            return null;
        }

        if (e.Equals("Remove", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(session, player, npc, BuildHennaRemoveHtml(player));
            return null;
        }

        if (e.StartsWith("Wear ", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(e.AsSpan(5).Trim(), out int dyeId) || dyeId <= 0)
                return "bad_request";
            return WearHennaFromNpc(session, player, npc, dyeId);
        }

        if (e.StartsWith("Cancel ", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(e.AsSpan(7).Trim(), out int slot) || slot is < 0 or > 2)
                return "bad_request";
            if (!TryCancelHenna(player, slot))
            {
                ZoneLog.Warn($"henna cancel failed object={player.ObjectId} slot={slot}");
                SendHtml(session, player, npc, "<html><body>That symbol slot is empty.</body></html>");
                return null;
            }

            ZoneLog.Info($"henna cancel object={player.ObjectId} slot={slot}");
            SelfUserInfo(player);
            SendHtml(session, player, npc, BuildHennaRemoveHtml(player));
            return null;
        }

        ZoneLog.Warn($"symbol-maker unknown event object={player.ObjectId} evt={e}");
        return "bad_request";
    }

    string? WearHennaFromNpc(ZoneSession session, ZoneEntity player, ZoneEntity npc, int dyeId)
    {
        if (!_henna.TryGet(dyeId, out var dye))
            throw new InvalidOperationException($"henna.json missing dyeId={dyeId}");
        if (dye.ClassIds.Count > 0 && !dye.ClassIds.Contains(player.ClassId))
        {
            SendHtml(session, player, npc, "<html><body>That dye is not for your class.</body></html>");
            return null;
        }

        if (player.Inventory == null)
            throw new InvalidOperationException($"player {player.ObjectId} has no inventory for henna wear.");

        int have = player.Inventory.CountOf(dye.ItemId);
        if (have < dye.WearCount)
        {
            ZoneLog.Warn(
                $"henna wear refuse object={player.ObjectId} dye={dyeId} item={dye.ItemId} " +
                $"need={dye.WearCount} have={have}");
            SendHtml(
                session, player, npc,
                $"<html><body>You need {dye.WearCount} dye item(s) (id {dye.ItemId}). You have {have}.</body></html>");
            return null;
        }

        if (!player.Inventory.TryConsume(dye.ItemId, dye.WearCount, out _, out _))
            throw new InvalidOperationException(
                $"henna consume failed object={player.ObjectId} item={dye.ItemId} count={dye.WearCount}");

        if (!TryWearHenna(player, dyeId, out int slot))
        {
            // Restore dye — fail loud rather than lose items into a full tattoo bar.
            player.Inventory.Add(dye.ItemId, dye.WearCount);
            ZoneLog.Warn($"henna wear no free slot object={player.ObjectId} dye={dyeId}");
            SendHtml(session, player, npc, "<html><body>You already wear three symbols.</body></html>");
            return null;
        }

        ZoneLog.Info(
            $"henna wear object={player.ObjectId} dye={dyeId} slot={slot} " +
            $"str={player.HennaStr} int={player.HennaInt} wit={player.HennaWit} men={player.HennaMen}");
        SelfUserInfo(player);
        SendHtml(session, player, npc, BuildHennaDrawHtml(player));
        return null;
    }

    string BuildHennaDrawHtml(ZoneEntity player)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>Choose a dye to draw:<br><br>");
        int listed = 0;
        foreach (var dye in _henna.ForClass(player.ClassId).OrderBy(d => d.DyeId))
        {
            // Keep the first talkable page short; STR/INT dyes are enough for cast testing.
            if (listed >= 24)
                break;
            int have = player.Inventory?.CountOf(dye.ItemId) ?? 0;
            sb.Append("<a action=\"bypass -h Quest SymbolMaker Wear ")
                .Append(dye.DyeId)
                .Append("\">#")
                .Append(dye.DyeId)
                .Append(' ')
                .Append(FormatHennaDelta(dye))
                .Append(" (item ")
                .Append(dye.ItemId)
                .Append('×')
                .Append(dye.WearCount)
                .Append(", have ")
                .Append(have)
                .Append(")</a><br>");
            listed++;
        }

        if (listed == 0)
            sb.Append("No dyes for your class in henna.json.<br>");
        sb.Append("<br><a action=\"bypass -h Quest SymbolMaker symbol_maker.htm\">Return</a></body></html>");
        return sb.ToString();
    }

    string BuildHennaRemoveHtml(ZoneEntity player)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>Worn symbols:<br><br>");
        AppendHennaSlot(sb, player, 0, player.HennaDye1);
        AppendHennaSlot(sb, player, 1, player.HennaDye2);
        AppendHennaSlot(sb, player, 2, player.HennaDye3);
        sb.Append("<br><a action=\"bypass -h Quest SymbolMaker symbol_maker.htm\">Return</a></body></html>");
        return sb.ToString();
    }

    void AppendHennaSlot(StringBuilder sb, ZoneEntity player, int slot, int dyeId)
    {
        if (dyeId <= 0)
        {
            sb.Append("Slot ").Append(slot + 1).Append(": empty<br>");
            return;
        }

        string label = _henna.TryGet(dyeId, out var dye) ? FormatHennaDelta(dye) : $"dye {dyeId}";
        sb.Append("Slot ").Append(slot + 1).Append(": ").Append(label)
            .Append(" <a action=\"bypass -h Quest SymbolMaker Cancel ").Append(slot)
            .Append("\">Remove</a><br>");
    }

    static string FormatHennaDelta(HennaDefinition dye)
    {
        var parts = new List<string>(3);
        if (dye.Str != 0) parts.Add($"STR{(dye.Str > 0 ? "+" : "")}{dye.Str}");
        if (dye.Con != 0) parts.Add($"CON{(dye.Con > 0 ? "+" : "")}{dye.Con}");
        if (dye.Dex != 0) parts.Add($"DEX{(dye.Dex > 0 ? "+" : "")}{dye.Dex}");
        if (dye.Int != 0) parts.Add($"INT{(dye.Int > 0 ? "+" : "")}{dye.Int}");
        if (dye.Men != 0) parts.Add($"MEN{(dye.Men > 0 ? "+" : "")}{dye.Men}");
        if (dye.Wit != 0) parts.Add($"WIT{(dye.Wit > 0 ? "+" : "")}{dye.Wit}");
        return parts.Count == 0 ? dye.Name : string.Join(' ', parts);
    }

    string ReadSymbolMakerHtml(string fileName)
    {
        if (_html.TryGet($"symbolmaker/{fileName}", out var html))
            return html;
        if (_html.TryGet($"default/31046.htm", out html) &&
            fileName.Equals("symbol_maker.htm", StringComparison.OrdinalIgnoreCase))
            return html;
        throw new FileNotFoundException($"Missing Symbol Maker HTML symbolmaker/{fileName}");
    }

}
