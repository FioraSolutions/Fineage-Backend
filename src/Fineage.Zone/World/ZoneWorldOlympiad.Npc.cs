using System.Globalization;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;

namespace Fineage.Zone.World;

/// <summary>
/// Block G2 Grand Olympiad Manager (31688) and Monument of Heroes (31690, 31769–31772) after Mobius
/// <c>OlympiadManager.onBypassFeedback</c> and the <c>MonumentOfHeroes</c> script: description pages, registration
/// and points, gate pass settlement, ranking, observer list, hero list, hero claim, weapon and circlet.
/// Pages are the Mobius HTML under <c>data/html/olympiad</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    const int OlympiadManagerId = 31688;
    static readonly int[] HeroMonumentIds = [31690, 31769, 31770, 31771, 31772];

    static bool IsHeroItem(int itemId) =>
        itemId == OlympiadProtocol.HeroCirclet ||
        itemId is >= OlympiadProtocol.FirstHeroWeapon and <= OlympiadProtocol.LastHeroWeapon;

    string? OlympiadPage(ZoneEntity player, string file) =>
        TryPage(player.Session?.Language, "olympiad/" + file, out var html) ? html : null;

    /// <summary>First talk: a noble gets the manager menu / monument menu; everyone else the default page.</summary>
    bool OlympiadFirstPage(ZoneEntity player, ZoneEntity npc, out string? html)
    {
        html = null;
        if (!player.Noble)
            return false;
        if (npc.NpcId == OlympiadManagerId)
            html = OlympiadPage(player, "noble_main.htm");
        else if (HeroMonumentIds.Contains(npc.NpcId))
            html = OlympiadPage(player, "hero_main.htm");
        return html != null;
    }

    string? OlympiadNpcBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string command)
    {
        if (command.StartsWith("OlympiadDesc", StringComparison.Ordinal))
        {
            string tail = command.Length > "OlympiadDesc ".Length ? command["OlympiadDesc ".Length..].Trim() : "0";
            if (tail.Length == 0 || !char.IsDigit(tail[0]) || tail.Any(c => !char.IsLetterOrDigit(c)))
                return "bad_request";
            string file = tail == "0" ? "noble_main.htm" : $"noble_desc{tail}.htm";
            SendHtml(session, player, npc, OlympiadPage(player, file) ?? NpcHtmlTextFallback(file));
            return null;
        }

        if (command.StartsWith("OlympiadNoble", StringComparison.Ordinal))
            return OlympiadNobleBypass(session, player, npc, command);

        // Olympiad <val>[_<arg>]
        string rest = command.Length > "Olympiad ".Length ? command["Olympiad ".Length..].Trim() : "";
        string[] parts = rest.Split('_', 2);
        if (!int.TryParse(parts[0], out int val))
            return "bad_request";
        int arg = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) ? a : -1;
        switch (val)
        {
            case 1:
                {
                    var matches = _olympiad.Board.MatchesByStadium();
                    string html = OlympiadPage(player, "olympiad_observe1.htm") ?? "";
                    for (int i = 0; i < _olympiad.Board.Stadiums.Count; i++)
                        html = html.Replace($"%title{i + 1}%", matches.TryGetValue(i, out var m) ? m.Title : "Initial State", StringComparison.Ordinal);
                    SendHtml(session, player, npc, html);
                    return null;
                }
            case 2:
                {
                    if (!PlayerClasses.TryGet(arg, out var cls) || cls == null || cls.ClassLevel != 3)
                        return "bad_request";
                    var names = _olympiad.Board.ClassLeaders(arg);
                    string html = OlympiadPage(player, "olympiad_ranking.htm") ?? "";
                    for (int i = 1; i <= 10; i++)
                    {
                        bool has = i <= names.Count;
                        html = html.Replace($"%place{i}%", has ? i.ToString(CultureInfo.InvariantCulture) : "", StringComparison.Ordinal)
                            .Replace($"%rank{i}%", has ? names[i - 1] : "", StringComparison.Ordinal);
                    }
                    SendHtml(session, player, npc, html);
                    return null;
                }
            case 3:
                return BeginOlympiadObserve(session, player, arg);
            case 4:
                {
                    // Net has no ExHeroList: the current heroes go out as a page.
                    var rows = _olympiad.Board.Heroes().Where(h => h.Claimed).ToList();
                    string body = rows.Count == 0
                        ? "There is no Hero in this era yet.<br>"
                        : string.Concat(rows.Select(h => $"{ClassName(h.ClassId)}: {h.Name}<br>"));
                    SendHtml(session, player, npc, $"<html><body>{npc.Name}:<br>{body}</body></html>");
                    return null;
                }
            default:
                return "bad_request";
        }
    }

    static string NpcHtmlTextFallback(string file) => "<html><body>My Text is missing:<br>olympiad/" + file + "</body></html>";

    string? OlympiadNobleBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string command)
    {
        if (player.SubClassActive)
        {
            SendHtml(session, player, npc, OlympiadPage(player, "noble_desc5.htm") ?? NpcHtmlTextFallback("noble_desc5.htm"));
            return null;
        }
        if (!player.Noble || !PlayerClasses.TryGet(player.ClassId, out var cls) || cls == null || cls.ClassLevel < 3)
        {
            SendHtml(session, player, npc, OlympiadPage(player, "noble_desc6.htm") ?? NpcHtmlTextFallback("noble_desc6.htm"));
            return null;
        }

        string tail = command.Length > "OlympiadNoble ".Length ? command["OlympiadNoble ".Length..].Trim() : "";
        if (!int.TryParse(tail, out int val))
            return "bad_request";
        long now = NowUnixMs();
        switch (val)
        {
            case 1:
                ApplyOlympiad(_olympiad.Board.Unregister(ActorFor(player), now));
                return null;
            case 2:
                {
                    string html = (OlympiadPage(player, "noble_registered.htm") ?? "")
                        .Replace("%listClassed%", _olympiad.Board.RegistrationCount(classed: true).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                        .Replace("%listNonClassedTeam%", "0", StringComparison.Ordinal)
                        .Replace("%listNonClassed%", _olympiad.Board.RegistrationCount(classed: false).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    SendHtml(session, player, npc, html);
                    return null;
                }
            case 3:
                SendHtml(session, player, npc, (OlympiadPage(player, "noble_points1.htm") ?? "")
                    .Replace("%points%", _olympiad.Board.PointsOf(player.ObjectId).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
                return null;
            case 4:
            case 5:
                ApplyOlympiad(_olympiad.Board.Register(ActorFor(player), classed: val == 5, now));
                return null;
            case 6:
                SendHtml(session, player, npc, OlympiadPage(player, _olympiad.Board.UnclaimedPasses(player.ObjectId) > 0 ? "noble_settle.htm" : "noble_nopoints.htm") ?? "");
                return null;
            case 7:
            case 8:
                // Mobius multisell 102 / 103 (hero and noble shops) are not converted in data/multisell.json.
                SendHtml(session, player, npc, OlympiadPage(player, "noble_nopoints.htm") ?? "");
                return null;
            case 9:
                SendHtml(session, player, npc, (OlympiadPage(player, "noble_points2.htm") ?? "")
                    .Replace("%points%", _olympiad.Board.LastPeriodPoints(player.ObjectId).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
                return null;
            case 10:
                {
                    int passes = _olympiad.Board.TakePasses(player.ObjectId);
                    if (passes > 0 && player.Inventory != null && player.Session != null && _game.Items.TryGet(_olympiad.Board.Settings.RewardItemId, out var _))
                    {
                        int itemId = _olympiad.Board.Settings.RewardItemId;
                        RecordBagChange(
                            player,
                            LedgerTxKind.OlympiadGrant,
                            LedgerAccount.Of(SystemAccount.Olympiad),
                            () =>
                            {
                                GiveItem(session, player.Inventory, itemId, passes);
                                SaveInventory(session, player.Inventory);
                                return true;
                            },
                            _ => itemId);
                    }
                    return null;
                }
            default:
                return "bad_request";
        }
    }

    /// <summary>Mobius <c>MonumentOfHeroes.onEvent</c>.</summary>
    string? MonumentOfHeroesBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        string? Monument(string file) =>
            TryPage(player.Session?.Language, "olympiad/monument/" + file, out var html) ? html : null;
        switch (scriptEvent)
        {
            case "HeroClaim":
                SendHtml(session, player, npc, Monument(_olympiad.Board.IsHero(player.ObjectId)
                    ? "already_hero_status.htm"
                    : _olympiad.Board.IsUnclaimedHero(player.ObjectId) ? "receive_hero.htm" : "no_hero_status.htm") ?? "");
                return null;
            case "HeroReceive":
                if (!_olympiad.Board.ClaimHero(player.ObjectId))
                    return null;
                SetHero(player, true);
                NetBridge?.FanoutSocialAction(KnownNetPlayersOf(player), player.ObjectId, OlympiadProtocol.HeroSocialAction);
                return null;
            case "HeroWeapon":
                if (!player.Hero)
                {
                    SendHtml(session, player, npc, Monument("not_a_hero.htm") ?? "");
                    return null;
                }
                bool hasWeapon = player.Inventory?.Items.Any(i => i.ItemId is >= OlympiadProtocol.FirstHeroWeapon and <= OlympiadProtocol.LastHeroWeapon) == true;
                SendHtml(session, player, npc, Monument(hasWeapon ? "already_have_weapon.htm" : "weapon_list.htm") ?? "");
                return null;
            case "HeroCirclet":
                if (!player.Hero)
                {
                    SendHtml(session, player, npc, Monument("not_a_hero.htm") ?? "");
                    return null;
                }
                if (player.Inventory!.CountOf(OlympiadProtocol.HeroCirclet) > 0)
                {
                    SendHtml(session, player, npc, Monument("already_have_circlet.htm") ?? "");
                    return null;
                }
                GrantHeroItem(session, player, OlympiadProtocol.HeroCirclet);
                return null;
            default:
                if (!int.TryParse(scriptEvent, out int weaponId) || weaponId is < OlympiadProtocol.FirstHeroWeapon or > OlympiadProtocol.LastHeroWeapon)
                    return "bad_request";
                // Mobius gives the weapon without re-checking; one Infinity weapon per hero is the page's promise.
                if (!player.Hero || player.Inventory!.Items.Any(i => i.ItemId is >= OlympiadProtocol.FirstHeroWeapon and <= OlympiadProtocol.LastHeroWeapon))
                    return null;
                GrantHeroItem(session, player, weaponId);
                return null;
        }
    }

    void GrantHeroItem(ZoneSession session, ZoneEntity player, int itemId)
    {
        if (player.Inventory == null || !_game.Items.TryGet(itemId, out _))
            return;
        RecordBagChange(
            player,
            LedgerTxKind.OlympiadGrant,
            LedgerAccount.Of(SystemAccount.Olympiad),
            () =>
            {
                GiveItem(session, player.Inventory, itemId, 1);
                SaveInventory(session, player.Inventory);
                return true;
            },
            _ => itemId);
    }

    /// <summary>Hero on/off: flag, hero skills, skill list, UserInfo / CharInfo.</summary>
    void SetHero(ZoneEntity player, bool hero)
    {
        if (player.Hero == hero)
            return;
        player.Hero = hero;
        RefreshKnownSkills(player);
        RefreshPledgeClass(player);
        if (player.Session == null)
            return;
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        SelfUserInfo(player);
        var peers = KnownNetPlayersOf(player).Where(id => id != player.ObjectId).ToList();
        if (peers.Count > 0)
            NetBridge?.FanoutCharInfo(peers, player.ObjectId);
    }

    /// <summary>
    /// Mobius <c>Hero.computeNewHeroes</c> for an old hero online: status and skills off, every hero item unequipped
    /// and destroyed. Offline heroes simply log in without the status (the board no longer lists them).
    /// </summary>
    void RevokeHero(int characterId)
    {
        if (!_byId.TryGetValue(characterId, out var player) || player.Kind != ZoneEntityKind.Player)
            return;
        SetHero(player, false);
        if (player.Inventory is not { } inventory || player.Session is not { } session)
            return;
        var doomed = inventory.Items.Where(i => IsHeroItem(i.ItemId)).ToList();
        if (doomed.Count == 0)
            return;
        foreach (var item in doomed)
        {
            if (item.IsEquipped)
                inventory.Unequip(item);
            inventory.Remove(item);
            NetBridge?.FanoutInventoryUpdate([player.ObjectId], InventoryMessages.Update(InventoryChange.Remove, item));
        }
        SaveInventory(session, inventory);
        RefreshGearSkills(player);
        SelfUserInfo(player);
    }
}
