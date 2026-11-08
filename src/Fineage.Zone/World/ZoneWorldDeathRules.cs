using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius death consequences for a player: exp loss from the level's exp bar
/// (<c>Player.calculateDeathExpPenalty</c>, <c>Player.java:5318-5363</c>), the Death Penalty roll and skill 5076
/// (<c>Player.calculateDeathPenaltyBuffLevel</c> / <c>increaseDeathPenaltyBuffLevel</c>, <c>:11690-11743</c>), and the
/// delevel that <c>PlayerStat.removeExp</c> may cause (<c>PlayableStat.java:94-122</c>).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>SystemMessageId 1916 YOUR_DEATH_PENALTY_IS_NOW_LEVEL_S1</c>.</summary>
    internal const int DeathPenaltyMessageId = 1916;

    /// <summary>
    /// Mobius <c>Player.deathPenalty(...)</c> (<c>:4889-4931</c>): a death that came from a monster costs exp off the
    /// current level's bar, then rolls the Death Penalty buff. Both are skipped when nothing killed the character
    /// (admin kill, scripted death), exactly like Mobius, which warns and returns on a null killer.
    /// </summary>
    void ApplyDeathConsequences(ZoneEntity player, int killerObjectId)
    {
        if (player.Session == null || killerObjectId <= 0)
            return;
        if (!_byId.TryGetValue(killerObjectId, out var killer))
            return;

        // Non-player killers are monsters / raid bosses, and Mobius charges both for a PvE death. A player killer is
        // PvP: Mobius only removes exp inside a siege or outside a PVP zone, and never rolls the penalty without
        // karma — which this zone does not model yet, so a clean PvP kill costs nothing.
        if (killer.Kind != ZoneEntityKind.Npc)
            return;

        ApplyDeathExpLoss(player);
        RollDeathPenalty(player, killer);
    }

    /// <summary>
    /// Mobius <c>Player.calculateDeathExpPenalty</c>: the bar of the level the character is on is what the share is
    /// taken from, so the loss follows the level table and not the absolute exp. The exp never goes below the start
    /// of the level, and <c>PlayableStat.removeExp</c> delevels when the bar underflows.
    /// </summary>
    void ApplyDeathExpLoss(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        double percent = _deathRules.PercentForLevel(player.Level);
        if (percent <= 0d)
            return;

        var table = _game.Classes?.Experience;
        if (table == null)
            return;

        long levelBarStart = ExpToBeLevel(table, player.Level);
        long levelBarEnd = ExpToBeLevel(table, player.Level + 1);
        if (levelBarEnd <= levelBarStart)
            return;

        long lost = _deathRules.ExpLost(player.Level, levelBarEnd - levelBarStart);
        if (lost <= 0)
            return;

        // PlayableStat.removeExp: exp never goes negative, and when it falls under the level's own floor the level
        // follows it down (Delevel, the reference default) instead of the bar being clipped.
        long before = player.Exp;
        player.Exp = Math.Max(0, before - lost);

        int level = player.Level;
        if (player.Exp < levelBarStart && player.Level > 1)
            level = LevelForExp(table, player.Exp);
        bool deleveled = level < player.Level;
        int delevelAmount = player.Level - level;
        player.Level = level;

        // Mobius Delevel = true: the bar is re-synced to the level the exp fell into, then addLevel runs for the
        // difference. A delevel never refills HP/MP (only a level *gain* does), so the maxima move but the bars stay.
        if (deleveled && _game.Classes != null && _game.Classes.TryGet(player.ClassId, out var template) && template != null)
        {
            player.MaxHp = template.ModifiedHp(player.Level);
            player.MaxMp = template.ModifiedMp(player.Level);
            player.CurHp = Math.Min(player.CurHp, player.MaxHp);
            player.CurMp = Math.Min(player.CurMp, player.MaxMp);
            DecreaseSkillsOnDelevel(player);
            RefreshKnownSkills(player);
            RefreshGradePenalty(player);
            RefreshWeightPenalty(player);
        }

        ZoneLog.Info(
            $"[death] exp object={player.ObjectId} level={player.Level} lost={lost} ({before}->{player.Exp})" +
            (deleveled ? $" delevel={delevelAmount}" : ""));

        if (player.Session is { } session)
            SendExpStatus(session, player, deleveled);
    }

    /// <summary>
    /// Mobius <c>Player.calculateDeathPenaltyBuffLevel</c> (<c>:11690-11720</c>): a <c>DeathPenaltyChance</c> roll from
    /// a monster death adds one Death Penalty level, and <c>increaseDeathPenaltyBuffLevel</c> then applies skill 5076,
    /// sends <c>EtcStatusUpdate</c> and message 1916.
    /// </summary>
    void RollDeathPenalty(ZoneEntity player, ZoneEntity killer)
    {
        if (player.DeathPenaltyLevel >= DeathPenalty.MaxLevel)
            return;
        int chance = _deathRules.DeathPenaltyChance;
        if (chance <= 0)
            return;

        // Mobius Rnd.get(1, 100) <= chance: an inclusive 1..chance roll out of 100.
        int roll = _deathRandom.Next(1, 101);
        if (roll > chance)
            return;

        player.DeathPenaltyLevel = Math.Min(player.DeathPenaltyLevel + 1, DeathPenalty.MaxLevel);
        // The passive fold is not in KnownSkills, so the level alone changes the stats: recompute the live speed and
        // hand the owner the new sheet, then tell them the level went up.
        RefreshRunSpeedAndFanout(player);
        FanoutNetEtcStatus(player);

        int level = player.DeathPenaltyLevel;
        ZoneLog.Info($"[death] penalty object={player.ObjectId} npc={killer.NpcId} roll={roll}/{chance} level={level}");

        if (player.Session is not { } session)
            return;
        NetBridge?.FanoutSystemMessage([player.ObjectId], new SystemMessageMsg
        {
            Id = DeathPenaltyMessageId,
            Text = $"Your Death Penalty is now level {level}.",
            Params = [level.ToString(System.Globalization.CultureInfo.InvariantCulture)]
        });
        // The level is durable: Mobius writes characters.death_penalty_level with the rest of the row on save.
        _persist.SaveDeathPenalty(session.Account, session.ObjectId, level);
    }

    /// <summary>
    /// Exp / level sheet after a death loss. Mobius sends <c>UserInfo</c> from <c>PlayerStat.addLevel</c>; the
    /// StatusUpdate is what the client's exp bar actually redraws from, so both go out.
    /// </summary>
    void SendExpStatus(ZoneSession session, ZoneEntity player, bool deleveled)
    {
        var status = new StatusUpdateMsg
        {
            ObjectId = player.ObjectId,
            AttributeIds =
            [
                InterludeStatus.Level,
                InterludeStatus.Exp,
                InterludeStatus.MaxHp,
                InterludeStatus.MaxMp
            ],
            Values =
            [
                player.Level,
                (int)Math.Min(int.MaxValue, player.Exp),
                player.MaxHp,
                player.MaxMp
            ]
        };
        SelfUserInfo(player);
        if (NetBridge == null)
            return;
        int[] self = [player.ObjectId];
        NetBridge.FanoutStatusUpdate(self, status);
        var vitals = BuildUserInfoVitals(player);
        NetBridge.FanoutPlayerStats(self, new Fineage.Net.Contract.PlayerStatsMsg
        {
            EntityValue = (ulong)(uint)player.ObjectId,
            EntityGeneration = 1,
            Level = player.Level,
            Exp = player.Exp,
            Sp = player.Sp,
            Hp = player.CurHp,
            MaxHp = player.MaxHp,
            Mp = player.CurMp,
            MaxMp = player.MaxMp,
            Cp = vitals.CurCp,
            MaxCp = vitals.MaxCp
        });
        if (deleveled)
            NetBridge.FanoutSkillList(self,
                Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
    }

    /// <summary>
    /// Mobius <c>Player.checkPlayerSkills</c> (<c>:12602-12640</c>) under <c>DecreaseSkillOnDelevel</c>: a trainer
    /// skill whose row is now more than nine levels above the character (Expertise is exempt) drops to the highest
    /// row still in range, or leaves the book when there is none. Auto-get rows are re-derived from the level by
    /// <see cref="RefreshKnownSkills"/>, so only the learned list is touched here.
    /// </summary>
    void DecreaseSkillsOnDelevel(ZoneEntity player)
    {
        if (player.LearnedSkills.Count == 0 || _game.Classes == null)
            return;

        IReadOnlyList<int> chain = _game.Classes.TryGet(player.ClassId, out _)
            ? _game.Classes.ParentChain(player.ClassId)
            : [player.ClassId];
        var trees = new List<ClassSkillTree>(chain.Count);
        foreach (int chainClassId in chain)
        {
            var tree = _game.SkillTrees.FindClass(chainClassId);
            if (tree != null)
                trees.Add(tree);
        }
        if (trees.Count == 0)
            return;

        foreach (var (skillId, learnedLevel) in player.LearnedSkills.ToList())
        {
            // Mobius looks the learned level up in the class tree; a skill that is not in the tree keeps its level.
            bool inTree = trees.Any(t => t.Skills.Any(e => e.SkillId == skillId && e.Level == learnedLevel));
            if (!inTree)
                continue;

            int levelDiff = skillId == Expertise.SkillId ? 0 : 9;
            var row = trees
                .SelectMany(t => t.Skills)
                .FirstOrDefault(e => e.SkillId == skillId && e.Level == learnedLevel);
            if (row.SkillId == 0 || player.Level >= row.GetLevel - levelDiff)
                continue;

            int next = -1;
            foreach (var e in trees.SelectMany(t => t.Skills))
            {
                if (e.SkillId != skillId || e.Level <= next)
                    continue;
                if (player.Level >= e.GetLevel - levelDiff)
                    next = e.Level;
            }

            if (next <= 0)
                player.LearnedSkills.Remove(skillId);
            else
                player.LearnedSkills[skillId] = next;
        }
    }

    /// <summary>Mobius <c>ExperienceData.getExpForLevel</c>; levels outside the table clamp to its ends.</summary>
    static long ExpToBeLevel(ExperienceTable table, int level)
    {
        if (level <= 1)
            return 0;
        int highest = Math.Min(table.MaxLevel, table.HighestRow);
        if (level > highest)
            level = highest;
        return table.ExpToBeLevel(level);
    }

    /// <summary>Highest level whose cumulative exp is still &lt;= <paramref name="exp"/> (Mobius <c>removeExp</c>).</summary>
    static int LevelForExp(ExperienceTable table, long exp)
    {
        int highest = Math.Min(table.MaxLevel, table.HighestRow);
        int level = 1;
        for (int candidate = 2; candidate <= highest; candidate++)
        {
            if (exp < table.ExpToBeLevel(candidate))
                break;
            level = candidate;
        }
        return level;
    }
}
