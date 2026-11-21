using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>
/// <c>showTeleports</c>, <c>teleport LIST ID</c>, and <c>goto</c> (an alias of teleport).
/// Noblesse selection stays on the stub. The world prices and moves the player.
/// </summary>
public sealed class TeleportBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Teleport", "goto", "showTeleports"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return BypassReply.Fail("bad_request");
        var token = parts[0];
        if (token.Equals("showTeleports", StringComparison.OrdinalIgnoreCase))
        {
            var list = parts.Length >= 2 ? parts[1] : TeleportRulesNormal();
            return BypassReply.ServiceCall(NpcServiceIntent.TeleportList, list);
        }

        if (token.Equals("goto", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("teleport", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Teleport", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 2 && int.TryParse(parts[1], out int onlyId))
                return BypassReply.ServiceCall(NpcServiceIntent.Teleport, "NORMAL", onlyId);
            if (parts.Length >= 3 && int.TryParse(parts[2], out int id))
                return BypassReply.ServiceCall(NpcServiceIntent.Teleport, parts[1], id);
            return BypassReply.Fail("bad_request");
        }

        return BypassReply.Fail("bad_request");
    }

    static string TeleportRulesNormal() => "NORMAL";
}

/// <summary>Private warehouse windows.</summary>
public sealed class WarehouseBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["DepositP", "WithdrawP"];

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        if (token.Equals("DepositP", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.Deposit);
        if (token.Equals("WithdrawP", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.Withdraw);
        return BypassReply.Fail("bad_request");
    }
}

/// <summary>Freight warehouse (Mobius <c>handlers/bypass/npc/Freight</c>).</summary>
public sealed class FreightBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } =
        ["package_deposit", "package_withdraw", "DepositF", "WithdrawF"];

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        if (token.Equals("package_deposit", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("DepositF", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.PackageDeposit);
        if (token.Equals("package_withdraw", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("WithdrawF", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.PackageWithdraw);
        return BypassReply.Fail("bad_request");
    }
}

/// <summary>Newbie Helper supplemental magic and the Blessing of Protection.</summary>
public sealed class BuffBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["SupportMagic", "SupportBlessing", "GiveBlessing"];

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        if (token.Equals("SupportMagic", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.SupportMagic);
        if (token.Equals("SupportBlessing", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.SupportBlessing);
        if (token.Equals("GiveBlessing", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.GiveBlessing);
        return BypassReply.Fail("bad_request");
    }
}

/// <summary>
/// Trainer <c>SkillList</c> (Mobius <c>Folk.showSkillList</c>) and <c>learn_skill ID</c>. Interim HTML learn list
/// until a Net AcquireSkillList window exists; the world decides free vs SP mode.
/// </summary>
public sealed class TrainerBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["SkillList", "learn_skill"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return BypassReply.Fail("bad_request");
        if (parts[0].Equals("SkillList", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.SkillList);
        if (parts[0].Equals("learn_skill", StringComparison.OrdinalIgnoreCase) && parts.Length == 2 &&
            int.TryParse(parts[1], out int skillId) && skillId > 0)
            return BypassReply.ServiceCall(NpcServiceIntent.LearnSkill, "", skillId);
        return BypassReply.Fail("bad_request");
    }
}

/// <summary>
/// Fisherman bypasses (Mobius <c>Fisherman.onBypassFeedback</c>): <c>FishSkillList</c> opens the guild skill window,
/// <c>FishingChampionship</c> shows the tournament page (the championship itself is not run yet).
/// </summary>
public sealed class FishermanBypassHandler : IBypassHandler
{
    readonly NpcHtmlCatalog _html;

    public FishermanBypassHandler(NpcHtmlCatalog html) => _html = html ?? throw new ArgumentNullException(nameof(html));

    public IReadOnlyList<string> Commands { get; } = ["FishSkillList", "FishingChampionship"];

    public BypassReply Handle(BypassCall call)
    {
        if (BypassCommands.Token(call.Command).Equals("FishSkillList", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.FishSkillList);
        var page = NpcHtmlPaths.ReadLink(_html, call.NpcType, "Link fisherman/fishing_championship.htm");
        return page.RejectedPath ? BypassReply.Fail("bad_request") : BypassReply.Page(page.Html);
    }
}

/// <summary>Lottery seller <c>Loto N</c> (Mobius <c>Loto</c> bypass): N picks the page, a number or a ticket to claim.</summary>
public sealed class LotoBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Loto"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int value = parts.Length > 1 && int.TryParse(parts[1], out int v) && v >= 0 ? v : 0;
        return BypassReply.ServiceCall(NpcServiceIntent.Loto, "", value);
    }
}

/// <summary>
/// Mobius <c>SignsPriest</c> <c>SevenSigns N ...</c> as NPC HTML. Only the Black Marketeer's <c>SevenSigns 7 amount</c>
/// (Ancient Adena → adena) runs over HTML; the priest record, join, contribution, reward, and stone exchange use the
/// Seven Signs packets (91).
/// </summary>
public sealed class SevenSignsBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["SevenSigns"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[1] != "7")
            return BypassReply.Fail("bad_request");
        int amount = parts.Length > 2 && int.TryParse(parts[2], out int v) && v > 0 ? v : -1;
        return BypassReply.ServiceCall(NpcServiceIntent.AncientAdenaToAdena, "", amount);
    }
}

/// <summary>
/// Mobius <c>VillageMaster</c> <c>Subclass N [p1] [p2]</c>: 0 menu, 1 add list, 2 change list, 3 modify list,
/// 4 add, 5 change, 6 modify choice, 7 modify. The arguments ride in the service arg.
/// </summary>
public sealed class SubclassBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Subclass"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int choice) || choice is < 0 or > 7)
            return BypassReply.Fail("bad_request");
        return BypassReply.ServiceCall(NpcServiceIntent.Subclass, string.Join(' ', parts.Skip(2)), choice);
    }
}

/// <summary>Mobius <c>Teleporter showNoblesSelect</c> (Block G1).</summary>
public sealed class NoblesSelectBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["showNoblesSelect"];

    public BypassReply Handle(BypassCall call) => BypassReply.ServiceCall(NpcServiceIntent.NoblesSelect);
}

/// <summary>Grand Olympiad Manager / Monument of Heroes pages (Block G2): the whole command rides in the service arg.</summary>
public sealed class OlympiadBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["OlympiadDesc", "OlympiadNoble", "Olympiad"];

    public BypassReply Handle(BypassCall call) => BypassReply.ServiceCall(NpcServiceIntent.Olympiad, call.Command.Trim());
}

/// <summary>Merchant <c>TerritoryStatus</c> (Mobius <c>Merchant.showTerritoryStatusWindow</c>): castle, lord and tax page.</summary>
public sealed class TerritoryBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["TerritoryStatus"];

    public BypassReply Handle(BypassCall call) => BypassReply.ServiceCall(NpcServiceIntent.TerritoryStatus);
}
