using System.Globalization;
using System.Text;
using Fineage.Zone.RaidBossPoints;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    RaidBossPointsManager _raidBossPoints = RaidBossPointsManager.CreateDefault();

    internal RaidBossPointsManager RaidBossPoints => _raidBossPoints;

    public void UseRaidBossPoints(RaidBossPointsManager manager) =>
        _raidBossPoints = manager ?? throw new ArgumentNullException(nameof(manager));

    internal void InitRaidBossPoints(RaidBossPointsManager? manager) =>
        _raidBossPoints = manager ?? RaidBossPointsManager.CreateDefault();

    void NoteRaidBossPoints(ZoneEntity npc, ZoneEntity? killer)
    {
        if (npc.NpcType is not ("RaidBoss" or "GrandBoss") || killer?.Session == null)
            return;

        var random = Rng(_combatRandom, killer);
        int points = RaidBossPointsManager.PointsForKill(npc.Level, random);
        if (points <= 0)
            return;

        foreach (var member in RaidPointRecipients(killer))
            _raidBossPoints.AddPoints(member.ObjectId, member.Name, npc.NpcId, points);
    }

    List<ZoneEntity> RaidPointRecipients(ZoneEntity killer)
    {
        var list = new List<ZoneEntity>();
        if (_party.Of(killer.ObjectId) is not { } party)
        {
            if (killer.Kind == ZoneEntityKind.Player && killer.Session != null)
                list.Add(killer);
            return list;
        }

        foreach (int id in party.MemberIds)
        {
            if (_byId.TryGetValue(id, out var member) && member.Kind == ZoneEntityKind.Player && member.Session != null && !member.Dead)
                list.Add(member);
        }

        return list;
    }

    bool RaidBossRankingFirstPage(ZoneEntity player, ZoneEntity npc, out string? html)
    {
        html = null;
        if (!_raidBossPoints.Catalog.IsRankingNpc(npc.NpcId))
            return false;
        html =
            "<html><body>%npcname%:<br>I track raid boss hunter standings.<br>" +
            "<a action=\"bypass npc_%objectId%_RaidRanking\">View raid boss points ranking</a></body></html>";
        return true;
    }

    string? ShowRaidBossRanking(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!_raidBossPoints.Catalog.IsRankingNpc(npc.NpcId))
            return "bad_request";

        string path = _raidBossPoints.Catalog.RankingPage;
        if (!TryPage(session.Language, path, out var template))
        {
            ZoneLog.Warn($"[raid-points] missing HTML page {path} for ranking npcId={npc.NpcId}");
            SendHtml(session, player, npc,
                $"<html><body>%npcname%:<br>Ranking page missing:<br>{path}</body></html>");
            return null;
        }

        int myTotal = _raidBossPoints.GetTotalPoints(player.ObjectId);
        int myRank = _raidBossPoints.CalculateRanking(player.ObjectId);
        var top = _raidBossPoints.TopN(_raidBossPoints.Catalog.TopN);
        var rows = new StringBuilder();
        if (top.Count == 0)
            rows.Append("<tr><td colspan=3 align=center>No raid boss points recorded yet.</td></tr>");
        else
        {
            foreach (var row in top)
            {
                rows.Append("<tr><td width=50 align=center>")
                    .Append(row.Rank.ToString(CultureInfo.InvariantCulture))
                    .Append("</td><td width=150 align=center>")
                    .Append(HtmlEscape(ResolveCharacterName(row.CharacterId, row.Name)))
                    .Append("</td><td width=70 align=center>")
                    .Append(row.TotalPoints.ToString(CultureInfo.InvariantCulture))
                    .Append("</td></tr>");
            }
        }

        string html = template
            .Replace("%my_points%", myTotal.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%my_rank%", myRank > 0 ? myRank.ToString(CultureInfo.InvariantCulture) : "-", StringComparison.Ordinal)
            .Replace("%rows%", rows.ToString(), StringComparison.Ordinal);
        SendHtml(session, player, npc, html);
        return null;
    }

    static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);

    string ResolveCharacterName(int characterId, string storedName)
    {
        if (_byId.TryGetValue(characterId, out var live) && !string.IsNullOrWhiteSpace(live.Name))
            return live.Name;
        if (!string.IsNullOrWhiteSpace(storedName))
            return storedName;
        if (_accounts?.FindByCharacterId(characterId) is { } record && !string.IsNullOrWhiteSpace(record.Name))
            return record.Name;
        return characterId.ToString(CultureInfo.InvariantCulture);
    }

    internal void NoteRaidBossPointsForTest(ZoneEntity npc, ZoneEntity killer) =>
        NoteRaidBossPoints(npc, killer);

    internal string? RaidBossRankingForTest(ZoneSession session, int characterId)
    {
        using (_affinity.Step())
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return "world";
            var npc = new ZoneEntity
            {
                Kind = ZoneEntityKind.Npc,
                NpcId = 31603,
                ObjectId = 1,
                Name = "Black Cat"
            };
            return ShowRaidBossRanking(session, player, npc);
        }
    }
}
