namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly WorldTickOutbox _tickOutbox = new();
    IWorldEventPublisher? _netBridgeInner;
    OutboxingWorldEventPublisher? _netBridgeOutbox;

    void BeginTickOutbox() => _tickOutbox.Begin();

    void EndTickOutboxRecording() => _tickOutbox.EndRecording();

    void FlushTickOutbox()
    {
        if (_netBridgeInner == null || _tickOutbox.OrderedCount == 0 && _tickOutbox.ReplaceableCount == 0)
        {
            _tickOutbox.Clear();
            return;
        }

        try
        {
            _tickOutbox.Flush(_netBridgeInner);
        }
        finally
        {
            _tickOutbox.Clear();
        }
    }
}