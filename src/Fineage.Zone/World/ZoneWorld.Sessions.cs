using System.Collections.Concurrent;
using Fineage.Zone;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly ConcurrentDictionary<int, ZoneSession> _liveSessions = new();

    /// <summary>
    /// The live in-world session for <paramref name="objectId"/>, if any.
    /// The Agent Gate must resolve this instance; it must not construct a throwaway session.
    /// </summary>
    public bool TryGetLiveSession(int objectId, out ZoneSession session) =>
        _liveSessions.TryGetValue(objectId, out session!);

    void RegisterLiveSession(ZoneSession session)
    {
        if (session.ObjectId == 0)
            return;
        _liveSessions[session.ObjectId] = session;
    }

    void UnregisterLiveSession(int objectId) => _liveSessions.TryRemove(objectId, out _);
}
