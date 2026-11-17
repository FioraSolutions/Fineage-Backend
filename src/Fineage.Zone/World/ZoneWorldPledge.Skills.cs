using Fineage.Game.Services;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Pledge skills after Mobius <c>VillageMaster.showPledgeSkillList</c> and <c>RequestAcquireSkill</c> (<c>PLEDGE</c>):
/// leader only, the clan level gates the next level of each skill, the price is reputation (<c>levelUpSp</c>)
/// plus the tree items (<c>LifeCrystalNeeded=true</c>).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>AcquireSkillType.PLEDGE</c> ordinal.</summary>
    internal const int AcquireSkillTypePledge = 2;

    string? ShowPledgeSkills(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var clan = ClanOf(player.ObjectId);
        if (clan == null || clan.LeaderId != player.ObjectId)
        {
            SendClanPage(session, player, npc, "villagemaster/NotClanLeader.htm");
            return null;
        }

        session.TrainerNpcId = npc.ObjectId;
        var rows = PledgeSkills.Available(clan.Level, clan.Skills);
        if (rows.Count == 0)
        {
            if (clan.Level < 8)
            {
                int next = clan.Level < 5 ? 5 : clan.Level + 1;
                SelfLine(session, 607, $"You do not have any further skills to learn. Come back when you have reached Level {next}.", next.ToString());
            }
            else
                SendClanPage(session, player, npc, "villagemaster/NoMoreSkills.htm");
            return null;
        }

        NetBridge?.FanoutAcquireSkillList([player.ObjectId], new AcquireSkillListMsg
        {
            SkillType = AcquireSkillTypePledge,
            Skills = rows.Select(r => new AcquireSkillEntryMsg
            {
                SkillId = r.SkillId,
                NextLevel = r.Level,
                MaxLevel = r.Level,
                SpCost = r.Reputation,
                Requirements = (int)r.SocialClass
            }).ToArray()
        });
        return null;
    }

    void PublishPledgeSkillInfo(ZoneEntity player, int skillId, int level)
    {
        var clan = ClanOf(player.ObjectId);
        if (clan == null || NetBridge == null)
            return;
        var row = PledgeSkills.Available(clan.Level, clan.Skills).FirstOrDefault(r => r.SkillId == skillId && r.Level == level);
        if (row == null)
            return;
        NetBridge.FanoutAcquireSkillInfo([player.ObjectId], new AcquireSkillInfoMsg
        {
            SkillId = row.SkillId,
            Level = row.Level,
            SpCost = row.Reputation,
            SkillType = AcquireSkillTypePledge,
            Requirements = row.Items.Select(i => new AcquireSkillRequirementMsg { Type = 99, ItemId = i.ItemId, Count = i.Count, Unk = 50 }).ToArray()
        });
    }

    string? LearnPledgeSkill(ZoneSession session, ZoneEntity player, int skillId, int level)
    {
        var clan = ClanOf(player.ObjectId);
        if (clan == null || clan.LeaderId != player.ObjectId)
            return PledgeProtocol.Denied;
        var npc = TrainerNpcOf(session);
        var row = PledgeSkills.Available(clan.Level, clan.Skills).FirstOrDefault(r => r.SkillId == skillId && r.Level == level);
        if (row == null)
        {
            ShowPledgeSkills(session, player, npc);
            return "bad_request";
        }

        if (clan.D.Reputation < row.Reputation)
        {
            SelfLine(session, 1852, "The attempt to acquire the skill has failed because of an insufficient Clan Reputation Score.");
            ShowPledgeSkills(session, player, npc);
            return PledgeProtocol.Reputation;
        }

        var inventory = player.Inventory;
        if (row.Items.Any(i => inventory == null || inventory.CountOf(i.ItemId) < i.Count))
        {
            SelfLine(session, 276, "You do not have the necessary materials or prerequisites to learn this skill.");
            ShowPledgeSkills(session, player, npc);
            return PledgeProtocol.Conditions;
        }

        if (row.Items.Count > 0)
        {
            var changes = new List<InventoryUpdateEntryMsg>();
            foreach (var item in row.Items)
                Take(inventory!, item.ItemId, item.Count, changes);
            FanInventory(session, Collapse(changes));
            SaveInventory(session, inventory!);
        }

        clan.Skills[row.SkillId] = row.Level;
        SelfLine(session, 1787, $"{row.Reputation} points have been deducted from the clan's reputation score.", row.Reputation.ToString());
        ChangeReputation(clan, -row.Reputation, save: false);
        SaveClan(clan);
        ClanLine(OnlineIds(clan), 1788, $"The clan skill {row.Name} has been added.", row.Name);
        BroadcastClan(clan);
        ShowPledgeSkills(session, player, npc);
        return null;
    }
}
