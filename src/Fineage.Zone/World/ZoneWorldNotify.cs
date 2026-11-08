using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Net-only notification helpers. Every "tell this player something" path in the world goes through here, so the
/// V2 <c>Pack</c> plumbing does not come back: the bridge fans the message out to the Net client of each watcher.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>sendPacket(new SystemMessage(id))</c> to one player.</summary>
    void NotifyLine(int objectId, int id, string text = "", params string[] args) =>
        NetBridge?.FanoutSystemMessage([objectId], new SystemMessageMsg
        {
            Id = id,
            Text = text,
            Params = args
        });

    void NotifyLine(IReadOnlyList<int> watchers, int id, string text = "", params string[] args) =>
        NetBridge?.FanoutSystemMessage(watchers, new SystemMessageMsg
        {
            Id = id,
            Text = text,
            Params = args
        });
}
