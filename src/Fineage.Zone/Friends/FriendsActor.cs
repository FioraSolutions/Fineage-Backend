using Fineage.Accounts;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Friends;

/// <summary>The friend and block lists of a player in the world.</summary>
internal sealed class SocialLists(string name)
{
    public string Name { get; } = name;

    /// <summary>Friend objectId → name snapshot (Mobius friend list).</summary>
    public Dictionary<int, string> Friends { get; } = [];

    /// <summary>Blocked objectId → name snapshot (Mobius ignore list).</summary>
    public Dictionary<int, string> Blocked { get; } = [];

    /// <summary>Mobius block-all / message refusal mode.</summary>
    public bool BlockAll { get; set; }
}

/// <summary>
/// Friends and block lists as a social actor: the lists of the players in the world (loaded on enter, dropped on
/// leave), who is friends with whom among them, and pending friend invites. Every request comes from a command or
/// the session lifecycle; the actor has no scheduled work. Online friends are listed in ascending id order.
/// </summary>
internal sealed class FriendsActor() : GlobalActor(ActorName)
{
    const string ActorName = "friends";

    readonly Dictionary<int, SocialLists> _online = [];
    readonly SortedDictionary<int, int> _invites = [];

    /// <summary>Loads the player's lists from its stored row as it enters the world, replacing any left over.</summary>
    public SocialLists Load(int objectId, string name, CharacterRecord? stored)
    {
        RequireActorThread();
        var lists = new SocialLists(name) { BlockAll = stored?.BlockAll ?? false };
        foreach (var row in stored?.Friends ?? [])
        {
            if (row.ObjectId > 0 && !string.IsNullOrWhiteSpace(row.Name))
                lists.Friends[row.ObjectId] = row.Name.Trim();
        }

        foreach (var row in stored?.Blocked ?? [])
        {
            if (row.ObjectId > 0 && !string.IsNullOrWhiteSpace(row.Name))
                lists.Blocked[row.ObjectId] = row.Name.Trim();
        }

        _online[objectId] = lists;
        return lists;
    }

    /// <summary>The lists of a player in the world; throws for one whose lists were never loaded.</summary>
    public SocialLists Of(int objectId)
    {
        RequireActorThread();
        return _online.TryGetValue(objectId, out var lists)
            ? lists
            : throw new InvalidOperationException($"player {objectId} has no friend lists loaded; it is not in the world");
    }

    public bool TryGet(int objectId, out SocialLists lists)
    {
        RequireActorThread();
        return _online.TryGetValue(objectId, out lists!);
    }

    /// <summary>Mobius <c>BlockList.isBlocked(owner, other)</c>: the owner blocks everyone or blocks the other.</summary>
    public bool Blocks(int ownerObjectId, int otherObjectId)
    {
        RequireActorThread();
        var lists = Of(ownerObjectId);
        return lists.BlockAll || lists.Blocked.ContainsKey(otherObjectId);
    }

    /// <summary>The players in the world who are friends with <paramref name="objectId"/> both ways, ascending.</summary>
    public List<int> MutualOnline(int objectId)
    {
        RequireActorThread();
        var lists = Of(objectId);
        var mutual = new List<int>();
        foreach (int friendId in lists.Friends.Keys.Order())
        {
            if (friendId != objectId && _online.TryGetValue(friendId, out var friend) && friend.Friends.ContainsKey(objectId))
                mutual.Add(friendId);
        }

        return mutual;
    }

    /// <summary>Drops the lists and invites of a player leaving the world.</summary>
    public void Unload(int objectId)
    {
        RequireActorThread();
        _online.Remove(objectId);
        _invites.Remove(objectId);
        foreach (var (inviteeId, inviterId) in _invites.ToList())
        {
            if (inviterId == objectId)
                _invites.Remove(inviteeId);
        }
    }

    public bool IsInvited(int inviteeObjectId)
    {
        RequireActorThread();
        return _invites.ContainsKey(inviteeObjectId);
    }

    public void Invite(int inviteeObjectId, int inviterObjectId)
    {
        RequireActorThread();
        _invites[inviteeObjectId] = inviterObjectId;
    }

    public bool TakeInvite(int inviteeObjectId, out int inviterObjectId)
    {
        RequireActorThread();
        return _invites.Remove(inviteeObjectId, out inviterObjectId);
    }
}
