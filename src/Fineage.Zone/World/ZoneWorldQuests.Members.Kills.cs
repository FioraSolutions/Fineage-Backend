using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Rewards;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void GrantKillReward(ZoneEntity killer, ZoneEntity npc)
    {
        if (killer.Kind != ZoneEntityKind.Player || killer.Session == null || npc.Kind != ZoneEntityKind.Npc)
            return;
        ApplyQuestKill(killer, npc);
        DistributeKillReward(npc);
    }

    /// <summary>
    /// Mobius <c>Attackable.calculateRewards</c>: exp/SP come from the monster template and are shared by the damage
    /// share (not paid to the last hit). Zero outside <c>MonsterExpMaxLevelDifference</c>.
    /// </summary>
    void DistributeKillReward(ZoneEntity npc)
    {
        long rewardExp = npc.RewardExp;
        int rewardSp = npc.RewardSp;
        if (rewardExp <= 0 && rewardSp <= 0)
            return;

        // Mobius: damage above 1 counts, and the attacker has to be in ALT_PARTY_RANGE of the corpse.
        var damage = new Dictionary<int, long>();
        long totalDamage = 0;
        foreach (var (id, dealt) in npc.DamageBy)
        {
            if (dealt <= 1 || !_byId.TryGetValue(id, out var attacker) || attacker.Kind != ZoneEntityKind.Player ||
                attacker.Session == null || attacker.Dead)
                continue;
            if (!ZoneCombat.Within(npc.X, npc.Y, attacker.X, attacker.Y, QuestPartyRange))
                continue;
            damage[id] = dealt;
            totalDamage += dealt;
        }

        if (totalDamage <= 0)
            return;

        int maxLevelDifference = ZoneRewardSettings.MonsterExpMaxLevelDifference;
        var settled = new HashSet<int>();
        foreach (var (id, dealt) in damage)
        {
            if (settled.Contains(id) || !_byId.TryGetValue(id, out var attacker) || attacker.Session == null)
                continue;

            if (_partyByPlayer.TryGetValue(id, out int partyId) && _parties.TryGetValue(partyId, out var party))
            {
                SharePartyKill(party, npc, damage, totalDamage, rewardExp, rewardSp, maxLevelDifference, settled);
                continue;
            }

            settled.Add(id);
            var share = KillRewardFormula.CalculateExpAndSp(
                attacker.Level, npc.Level, rewardExp, rewardSp, dealt, totalDamage, maxLevelDifference);
            SendKillReward(
                attacker.Session,
                attacker,
                share.Exp * _rates.RateXp,
                share.Sp * _rates.RateSp);
        }
    }

    /// <summary>Mobius <c>Party.distributeXpAndSp</c> over the members of one party that took part in the kill.</summary>
    void SharePartyKill(
        ZoneParty party,
        ZoneEntity npc,
        Dictionary<int, long> damage,
        long totalDamage,
        long rewardExp,
        int rewardSp,
        int maxLevelDifference,
        HashSet<int> settled)
    {
        var members = new List<ZoneEntity>(party.MemberIds.Count);
        long partyDamage = 0;
        int partyLevel = 0;
        foreach (int memberId in party.MemberIds)
        {
            if (!_byId.TryGetValue(memberId, out var member) || member.Kind != ZoneEntityKind.Player ||
                member.Session == null || member.Dead)
                continue;
            if (!ZoneCombat.Within(npc.X, npc.Y, member.X, member.Y, QuestPartyRange))
                continue;
            members.Add(member);
            settled.Add(memberId);
            partyDamage += damage.GetValueOrDefault(memberId);
            partyLevel = Math.Max(partyLevel, member.Level);
        }

        if (members.Count == 0 || partyDamage <= 0)
            return;

        // Mobius PartyXpCutoffMethod = level: members more than PartyXpCutoffLevel below the top level earn nothing.
        var valid = members.Where(m => partyLevel - m.Level <= KillRewardFormula.PartyXpCutoffLevel).ToList();
        if (valid.Count == 0)
            return;

        double partyMul = partyDamage < totalDamage ? (double)partyDamage / totalDamage : 1;
        var pool = KillRewardFormula.CalculateExpAndSp(
            partyLevel, npc.Level, rewardExp, rewardSp, partyDamage, totalDamage, maxLevelDifference);
        double bonus = KillRewardFormula.PartyBonus(valid.Count);
        double expPool = pool.Exp * partyMul * bonus * _rates.RateXp;
        double spPool = pool.Sp * partyMul * bonus * _rates.RateSp;

        long sqLevelSum = 0;
        foreach (var member in valid)
            sqLevelSum += (long)member.Level * member.Level;
        if (sqLevelSum <= 0)
            return;

        foreach (var member in valid)
        {
            double share = (double)((long)member.Level * member.Level) / sqLevelSum;
            SendKillReward(member.Session!, member, expPool * share, spPool * share);
        }
    }

    /// <summary>
    /// Pays one player's share: exp/SP into the counter, SystemMessage 95, StatusUpdate and UserInfo, save, then the
    /// level-up packets when the exp crossed a row.
    /// </summary>
    void SendKillReward(ZoneSession session, ZoneEntity player, double rawExp, double rawSp)
    {
        long exp = KillRewardFormula.Round(rawExp);
        int sp = (int)Math.Min(int.MaxValue, KillRewardFormula.Round(rawSp));
        if (exp <= 0 && sp <= 0)
            return;

        bool levelled = ApplyExpSp(player, exp, sp);
        var notice = new SystemMessageMsg
        {
            Id = KillRewardMessageId,
            Params = [exp.ToString(System.Globalization.CultureInfo.InvariantCulture), sp.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Text = $"You have earned {exp} experience and {sp} SP."
        };
        // StatusUpdate carries live Exp/Sp/Level; UserInfo refreshes the full sheet (vitals + template stats).
        var status = new StatusUpdateMsg
        {
            ObjectId = player.ObjectId,
            AttributeIds =
            [
                InterludeStatus.Level,
                InterludeStatus.Exp,
                InterludeStatus.Sp,
                InterludeStatus.CurHp,
                InterludeStatus.MaxHp,
                InterludeStatus.CurMp,
                InterludeStatus.MaxMp
            ],
            Values =
            [
                player.Level,
                (int)Math.Min(int.MaxValue, player.Exp),
                player.Sp,
                player.CurHp,
                player.MaxHp,
                player.CurMp,
                player.MaxMp
            ]
        };
        SelfUserInfo(player);

        if (NetBridge != null)
        {
            int[] self = [player.ObjectId];
            var vitals = BuildUserInfoVitals(player);
            NetBridge.FanoutSystemMessage(self, new SystemMessageMsg
            {
                Id = notice.Id,
                Text = notice.Text,
                Params = notice.Params ?? []
            });
            NetBridge.FanoutStatusUpdate(self, status);
            NetBridge.FanoutPlayerStats(self, new Fineage.Protocol.PlayerStatsMsg
            {
                ObjectId = player.ObjectId,
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
        }

        SaveQuestProgress(session, player);

        // Mobius addExpAndSp → addLevel: level up after the exp notice so the client sees "earned exp", then level up.
        if (levelled)
            ApplyLevelUp(session, player);
    }

    internal const int KillRewardMessageId = 95;

    void SaveQuestProgress(ZoneSession session, ZoneEntity entity) =>
        _persist.SaveQuestProgress(session.Account, session.ObjectId, entity);

    bool TryPlayer(ZoneSession session, out ZoneEntity player)
    {
        if (_byId.TryGetValue(session.ObjectId, out player!) &&
            player.Kind == ZoneEntityKind.Player &&
            player.Inventory != null)
            return true;
        player = null!;
        return false;
    }

    bool InTalkRange(ZoneEntity player, ZoneEntity npc) =>
        NpcHtmlProtocol.InRange(player.X, player.Y, player.Z, npc.X, npc.Y, npc.Z, _game.Quests.TalkRange);

    static bool EventOk(string text)
    {
        foreach (char c in text)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }

        return true;
    }

    static string FailureCode(QuestFailure failure) => failure switch
    {
        QuestFailure.Unknown => QuestProtocol.Unknown,
        QuestFailure.Limit => QuestProtocol.Limit,
        QuestFailure.State => QuestProtocol.State,
        QuestFailure.Abort => QuestProtocol.Abort,
        QuestFailure.Item => QuestProtocol.Item,
        QuestFailure.BadRequest => "bad_request",
        _ => "bad_request"
    };
}
