using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>AcquireSkillType.TRANSFER</c> ordinal.</summary>
    internal const int AcquireSkillTypeTransfer = 5;

    string? TryUseTransferCertificate(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        if (!_game.TransferSkillTrees.IsKnownCertificate(item.ItemId))
            return RefuseUnknownTransferCertificate(session, item.ItemId, player.ObjectId);

        return LearnTransferSkillFromItem(session, player, item);
    }

    /// <summary>Test hook for certificate ids that are not in <c>skill-transfer-trees.json</c>.</summary>
    internal string? RefuseUnknownTransferCertificateForTest(ZoneSession session, int certificateItemId) =>
        RefuseUnknownTransferCertificate(session, certificateItemId, session.ObjectId);

    string? RefuseUnknownTransferCertificate(ZoneSession session, int certificateItemId, int playerObjectId)
    {
        ZoneLog.Warn($"transfer certificate unknown itemId={certificateItemId} object={playerObjectId}");
        SelfLine(session, 129, "This item cannot be used.");
        return null;
    }

    string? LearnTransferSkillFromItem(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        var row = TransferSkills.NextForCertificate(
            _game.TransferSkillTrees,
            _game.Skills,
            _game.Classes,
            player.ClassId,
            player.Level,
            item.ItemId,
            player.KnownSkills);
        if (row == null)
        {
            SelfLine(session, 129, "This item cannot be used.");
            return null;
        }

        return CompleteTransferLearn(session, player, row.Value, item.ObjectId, item.ItemId);
    }

    string? LearnTransferSkill(ZoneSession session, ZoneEntity player, int skillId, int level)
    {
        var row = FindTransferSkillRow(player, skillId, level);
        if (row == null)
            return "bad_request";
        return CompleteTransferLearn(session, player, row.Value, consumeObjectId: 0, expectedCertificateId: 0);
    }

    TransferSkillEntry? FindTransferSkillRow(ZoneEntity player, int skillId, int level)
    {
        int treeClassId = TransferSkills.ResolveTreeClassId(_game.Classes, player.ClassId);
        var entry = TransferSkills.FindEntry(_game.TransferSkillTrees, treeClassId, skillId, level);
        if (entry == null)
            return null;
        if (entry.Value.GetLevel > player.Level)
            return null;
        if (player.KnownSkills.ContainsKey(skillId))
            return null;
        if (_game.Skills.Find(skillId) == null)
            return null;
        return entry;
    }

    string? CompleteTransferLearn(
        ZoneSession session,
        ZoneEntity player,
        TransferSkillEntry row,
        int consumeObjectId,
        int expectedCertificateId)
    {
        if (row.GetLevel > player.Level)
        {
            Notice(session, SkillLevelRequirementMessageId, "You do not meet the skill level requirements.");
            return null;
        }

        if (player.KnownSkills.ContainsKey(row.SkillId))
            return "bad_request";

        if (!_game.TransferSkillTrees.IsKnownCertificate(row.CertificateItemId))
            return RefuseUnknownTransferCertificate(session, row.CertificateItemId, player.ObjectId);

        if (expectedCertificateId > 0 && row.CertificateItemId != expectedCertificateId)
        {
            Notice(session, PrerequisiteMessageId,
                "You do not have the necessary materials or prerequisites to learn this skill.");
            return null;
        }

        var inventory = player.Inventory!;
        if (inventory.CountOf(row.CertificateItemId) < row.Count)
        {
            Notice(session, PrerequisiteMessageId,
                "You do not have the necessary materials or prerequisites to learn this skill.");
            return null;
        }

        if (!RecordBagChange(
                player,
                LedgerTxKind.TrainerSkillLearn,
                Fees,
                () =>
                {
                    var touches = new List<BagTouch>();
                    if (consumeObjectId > 0)
                    {
                        var stack = inventory.Find(consumeObjectId);
                        if (stack == null || stack.ItemId != row.CertificateItemId || stack.Count < row.Count)
                            return false;
                    }
                    if (!inventory.TryTake(row.CertificateItemId, row.Count, touches))
                        return false;

                    if (touches.Count > 0)
                    {
                        SaveInventory(session, inventory);
                    }

                    return true;
                },
                _ => row.SkillId))
        {
            return "bad_request";
        }

        player.LearnedSkills[row.SkillId] = row.Level;
        RefreshKnownSkills(player);
        _persist.ReplaceLearnedSkills(
            session.Account,
            session.ObjectId,
            player.LearnedSkills.Select(p => new Fineage.Accounts.StoredSkill(p.Key, p.Value)).ToList());
        NetBridge?.FanoutSkillList([session.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        string skillName = _game.Skills.Require(row.SkillId).Name;
        Notice(session, 277, $"You have earned {skillName}.");
        ZoneLog.Info($"transfer skill object={player.ObjectId} skill={row.SkillId} lvl={row.Level} cert={row.CertificateItemId}");
        return null;
    }

    void PublishTransferSkillInfo(ZoneSession session, ZoneEntity player, int skillId, int level)
    {
        if (NetBridge == null || session.ObjectId <= 0)
            return;
        var row = FindTransferSkillRow(player, skillId, level);
        if (row == null)
            return;

        var requirements = new[]
        {
            new AcquireSkillRequirementMsg
            {
                Type = 99,
                ItemId = row.Value.CertificateItemId,
                Count = row.Value.Count,
                Unk = 50
            }
        };

        NetBridge.FanoutAcquireSkillInfo([player.ObjectId], new AcquireSkillInfoMsg
        {
            SkillId = row.Value.SkillId,
            Level = row.Value.Level,
            SpCost = 0,
            SkillType = AcquireSkillTypeTransfer,
            Requirements = requirements
        });
    }
}