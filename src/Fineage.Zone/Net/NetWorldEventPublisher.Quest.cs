using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.QuestListMsg list)
    {
        var payload = NetCodec.EncodeQuestList(ToNetQuestList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.QuestList, payload);
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

        return new QuestListNetMsg
        {
            Active = active,
            CompletedMask = list.CompletedMask ?? [],
            CompletedQuestIds = list.CompletedQuestIds ?? [],
            MaxActive = list.MaxActive
        };
    }
}
