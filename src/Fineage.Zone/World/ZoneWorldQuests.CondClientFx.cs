using Fineage.Game.Quests;
using Fineage.Net.Contract;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void EmitCondClientFx(ZoneSession session, ZoneEntity player, int questId, int cond, bool playSounds)
    {
        if (NetBridge == null || _quests == null)
            return;
        if (_quests.QuestDef(questId) is not { } quest)
            return;
        if (!quest.CondClientFx.TryGetValue(cond, out var ops) || ops.Count == 0)
            return;
        ApplyClientFxOps(session, player, ops, playSounds);
    }

    void ApplyClientFxOps(ZoneSession session, ZoneEntity player, IReadOnlyList<QuestOp> ops, bool playSounds)
    {
        int[] self = [session.ObjectId];
        foreach (QuestOp op in ops)
        {
            switch (op)
            {
                case SoundOp sound when playSounds:
                    if (!string.IsNullOrWhiteSpace(sound.Name))
                        NetBridge!.FanoutPlaySound(self, 0, sound.Name, 0, 0, 0, 0, 0);
                    break;
                case TutorialUiOp tutorial:
                    NetBridge!.FanoutTutorialUi(self, ToTutorialNet(tutorial));
                    break;
            }
        }
    }

    void ApplyTutorialUiOutcome(ZoneSession session, IReadOnlyList<TutorialUiOp> rows)
    {
        if (NetBridge == null || rows.Count == 0)
            return;
        int[] self = [session.ObjectId];
        foreach (var row in rows)
            NetBridge.FanoutTutorialUi(self, ToTutorialNet(row));
    }

    static TutorialUiNetMsg ToTutorialNet(TutorialUiOp op) => new()
    {
        Kind = (TutorialUiKind)op.Kind,
        Html = op.Html ?? "",
        EventId = op.EventId,
        On = op.On
    };
}