using System.Globalization;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Fishing;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly FishingChampionshipActor _fishingChampionship;
    internal FishingChampionshipBoard FishingChampionship => _fishingChampionship.Board;
    public void UseFishingChampionship(FishingChampionshipBoard board) { using (_affinity.Step()) _fishingChampionship.Use(board); }
    internal void TickFishingChampionshipForTests() => AtHookTick(tick => _fishingChampionship.Step(tick));
    void RecordFishingChampionshipCatch(ZoneEntity player, int lureId) { if (player.Session == null) return; FishingChampionship.RecordCatch(player.Name, lureId, _fishingRandom); }
    string? ShowFishingChampionship(ZoneSession session, ZoneEntity player, ZoneEntity npc, bool claim)
    {
        if (claim)
        {
            // #996: the claim mark and the prize are one move, so they commit together (the championship document rides
            // the economy commit with the ledger row and the bag).
            if (player.Inventory != null)
            {
                int itemId = FishingChampionship.Settings.RewardItemId;
                int rank = 0;
                RecordBagChange(player, LedgerTxKind.FishingChampionshipPrize, LedgerAccount.Of(SystemAccount.Gathering), () =>
                {
                    long reward = FishingChampionship.TryClaim(player.Name, out rank);
                    if (reward > 0)
                        GiveItem(session, player.Inventory!, itemId, (int)Math.Min(reward, int.MaxValue));
                    return (string?)null;
                }, _ => rank);
            }
        }
        var page = _pages.ReadLink(session.Language, npc.NpcType, "Link fisherman/fishing_championship.htm");
        if (page.RejectedPath) return "bad_request";
        SendHtml(session, player, npc, RenderFishingChampionshipPage(page.Html, npc));
        return null;
    }
    internal string? FishingChampionshipForTest(ZoneSession session, bool claim = false, int npcId = 31562)
    {
        using (_affinity.Step())
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player)) return "world";
            var npc = _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId) ?? new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 1, NpcType = "Fisherman" };
            return ShowFishingChampionship(session, player, npc, claim);
        }
    }
    string RenderFishingChampionshipPage(string template, ZoneEntity npc)
    {
        var winners = FishingChampionship.LastWinners; string[] ordinals = ["1st", "2nd", "3rd", "4th", "5th"]; string html = template;
        for (int i = 0; i < ordinals.Length; i++)
        {
            string name = i < winners.Count ? HtmlText(winners[i].Name) : "—";
            string len = i < winners.Count ? FishingChampionshipBoard.FormatLength(winners[i].Length) : "00.00";
            html = html.Replace($"<tr><td width=50 align=center>{ordinals[i]} </td><td width=150 align=center>Not Working</td><td width=70 align=center>00.00</td></tr>", $"<tr><td width=50 align=center>{ordinals[i]} </td><td width=150 align=center>{name}</td><td width=70 align=center>{len}</td></tr>", StringComparison.Ordinal);
        }
        var rewards = FishingChampionship.Settings.Rewards;
        if (rewards.Length >= 5)
            html = html.Replace("<td width=70 align=center>800000</td>", $"<td width=70 align=center>{rewards[0]}</td>", StringComparison.Ordinal).Replace("<td width=70 align=center>500000</td>", $"<td width=70 align=center>{rewards[1]}</td>", StringComparison.Ordinal).Replace("<td width=70 align=center>300000</td>", $"<td width=70 align=center>{rewards[2]}</td>", StringComparison.Ordinal).Replace("<td width=70 align=center>200000</td>", $"<td width=70 align=center>{rewards[3]}</td>", StringComparison.Ordinal).Replace("<td width=70 align=center>100000</td>", $"<td width=70 align=center>{rewards[4]}</td>", StringComparison.Ordinal);
        return html.Replace("This list will be updated in 0 minute(s)!", $"This list will be updated in {FishingChampionship.MinutesRemaining(NowUnixMs())} minute(s)!", StringComparison.Ordinal).Replace("%objectId%", npc.ObjectId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
