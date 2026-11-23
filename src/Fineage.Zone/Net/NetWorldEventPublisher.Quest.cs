using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.QuestListMsg list)
    {
        var payload = NetCodec.EncodeQuestList(ToNetQuestList(list));
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.QuestList, payload);
    }

    internal static QuestListNetMsg ToNetQuestList(Fineage.Protocol.QuestListMsg list)
    {
        var src = list.Active ?? [];
        var active = new QuestJournalEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var entry = src[i];
            active[i] = new QuestJournalEntryNet
            {
                QuestId = entry.QuestId,
                Name = entry.Name ?? "",
                State = entry.State,
                Cond = entry.Cond,
                ProgressFlags = entry.ProgressFlags,
                JournalWord = entry.JournalWord,
                Repeatable = entry.Repeatable
            };
        }

        var rewardSets = (list.RewardSets ?? []).Select(set => new QuestJournalRewardSetNet
        {
            QuestId = set.QuestId,
            Rows = (set.Rows ?? []).Select(r => new QuestJournalRewardNet
            {
                Kind = r.Kind,
                ItemId = r.ItemId,
                Count = r.Count,
                Label = r.Label ?? ""
            }).ToArray()
        }).ToArray();

        return new QuestListNetMsg
        {
            Active = active,
            CompletedMask = list.CompletedMask ?? [],
            CompletedQuestIds = list.CompletedQuestIds ?? [],
            MaxActive = list.MaxActive,
            RewardSets = rewardSets
        };
    }

    void IWorldEventPublisher.FanoutQuestDirection(IReadOnlyList<int> watcherObjectIds, QuestDirectionNetMsg msg)
    {
        var payload = NetCodec.EncodeQuestDirection(msg);
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.QuestDirection, payload);
    }

    void IWorldEventPublisher.FanoutQuestNpcMarks(IReadOnlyList<int> watcherObjectIds, QuestNpcMarksNetMsg msg)
    {
        var payload = NetCodec.EncodeQuestNpcMarks(msg);
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.QuestNpcMarks, payload);
    }

    void IWorldEventPublisher.FanoutTutorialUi(IReadOnlyList<int> watcherObjectIds, TutorialUiNetMsg msg)
    {
        var payload = NetCodec.EncodeTutorialUi(msg);
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.TutorialUi, payload);
    }
}
