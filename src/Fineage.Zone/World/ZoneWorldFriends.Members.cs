using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Friends;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryFriendDelete(ZoneSession session, FriendDeleteMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        string name = (request.Name ?? "").Trim();
        if (name.Length == 0)
            return "bad_request";

        int friendId = self.Friends
            .FirstOrDefault(kv => string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase))
            .Key;
        if (friendId == 0)
        {
            // Name may have changed — try live / store lookup.
            var online = FindOnlinePlayerByName(name);
            if (online != null && self.Friends.ContainsKey(online.ObjectId))
                friendId = online.ObjectId;
            else if (_accounts?.FindByCharacterName(name) is { } stored && self.Friends.ContainsKey(stored.Id))
                friendId = stored.Id;
        }
        if (friendId == 0 || !self.Friends.Remove(friendId, out string? friendName))
            return FriendProtocol.NotFriend;

        PersistFriends(self);
        FanoutFriendUpdate(self.ObjectId, added: false, friendId, friendName, online: 0);

        if (_byId.TryGetValue(friendId, out var peer) && peer.Kind == ZoneEntityKind.Player)
        {
            if (peer.Friends.Remove(self.ObjectId))
            {
                PersistFriends(peer);
                if (peer.Session != null && peer.Session.InWorld)
                    FanoutFriendUpdate(peer.ObjectId, added: false, self.ObjectId, self.Name, online: 0);
            }
        }
        else if (_accounts?.FindByCharacterId(friendId) is { } offline)
        {
            var remaining = (offline.Friends ?? [])
                .Where(f => f.ObjectId != self.ObjectId)
                .ToArray();
            _persist.ReplaceFriends(offline.Account, offline.Id, remaining);
        }

        NotifyLine(self.ObjectId, 0, $"{friendName} has been deleted from your Friends List.");
        return null;
    }

    string? TryBlockRequest(ZoneSession session, BlockRequestMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";

        if (!FriendRules.IsKnownBlockType(request.Type))
            return "bad_request";

        switch (request.Type)
        {
            case FriendProtocol.BlockList:
                SendBlockList(self);
                return null;
            case FriendProtocol.BlockAllOn:
                self.BlockAll = true;
                PersistBlocked(self);
                NotifyLine(self.ObjectId, 0, "Message refusal mode.");
                return null;
            case FriendProtocol.BlockAllOff:
                self.BlockAll = false;
                PersistBlocked(self);
                NotifyLine(self.ObjectId, 0, "Message acceptance mode.");
                return null;
            case FriendProtocol.BlockAdd:
            case FriendProtocol.BlockRemove:
                break;
        }

        string name = (request.Name ?? "").Trim();
        int targetId = 0;
        string targetName = name;
        var online = FindOnlinePlayerByName(name);
        if (online != null)
        {
            targetId = online.ObjectId;
            targetName = online.Name;
        }
        else if (_accounts?.FindByCharacterName(name) is { } stored)
        {
            targetId = stored.Id;
            targetName = stored.Name;
        }

        if (request.Type == FriendProtocol.BlockAdd)
        {
            if (FriendRules.BlockAddError(
                    name,
                    self.Name,
                    targetId,
                    alreadyBlocked: self.Blocked.ContainsKey(targetId),
                    blockedCount: self.Blocked.Count) is { } blockError)
                return blockError;
            self.Blocked[targetId] = targetName;
            // Mobius: blocking removes from friends both ways when present.
            if (self.Friends.Remove(targetId))
            {
                PersistFriends(self);
                FanoutFriendUpdate(self.ObjectId, added: false, targetId, targetName, online: 0);
                if (_byId.TryGetValue(targetId, out var peer) && peer.Friends.Remove(self.ObjectId))
                {
                    PersistFriends(peer);
                    if (peer.Session?.InWorld == true)
                        FanoutFriendUpdate(peer.ObjectId, added: false, self.ObjectId, self.Name, online: 0);
                }
            }
            PersistBlocked(self);
            NotifyLine(self.ObjectId, 0, $"You have blocked {targetName}.");
            return null;
        }

        if (name.Length == 0)
            return "bad_request";
        if (!self.Blocked.Remove(targetId))
            return FriendProtocol.Missing;
        PersistBlocked(self);
        NotifyLine(self.ObjectId, 0, $"You have unblocked {targetName}.");
        return null;
    }

    void SendBlockList(ZoneEntity self)
    {
        var rows = self.Blocked
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new FriendRowMsg
            {
                ObjectId = kv.Key,
                Name = kv.Value,
                Online = IsOnlinePlayer(kv.Key) ? 1 : 0
            })
            .ToArray();
        NetBridge?.FanoutBlockList([self.ObjectId], new BlockListMsg
        {
            BlockAll = self.BlockAll ? 1 : 0,
            Blocked = rows
        });
    }

    void PersistFriends(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        var rows = player.Friends
            .OrderBy(kv => kv.Key)
            .Select(kv => new StoredSocial(kv.Key, kv.Value))
            .ToArray();
        _persist.ReplaceFriends(player.Session.Account, player.ObjectId, rows);
    }

    void PersistBlocked(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        var rows = player.Blocked
            .OrderBy(kv => kv.Key)
            .Select(kv => new StoredSocial(kv.Key, kv.Value))
            .ToArray();
        _persist.ReplaceBlocked(player.Session.Account, player.ObjectId, rows, player.BlockAll);
    }

    ZoneEntity? FindOnlinePlayerByName(string name)
    {
        foreach (var player in Players())
        {
            if (player.Session != null
                && player.Session.InWorld
                && string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase))
                return player;
        }
        return null;
    }

    bool IsOnlinePlayer(int objectId) =>
        _byId.TryGetValue(objectId, out var entity)
        && entity.Kind == ZoneEntityKind.Player
        && entity.Session != null
        && entity.Session.InWorld;

    string LiveNameOr(int objectId, string fallback) =>
        _byId.TryGetValue(objectId, out var entity) && entity.Kind == ZoneEntityKind.Player
            ? entity.Name
            : fallback;

    static bool IsBlocked(ZoneEntity owner, ZoneEntity other) =>
        owner.BlockAll || owner.Blocked.ContainsKey(other.ObjectId);

    void FanoutFriendUpdate(int watcherObjectId, bool added, int objectId, string name, bool online) =>
        FanoutFriendUpdate(watcherObjectId, added, objectId, name, online ? 1 : 0);

    void FanoutFriendUpdate(int watcherObjectId, bool added, int objectId, string name, int online)
    {
        NetBridge?.FanoutFriendUpdate([watcherObjectId], new FriendUpdateMsg
        {
            Added = added ? 1 : 0,
            ObjectId = objectId,
            Name = name,
            Online = online
        });
    }
}
