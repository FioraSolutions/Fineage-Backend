namespace Fineage.Game.Quests;

/// <summary>
/// Mobius Q247 noble grant step (<c>set_noble</c> + tiara + <c>give_exp_sp</c>): village masters charge SP/adena from that
/// data when the gate quest is ready (cond 2+) instead of the Lady of the Lake (Interlude has no <c>RequestNobless</c> in Java).
/// </summary>
public static class NoblesseGrant
{
    public const int GateQuestId = 247;
    public const int GateReadyCond = 2;

    public readonly record struct VillageMasterCosts(int Sp, int Adena, int TiaraItemId, long RewardExp);

    /// <summary>
    /// SP cost is <c>give_exp_sp.sp</c> when positive, otherwise <c>give_exp_sp.exp</c>. Adena is ten times the SP cost
    /// (certification fee tiered from the quest reward row).
    /// </summary>
    public static bool TryReadVillageMasterCosts(QuestDefinition? quest, out VillageMasterCosts costs)
    {
        costs = default;
        if (quest is null || quest.Id != GateQuestId)
            return false;
        foreach (var dialog in quest.Dialogs)
        {
            if (!dialog.Ops.Any(op => op is SetNobleOp))
                continue;
            int tiara = 7694;
            long rewardExp = 0;
            int spCost = 0;
            foreach (var op in dialog.Ops)
            {
                switch (op)
                {
                    case GiveItemOp give when give.Count > 0:
                        tiara = give.ItemId;
                        break;
                    case GiveExpSpOp exp:
                        rewardExp = exp.Exp;
                        spCost = exp.Sp > 0
                            ? exp.Sp
                            : exp.Exp > int.MaxValue
                                ? int.MaxValue
                                : (int)exp.Exp;
                        break;
                }
            }

            if (spCost <= 0)
                return false;
            int adena = spCost > int.MaxValue / 10 ? int.MaxValue : spCost * 10;
            costs = new VillageMasterCosts(spCost, adena, tiara, rewardExp);
            return true;
        }

        return false;
    }
}