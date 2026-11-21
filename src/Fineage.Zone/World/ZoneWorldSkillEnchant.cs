using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    const int EnchantSuccessMessageId = 1438;
    const int EnchantFailMessageId = 1440;
    const int EnchantNotEnoughSpMessageId = 1441;
    const int EnchantNotEnoughExpMessageId = 1442;
    const int EnchantMissingItemsMessageId = 1443;

    public Task<string?> TryEnchantSkillInfoAsync(ZoneSession session, int skillId, int skillLevel, CancellationToken ct) =>
        Task.FromResult(TryEnchantSkillInfo(session, skillId, skillLevel));

    public Task<string?> TryEnchantSkillAsync(ZoneSession session, int skillId, int skillLevel, CancellationToken ct) =>
        Task.FromResult(WithDeferredInventoryPersist(() => TryEnchantSkill(session, skillId, skillLevel)));

    internal string? EnchantSkillForTest(ZoneSession session, int skillId, int skillLevel, bool informing) =>
        informing ? TryEnchantSkillInfo(session, skillId, skillLevel) : TryEnchantSkill(session, skillId, skillLevel);

    string? TryEnchantSkillInfo(ZoneSession session, int skillId, int skillLevel)
    {
        if (!SkillEnchantRules.TrainerEnchantEnabled)
            return "disabled";
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (!SkillEnchantService.TryResolveRoute(
                _game.SkillEnchants, player.KnownSkills, player.Level, player.ClassId, skillId, skillLevel, out var route))
        {
            ZoneLog.Warn($"skill-enchant missing route skillId={skillId} targetLevel={skillLevel}");
            return "bad_request";
        }

        var reqs = new List<EnchantSkillRequirementMsg>();
        if (SkillEnchantService.RequiresSpellbook(skillLevel) && player.Inventory?.CountOf(SkillEnchantService.SpellbookItemId) >= 1)
            reqs.Add(new EnchantSkillRequirementMsg { Type = 4, ItemId = SkillEnchantService.SpellbookItemId, Count = 1, Unk = 0 });
        else if (SkillEnchantService.RequiresSpellbook(skillLevel))
            reqs.Add(new EnchantSkillRequirementMsg { Type = 4, ItemId = SkillEnchantService.SpellbookItemId, Count = 1, Unk = 0 });

        NetBridge?.FanoutEnchantSkillInfo([player.ObjectId], new EnchantSkillInfoMsg
        {
            SkillId = route.SkillId,
            Level = route.Level,
            SpCost = route.Sp,
            ExpCost = route.Exp,
            Rate = route.RateForPlayerLevel(player.Level),
            Requirements = reqs.ToArray()
        });
        return null;
    }

    string? TryEnchantSkill(ZoneSession session, int skillId, int skillLevel)
    {
        if (!SkillEnchantRules.TrainerEnchantEnabled)
            return "disabled";
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (!SkillEnchantService.TryResolveRoute(
                _game.SkillEnchants, player.KnownSkills, player.Level, player.ClassId, skillId, skillLevel, out var route))
        {
            ZoneLog.Warn($"skill-enchant missing route skillId={skillId} targetLevel={skillLevel}");
            return "bad_request";
        }

        if (player.KnownSkills.TryGetValue(skillId, out int have) && have >= skillLevel)
            return "bad_request";

        if (player.Sp < route.Sp)
        {
            Notice(session, EnchantNotEnoughSpMessageId, "You do not have enough SP to enchant that skill.");
            return null;
        }

        var table = _game.Classes?.Experience;
        if (table != null && route.Exp > 0)
        {
            long floor = ExpFloorForEnchant(table, player.Level);
            if (player.Exp < route.Exp || player.Exp - route.Exp < floor)
            {
                Notice(session, EnchantNotEnoughExpMessageId, "You do not have enough experience (EXP) to enchant that skill.");
                return null;
            }
        }

        if (SkillEnchantService.RequiresSpellbook(skillLevel))
        {
            var inventory = player.Inventory;
            if (inventory == null || inventory.CountOf(SkillEnchantService.SpellbookItemId) < 1)
            {
                Notice(session, EnchantMissingItemsMessageId, "You do not have all of the items needed to enchant that skill.");
                return null;
            }

            if (!RecordBagChange(
                    player,
                    LedgerTxKind.TrainerSkillEnchant,
                    Fees,
                    () =>
                    {
                        var touches = new List<BagTouch>();
                        if (!inventory.TryTake(SkillEnchantService.SpellbookItemId, 1, touches))
                            return false;
                        if (touches.Count > 0)
                        {
                            var invUpdate = new InventoryUpdateMsg
                            {
                                Changes = touches.Select(t => InventoryMessages.ChangeRow((int)t.Kind, t.Item)).ToArray()
                            };
                            NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
                            SaveInventory(session, player.Inventory!);
                        }

                        return true;
                    },
                    _ => skillId))
                return "bad_request";
        }

        if (!RecordProgressSpend(player, LedgerTxKind.TrainerSkillEnchant, skillId, route.Sp, route.Exp, () =>
            {
                player.Sp -= route.Sp;
                if (route.Exp > 0)
                    player.Exp -= route.Exp;
            }))
            return "bad_request";

        int rate = route.RateForPlayerLevel(player.Level);
        bool success = Rng(_combatRandom, player).Next(100) < rate;
        if (success)
        {
            player.LearnedSkills[skillId] = skillLevel;
            RefreshKnownSkills(player);
            _persist.ReplaceLearnedSkills(
                session.Account,
                session.ObjectId,
                player.LearnedSkills.Select(p => new Fineage.Accounts.StoredSkill(p.Key, p.Value)).ToList());
            SaveQuestProgress(session, player);
            NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
            Notice(session, EnchantSuccessMessageId, "Skill enchant was successful.");
        }
        else
        {
            if (skillLevel > 100)
            {
                player.LearnedSkills[skillId] = route.BaseLevel;
                RefreshKnownSkills(player);
                _persist.ReplaceLearnedSkills(
                    session.Account,
                    session.ObjectId,
                    player.LearnedSkills.Select(p => new Fineage.Accounts.StoredSkill(p.Key, p.Value)).ToList());
                NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
            }

            Notice(session, EnchantFailMessageId, "Skill enchant failed. The skill will be initialized.");
        }

        PublishEnchantSkillList(session, player);
        return null;
    }

    void PublishEnchantSkillList(ZoneSession session, ZoneEntity player)
    {
        if (NetBridge == null)
            return;
        var offers = SkillEnchantService.ListOffers(_game.SkillEnchants, player.KnownSkills, player.Level, player.ClassId);
        var rows = new EnchantSkillListEntryMsg[offers.Count];
        for (int i = 0; i < offers.Count; i++)
        {
            var row = offers[i];
            rows[i] = new EnchantSkillListEntryMsg
            {
                SkillId = row.SkillId,
                NextLevel = row.Level,
                Sp = row.Sp,
                Exp = row.Exp
            };
        }

        NetBridge.FanoutEnchantSkillList([player.ObjectId], new EnchantSkillListMsg { Skills = rows });
    }

    static long ExpFloorForEnchant(ExperienceTable table, int level)
    {
        if (level <= 1)
            return 0;
        int highest = Math.Min(table.MaxLevel, table.HighestRow);
        if (level > highest)
            level = highest;
        return table.ExpToBeLevel(level);
    }
}