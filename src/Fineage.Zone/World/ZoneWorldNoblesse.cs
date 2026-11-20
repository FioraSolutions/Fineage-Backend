using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Block G1 noblesse after Mobius <c>Player.setNoble</c>: the flag is persisted with the class state, the noble skill tree
/// (nobleSkillTree.xml) is granted on every class index without being stored, CharInfo / UserInfo carry it, the clan
/// pledge class floor is 5, and the Olympiad board knows the character. Hero skills (heroSkillTree.xml) follow
/// <see cref="ZoneEntity.Hero"/> on the base class only (Mobius setHero: only when the base class is active).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius nobleSkillTree: Strider Siege Assault, Build Advanced Headquarters, Wyvern Aegis, Noblesse Blessing,
    /// Summon CP Potion, Fortune / Harmony / Symphony of Noblesse.</summary>
    internal static readonly int[] NobleSkillIds = [325, 326, 327, 1323, 1324, 1325, 1326, 1327];

    /// <summary>Mobius heroSkillTree: Heroic Miracle, Heroic Berserker, Heroic Valor, Heroic Grandeur, Heroic Dread.</summary>
    internal static readonly int[] HeroSkillIds = [395, 396, 1374, 1375, 1376];

    void AddStatusSkills(ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        if (entity.Noble)
            AddLevelOne(entity, NobleSkillIds);
        if (entity.Hero && !entity.SubClassActive && !_olympiadArena.Contains(entity.ObjectId))
            AddLevelOne(entity, HeroSkillIds);
    }

    void AddLevelOne(ZoneEntity entity, int[] ids)
    {
        foreach (int id in ids)
        {
            if (_game.Skills.Find(id) != null && !entity.KnownSkills.ContainsKey(id))
                entity.KnownSkills[id] = 1;
        }
    }

    void SyncOlympiadNoblesse(ZoneEntity entity)
    {
        // Block G2: a claimed hero of the current era is a hero again on every login (Mobius Hero.isHero).
        if (_olympiad.IsHero(entity.ObjectId))
            entity.Hero = true;
        if (entity.Noble)
        {
            if (!_olympiad.IsNoble(entity.ObjectId))
                _olympiad.GrantNoblesse(entity.ObjectId);
        }
        else if (_olympiad.IsNoble(entity.ObjectId))
            entity.Noble = true; // granted by the Olympiad admin path before G1 persisted the flag
    }

    /// <summary>
    /// Mobius <c>setNoble(true)</c>: flag, noble skills, skill list, UserInfo / CharInfo, clan pledge class, Olympiad.
    /// Persisted through the class state. Returns false when the player is not live.
    /// </summary>
    internal bool SetNoble(ZoneEntity player, bool noble)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Session is not { } session)
            return false;
        if (player.Noble == noble)
            return true;
        player.Noble = noble;
        if (noble)
            _olympiad.GrantNoblesse(player.ObjectId);
        RefreshKnownSkills(player);
        RefreshPledgeClass(player);
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        SelfUserInfo(player);
        var peers = KnownNetPlayersOf(player).Where(id => id != player.ObjectId).ToList();
        if (peers.Count > 0)
            NetBridge?.FanoutCharInfo(peers, player.ObjectId);
        SaveClassState(session, player);
        ZoneLog.Info($"noble object={player.ObjectId} noble={noble}");
        return true;
    }

    /// <summary>Admin / test entry: grant or revoke noblesse on a live character.</summary>
    public bool TrySetNoble(int objectId, bool noble)
    {
        lock (_gate)
            return _byId.TryGetValue(objectId, out var player) && SetNoble(player, noble);
    }

    void SendNoblesPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, bool noble)
    {
        string file = noble ? "teleporter/nobles_select.htm" : "teleporter/not_nobles.htm";
        SendHtml(session, player, npc, _html.TryGet(file, out var html)
            ? html
            : "<html><body>%npcname%:<br>" + NpcServiceProtocol.NoblesOnlyText + "</body></html>");
    }

    void RefreshPledgeClass(ZoneEntity player)
    {
        if (player.ClanId == 0)
        {
            player.PledgeClass = PledgeClassOf(null, player.ObjectId, 0, player.Hero);
            return;
        }
        var clan = ClanOf(player.ObjectId);
        if (clan == null)
            return;
        var member = clan.Members.FirstOrDefault(m => m.CharacterId == player.ObjectId);
        player.PledgeClass = PledgeClassOf(clan, player.ObjectId, member?.PledgeType ?? 0, player.Hero);
    }
}
