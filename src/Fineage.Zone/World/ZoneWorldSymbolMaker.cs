using System.Text;
using Fineage.Game;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static readonly HashSet<string> SymbolMakerPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "symbol_maker.htm",
        "symbol_maker-1.htm",
        "symbol_maker-2.htm",
        "symbol_maker-3.htm"
    };

    /// <summary>
    /// Mobius <c>ai.others.SymbolMaker</c> script events.
    /// Draw/Remove use HTML lists until Net carries HennaEquipList / HennaRemoveList.
    /// </summary>
    string? SymbolMakerBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string evt)
    {
        string e = (evt ?? "").Trim();
        if (e.Length == 0)
            e = "symbol_maker.htm";

        if (SymbolMakerPages.Contains(e))
        {
            SendHtml(session, player, npc, ReadSymbolMakerHtml(e));
            return null;
        }

        return e.ToLowerInvariant() switch
        {
            "draw" => ShowHennaDraw(session, player, npc),
            "remove" => ShowHennaRemove(session, player, npc),
            _ when e.StartsWith("Wear ", StringComparison.OrdinalIgnoreCase)
                => WearHennaFromNpc(session, player, npc, e.AsSpan(5).Trim()),
            _ when e.StartsWith("Cancel ", StringComparison.OrdinalIgnoreCase)
                => CancelHennaFromNpc(session, player, npc, e.AsSpan(7).Trim()),
            _ => UnknownSymbolMakerEvent(player, e)
        };
    }

    string? ShowHennaDraw(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        SendHtml(session, player, npc, BuildHennaDrawHtml(player));
        return null;
    }

    string? ShowHennaRemove(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        SendHtml(session, player, npc, BuildHennaRemoveHtml(player));
        return null;
    }

    string? WearHennaFromNpc(ZoneSession session, ZoneEntity player, ZoneEntity npc, ReadOnlySpan<char> dyeToken)
    {
        if (!int.TryParse(dyeToken, out int dyeId) || dyeId <= 0)
            return "bad_request";

        var result = TryWearHennaPaying(player, dyeId, out int slot, out var dye, out int have);
        switch (result)
        {
            case HennaWearResult.UnknownDye:
                throw new InvalidOperationException($"henna.json missing dyeId={dyeId}");
            case HennaWearResult.NoInventory:
                throw new InvalidOperationException($"player {player.ObjectId} has no inventory for henna wear.");
            case HennaWearResult.WrongClass:
                SendHtml(session, player, npc, "<html><body>That dye is not for your class.</body></html>");
                return null;
            case HennaWearResult.MissingItems:
                ZoneLog.Warn(
                    $"henna wear refuse object={player.ObjectId} dye={dyeId} item={dye!.ItemId} " +
                    $"need={dye.WearCount} have={have}");
                SendHtml(
                    session, player, npc,
                    $"<html><body>You need {dye.WearCount} dye item(s) (id {dye.ItemId}). You have {have}.</body></html>");
                return null;
            case HennaWearResult.NoFreeSlot:
                ZoneLog.Warn($"henna wear no free slot object={player.ObjectId} dye={dyeId}");
                SendHtml(session, player, npc, "<html><body>You already wear three symbols.</body></html>");
                return null;
            case HennaWearResult.Ok:
                ZoneLog.Info(
                    $"henna wear object={player.ObjectId} dye={dyeId} slot={slot} " +
                    $"str={player.HennaStr} int={player.HennaInt} wit={player.HennaWit} men={player.HennaMen}");
                SelfUserInfo(player);
                SendHtml(session, player, npc, BuildHennaDrawHtml(player));
                return null;
            default:
                throw new InvalidOperationException($"Unhandled henna wear result {result}");
        }
    }

    string? CancelHennaFromNpc(ZoneSession session, ZoneEntity player, ZoneEntity npc, ReadOnlySpan<char> slotToken)
    {
        if (!int.TryParse(slotToken, out int slot) || slot is < 0 or > 2)
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

    string? UnknownSymbolMakerEvent(ZoneEntity player, string evt)
    {
        ZoneLog.Warn($"symbol-maker unknown event object={player.ObjectId} evt={evt}");
        return "bad_request";
    }

    /// <summary>
    /// Mobius HennaEquipList shape for Net HTML: only dyes the class can wear and the bag can pay for.
    /// </summary>
    string BuildHennaDrawHtml(ZoneEntity player)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>Choose a dye to draw:<br><br>");
        int listed = 0;
        if (player.Inventory == null)
            throw new InvalidOperationException($"player {player.ObjectId} has no inventory for henna draw list.");

        foreach (var dye in _henna.ForClass(player.ClassId).OrderBy(d => d.DyeId))
        {
            int have = player.Inventory.CountOf(dye.ItemId);
            if (have < dye.WearCount)
                continue;
            sb.Append("<a action=\"bypass -h Quest SymbolMaker Wear ")
                .Append(dye.DyeId)
                .Append("\">")
                .Append(FormatHennaDelta(dye))
                .Append(" (")
                .Append(dye.ItemId)
                .Append('×')
                .Append(dye.WearCount)
                .Append(", have ")
                .Append(have)
                .Append(")</a><br>");
            listed++;
        }

        if (listed == 0)
            sb.Append("You have no dye that can be drawn for your class.<br>");
        sb.Append("<br><a action=\"bypass -h Quest SymbolMaker symbol_maker.htm\">Return</a></body></html>");
        return sb.ToString();
    }

    string BuildHennaRemoveHtml(ZoneEntity player)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>Worn symbols:<br><br>");
        AppendHennaSlot(sb, 0, player.HennaDye1);
        AppendHennaSlot(sb, 1, player.HennaDye2);
        AppendHennaSlot(sb, 2, player.HennaDye3);
        sb.Append("<br><a action=\"bypass -h Quest SymbolMaker symbol_maker.htm\">Return</a></body></html>");
        return sb.ToString();
    }

    void AppendHennaSlot(StringBuilder sb, int slot, int dyeId)
    {
        if (dyeId <= 0)
        {
            sb.Append("Slot ").Append(slot + 1).Append(": empty<br>");
            return;
        }

        if (!_henna.TryGet(dyeId, out var dye))
            throw new InvalidOperationException($"Worn henna dyeId={dyeId} missing from henna.json");
        sb.Append("Slot ").Append(slot + 1).Append(": ").Append(FormatHennaDelta(dye))
            .Append(" <a action=\"bypass -h Quest SymbolMaker Cancel ").Append(slot)
            .Append("\">Remove</a><br>");
    }

    static string FormatHennaDelta(HennaDefinition dye)
    {
        var parts = new List<string>(3);
        AppendStat(parts, "STR", dye.Str);
        AppendStat(parts, "CON", dye.Con);
        AppendStat(parts, "DEX", dye.Dex);
        AppendStat(parts, "INT", dye.Int);
        AppendStat(parts, "MEN", dye.Men);
        AppendStat(parts, "WIT", dye.Wit);
        return parts.Count == 0 ? dye.Name : string.Join(' ', parts);
    }

    static void AppendStat(List<string> parts, string name, int value)
    {
        if (value == 0)
            return;
        parts.Add($"{name}{(value > 0 ? "+" : "")}{value}");
    }

    string ReadSymbolMakerHtml(string fileName)
    {
        if (!_html.TryGet($"symbolmaker/{fileName}", out var html))
            throw new FileNotFoundException($"Missing Symbol Maker HTML symbolmaker/{fileName}");
        return html;
    }
}
