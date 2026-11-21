using System.Collections.Concurrent;

namespace Fineage.LoadTest.Bot;

/// <summary>Shared registry of live bot object ids for party / PvP targets.</summary>
public sealed class BotPeerDirectory
{
    readonly ConcurrentDictionary<int, int> _objectByBot = new();

    public void Register(int botId, int objectId) => _objectByBot[botId] = objectId;

    public void Unregister(int botId) => _objectByBot.TryRemove(botId, out _);

    public int? PickPeerObjectId(int selfBotId, Random rng)
    {
        var peers = _objectByBot.Where(kv => kv.Key != selfBotId).Select(kv => kv.Value).ToList();
        if (peers.Count == 0)
            return null;
        return peers[rng.Next(peers.Count)];
    }
}