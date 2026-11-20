using Fineage.Zone.Simulation;

namespace Fineage.Zone.Chat;

/// <summary>
/// The global chat channels as a social actor: who is in the world, by object id and by name, whichever region they
/// are in. Whispers and name lookups resolve here, and the world-wide channels (hero voice, announcements) reach
/// <see cref="Online"/>; ranged channels (say, shout, trade) resolve their audience from the speaker's surroundings,
/// and party, clan and alliance lines from those actors. Players are listed in ascending id order.
/// </summary>
internal sealed class ChatActor() : GlobalActor(ActorName)
{
    const string ActorName = "chat";

    readonly SortedDictionary<int, string> _online = [];
    readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Entered(int objectId, string name)
    {
        RequireActorThread();
        if (_byName.TryGetValue(name, out int other) && other != objectId)
            throw new InvalidOperationException($"player {objectId} enters as '{name}', the name of player {other}");
        Left(objectId);
        _online[objectId] = name;
        _byName[name] = objectId;
    }

    public void Left(int objectId)
    {
        RequireActorThread();
        if (_online.Remove(objectId, out string? name))
            _byName.Remove(name);
    }

    /// <summary>The player in the world with that name (case-insensitive), or 0.</summary>
    public int Find(string name)
    {
        RequireActorThread();
        return _byName.TryGetValue(name, out int objectId) ? objectId : 0;
    }

    /// <summary>The players in the world, ascending (a copy).</summary>
    public List<int> Online
    {
        get
        {
            RequireActorThread();
            return [.. _online.Keys];
        }
    }
}
