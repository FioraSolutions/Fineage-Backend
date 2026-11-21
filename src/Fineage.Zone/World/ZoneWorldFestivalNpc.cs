using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Party;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static readonly int[] FestivalGuideNpcIds =
    [
        31127, 31128, 31129, 31130, 31131,
        31137, 31138, 31139, 31140, 31141
    ];

    bool FestivalFirstPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, out string? html)
    {
        html = null;
        if (!SevenSignsFestivalGuide.TryProfile(npc.NpcId, out var profile) || SevenSignsFestivalGuide.IsWitch(npc.NpcId))
        {
            if (SevenSignsFestivalGuide.IsWitch(npc.NpcId))
            {
                html = LoadFestivalHtml("seven_signs/festival/festival_witch.htm", npc, player, profile);
                return true;
            }
            return false;
        }

        if (!FestivalGuideNpcIds.Contains(npc.NpcId))
            return false;

        string file = profile.Cabal == SevenSignsCabal.Dawn
            ? "seven_signs/festival/dawn_guide.htm"
            : "seven_signs/festival/dusk_guide.htm";
        html = LoadFestivalHtml(file, npc, player, profile);
        return true;
    }

    string? ApplyFestival(ZoneSession session, ZoneEntity player, ZoneEntity npc, BypassReply reply)
    {
        if (!SevenSignsFestivalGuide.TryProfile(npc.NpcId, out var profile))
            return "bad_request";

        string arg = reply.ServiceArg ?? "";
        if (arg.StartsWith("desc:", StringComparison.OrdinalIgnoreCase))
        {
            int page = int.TryParse(arg["desc:".Length..], out int p) ? p : 0;
            SendFestivalPage(session, player, npc, profile, page, suffix: null, description: true);
            return null;
        }

        if (!arg.StartsWith("festival:", StringComparison.OrdinalIgnoreCase))
            return "bad_request";

        var parts = arg["festival:".Length..].Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int action = parts.Length > 0 && int.TryParse(parts[0], out int a) ? a : -1;
        int stoneId = parts.Length > 1 && int.TryParse(parts[1], out int s) ? s : 0;

        switch (action)
        {
            case 1:
                if (_sevenSigns.Engine.Period == SevenSignsPeriod.SealValidation)
                {
                    SendFestivalPage(session, player, npc, profile, 2, "a", false);
                    return null;
                }
                if (FestivalBoard.GlobalFestivalActive || !FestivalBoard.SignupOpen(NowUnixMs()))
                {
                    SendFestivalPage(session, player, npc, profile, 2, "f", false);
                    return null;
                }
                if (_party.Of(player.ObjectId) is not { } party)
                {
                    SendFestivalPage(session, player, npc, profile, 2, "b", false);
                    return null;
                }
                if (party.LeaderObjectId != player.ObjectId)
                {
                    SendFestivalPage(session, player, npc, profile, 2, "c", false);
                    return null;
                }
                if (party.MemberIds.Count < _festivalCatalog.MinPartySize)
                {
                    SendFestivalPage(session, player, npc, profile, 2, "b", false);
                    return null;
                }
                if (PartyMaxLevel(party) > SevenSignsFestivalGuide.MaxLevelForTier(profile.TierId))
                {
                    SendFestivalPage(session, player, npc, profile, 2, "d", false);
                    return null;
                }
                SendFestivalPage(session, player, npc, profile, 1, null, false);
                return null;
            case 2:
                return PayFestivalSignup(session, player, npc, profile, stoneId);
            case 3:
                return RegisterFestivalScore(session, player, npc, profile);
            case 6:
                SendFestivalBonusTable(session, player, npc);
                return null;
            case 5:
                SendFestivalStatus(session, player, npc, profile);
                return null;
            case 8:
                if (TryIncreaseFestivalChallenge(player, profile.TierId, profile.Cabal))
                    SendFestivalPage(session, player, npc, profile, 8, "b", false);
                else
                    SendFestivalPage(session, player, npc, profile, 8, "c", false);
                return null;
            case 9:
                if (_party.Of(player.ObjectId) is { } leaveParty)
                    TryAdminFestivalEnd(leaveParty.PartyId);
                return null;
            case 0:
                return DistribFestivalBonus(session, player);
            default:
                SendFestivalPage(session, player, npc, profile, action, null, false);
                return null;
        }
    }

    string? PayFestivalSignup(ZoneSession session, ZoneEntity player, ZoneEntity npc, FestivalGuideProfile profile, int stoneId)
    {
        if (!FestivalBoard.SignupOpen(NowUnixMs()))
            return "festival_closed";
        int cost = SevenSignsFestivalGuide.StoneCost(profile, stoneId);
        if (cost <= 0)
            return "bad_request";
        if (player.Inventory == null || player.Inventory.CountOf(stoneId) < cost)
            return "no_stones";
        if (_party.Of(player.ObjectId) is not { } party || party.LeaderObjectId != player.ObjectId)
            return "no_party";
        if (FestivalBoard.Registrations().Any(r => r.PartyId == party.PartyId))
        {
            SendFestivalPage(session, player, npc, profile, 2, "f", false);
            return null;
        }

        return RecordBagChange(
            player,
            LedgerTxKind.SevenSignsContribute,
            LedgerAccount.Of(SystemAccount.SevenSigns),
            () =>
            {
                var changes = new List<InventoryUpdateEntryMsg>();
                Take(player.Inventory, stoneId, cost, changes);
                FestivalBoard.AddAccumulatedBonus(profile.TierId, stoneId, cost);
                if (changes.Count > 0)
                {
                    NetBridge?.FanoutInventoryUpdate([session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
                    SaveInventory(session, player.Inventory);
                }

                FestivalBoard.RegisterParty(new FestivalPartyRegistration
                {
                    PartyId = party.PartyId,
                    LeaderObjectId = player.ObjectId,
                    TierId = profile.TierId,
                    Cabal = profile.Cabal,
                    Members = party.MemberIds.ToList()
                });
                SendFestivalPage(session, player, npc, profile, 2, "e", false);
                return (string?)null;
            },
            _ => profile.TierId);
    }

    string? RegisterFestivalScore(ZoneSession session, ZoneEntity player, ZoneEntity npc, FestivalGuideProfile profile)
    {
        if (_sevenSigns.Engine.Period == SevenSignsPeriod.SealValidation)
        {
            SendFestivalPage(session, player, npc, profile, 3, "a", false);
            return null;
        }
        if (!FestivalBoard.IsPreviousLeader(profile.Cabal, profile.TierId, player.ObjectId))
        {
            SendFestivalPage(session, player, npc, profile, 3, "b", false);
            return null;
        }
        if (player.Inventory == null)
            return "world";

        int offeringId = _festivalCatalog.OfferingItemId;
        int count = player.Inventory.CountOf(offeringId);
        if (count <= 0)
            return "no_offerings";

        return RecordBagChange(
            player,
            LedgerTxKind.SevenSignsContribute,
            LedgerAccount.Of(SystemAccount.SevenSigns),
            () =>
            {
                var changes = new List<InventoryUpdateEntryMsg>();
                Take(player.Inventory, offeringId, count, changes);
                if (changes.Count > 0)
                {
                    NetBridge?.FanoutInventoryUpdate([session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
                    SaveInventory(session, player.Inventory);
                }

                long offeringScore = (long)count * _festivalCatalog.OfferingValue;
                var previousMembers = FestivalBoard.PreviousParticipants(profile.Cabal, profile.TierId);
                bool claimed = _festivalRuntime.TryRecordInstanceScore(profile.Cabal, profile.TierId, offeringScore);
                if (claimed)
                {
                    int memberCount = Math.Max(1, previousMembers.Count);
                    long bonusShare = FestivalBoard.TakeAccumulatedShare(profile.TierId, memberCount);
                    if (bonusShare > 0 && _festivalRuntime.Engine != null)
                    {
                        int each = (int)Math.Min(int.MaxValue, bonusShare);
                        foreach (int memberId in previousMembers)
                            _festivalRuntime.Engine.CreditFestivalAncientAdena(memberId, each);
                    }
                    FestivalBoard.ClearPreviousParticipants(profile.Cabal, profile.TierId);
                    SendFestivalPage(session, player, npc, profile, 3, "c", false);
                }
                else
                    SendFestivalPage(session, player, npc, profile, 3, "d", false);
                return (string?)null;
            },
            _ => profile.TierId);
    }

    string? DistribFestivalBonus(ZoneSession session, ZoneEntity player)
    {
        if (_sevenSigns.Engine.Period != SevenSignsPeriod.SealValidation)
            return null;
        if (_sevenSigns.Engine.WinningCabal != (SevenSignsCabal)_sevenSigns.Engine.PlayerCabal(player.ObjectId))
            return null;
        // Mobius pays per-tier pool share when the player was on the winning recorded party — simplified: unused pool zeroes on competition end.
        return null;
    }

    int PartyMaxLevel(ZoneParty party)
    {
        int max = 0;
        foreach (int id in party.MemberIds)
        {
            if (_byId.TryGetValue(id, out var member))
                max = Math.Max(max, member.Level);
        }
        return max;
    }

    void SendFestivalPage(
        ZoneSession session,
        ZoneEntity player,
        ZoneEntity npc,
        FestivalGuideProfile profile,
        int page,
        string? suffix,
        bool description)
    {
        string file = description
            ? $"seven_signs/festival/desc_{page}.htm"
            : suffix == null
                ? $"seven_signs/festival/festival_{page}.htm"
                : $"seven_signs/festival/festival_{page}{suffix}.htm";
        SendHtml(session, player, npc, LoadFestivalHtml(file, npc, player, profile));
    }

    void SendFestivalBonusTable(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var rows = new System.Text.StringBuilder();
        for (int i = 0; i < 5; i++)
        {
            long bonus = FestivalBoard.AccumulatedBonus(i);
            rows.Append("<tr><td align=\"center\" width=\"150\">")
                .Append(SevenSignsFestivalGuide.FestivalName(i))
                .Append("</td><td align=\"center\" width=\"150\">")
                .Append(bonus)
                .Append("</td></tr>");
        }
        string html = LoadFestivalHtml("seven_signs/festival/festival_6.htm", npc, player, default)
            .Replace("%bonusTable%", rows.ToString(), StringComparison.Ordinal);
        SendHtml(session, player, npc, html);
    }

    void SendFestivalStatus(ZoneSession session, ZoneEntity player, ZoneEntity npc, FestivalGuideProfile profile)
    {
        var rows = new System.Text.StringBuilder();
        for (int i = 0; i < 5; i++)
        {
            int dawn = FestivalBoard.HighScore(SevenSignsCabal.Dawn, i);
            int dusk = FestivalBoard.HighScore(SevenSignsCabal.Dusk, i);
            string winner = dawn > dusk ? "Children of Dawn" : dusk > dawn ? "Children of Dusk" : "None";
            rows.Append("<tr><td width=\"100\" align=\"center\">")
                .Append(SevenSignsFestivalGuide.FestivalName(i))
                .Append("</td><td align=\"center\" width=\"35\">")
                .Append(dusk)
                .Append("</td><td align=\"center\" width=\"35\">")
                .Append(dawn)
                .Append("</td><td align=\"center\" width=\"130\">")
                .Append(winner)
                .Append("</td></tr>");
        }
        string html = LoadFestivalHtml("seven_signs/festival/festival_5.htm", npc, player, profile)
            .Replace("%statsTable%", rows.ToString(), StringComparison.Ordinal);
        SendHtml(session, player, npc, html);
    }

    string LoadFestivalHtml(string path, ZoneEntity npc, ZoneEntity player, FestivalGuideProfile profile)
    {
        string npcName = _pages.Name(player.Session?.Language, "npc", npc.NpcId, npc.Name);
        string html = TryPage(player.Session?.Language, path, out var body) ? body : "<html><body>%npcname%</body></html>";
        html = html.Replace("%objectId%", npc.ObjectId.ToString(), StringComparison.Ordinal)
            .Replace("%npc_name%", npcName, StringComparison.Ordinal)
            .Replace("%npcname%", npcName, StringComparison.Ordinal)
            .Replace("%festivalType%", SevenSignsFestivalGuide.FestivalName(profile.TierId), StringComparison.Ordinal)
            .Replace("%festivalMins%", FestivalMinsLine(), StringComparison.Ordinal)
            .Replace("%cycleMins%", (_festivalCatalog.CycleLengthMs / 60_000).ToString(), StringComparison.Ordinal)
            .Replace("%blueStoneNeeded%", profile.BlueStones.ToString(), StringComparison.Ordinal)
            .Replace("%greenStoneNeeded%", profile.GreenStones.ToString(), StringComparison.Ordinal)
            .Replace("%redStoneNeeded%", profile.RedStones.ToString(), StringComparison.Ordinal)
            .Replace("%minFestivalPartyMembers%", _festivalCatalog.MinPartySize.ToString(), StringComparison.Ordinal);
        return html;
    }

    string FestivalMinsLine()
    {
        if (_sevenSigns.Engine.Period == SevenSignsPeriod.SealValidation)
            return "<font color=\"FF0000\">This is the Seal Validation period. Festivals will resume next week.</font>";
        long now = NowUnixMs();
        if (FestivalBoard.GlobalFestivalActive)
            return "<font color=\"FF0000\">The festival is in progress.</font>";
        int mins = FestivalBoard.MinutesToNextFestivalStart(now);
        if (mins < 0)
            return "<font color=\"FF0000\">The festival is available during the competition period.</font>";
        if (FestivalBoard.FestivalSliceActive(now))
            return "<font color=\"FF0000\">The festival has begun!</font>";
        return $"<font color=\"FF0000\">The next festival will begin in {mins} minute(s).</font>";
    }
}