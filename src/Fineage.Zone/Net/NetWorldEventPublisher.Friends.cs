using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendInviteAskMsg ask)
    {
        var payload = NetCodec.EncodeFriendInviteAsk(new FriendInviteAskNetMsg
        {
            RequestorName = ask.RequestorName ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendInviteAsk, payload);
    }

    void IWorldEventPublisher.FanoutFriendList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendListMsg list)
    {
        var payload = NetCodec.EncodeFriendList(ToNetFriendList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendList, payload);
    }

    void IWorldEventPublisher.FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendUpdateMsg update)
    {
        var payload = NetCodec.EncodeFriendUpdate(new FriendUpdateNetMsg
        {
            Added = update.Added,
            ObjectId = update.ObjectId,
            Name = update.Name ?? "",
            Online = update.Online
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.FriendUpdate, payload);
    }

    void IWorldEventPublisher.FanoutBlockList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BlockListMsg list)
    {
        var src = list.Blocked ?? [];
        var rows = new FriendRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new FriendRowNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                Online = src[i].Online
            };
        }
        var payload = NetCodec.EncodeBlockList(new BlockListNetMsg
        {
            BlockAll = list.BlockAll,
            Blocked = rows
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BlockList, payload);
    }

    static FriendListNetMsg ToNetFriendList(Fineage.Protocol.FriendListMsg list)
    {
        var src = list.Friends ?? [];
        var rows = new FriendRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new FriendRowNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                Online = src[i].Online
            };
        }

        return new FriendListNetMsg { Friends = rows };
    }
}
