namespace Fineage.Net.Host;

/// <summary>
/// Reserves dispatcher send-work for live recipient handles before a reliable batch is handed over. No encoding,
/// sends, downstream locks, or waits happen here. Recipients beyond the remaining work are disconnected explicitly;
/// the caller charges exactly the admitted work and the ordinary ordered dispatcher path delivers it.
/// </summary>
public sealed class FanoutReliableAdmission
{
    readonly Func<IReadOnlyList<int>, ushort?, AudienceSnapshot>? _capture;

    public FanoutReliableAdmission(Func<IReadOnlyList<int>, ushort?, AudienceSnapshot>? capture = null) =>
        _capture = capture;

    /// <summary>
    /// True when this admission can resolve concrete connections and therefore shed exactly the recipients that do
    /// not fit. A descriptor-only / recording publisher cannot: its batches are refused at the hard limit instead
    /// of being charged less work than they will deliver.
    /// </summary>
    public bool CanShedRecipients => _capture != null;

    /// <summary>
    /// Admits at most <paramref name="remainingWork"/> live recipients from <paramref name="objectIds"/>, disconnects
    /// the rest, and returns the number of recipients left deliverable. No encoding, send, lock or wait happens here.
    /// </summary>
    public long Admit(IReadOnlyList<int> objectIds, ushort? opcode, long remainingWork, int limit, string reason)
    {
        if (_capture == null || objectIds.Count == 0)
            return 0;

        long room = Math.Max(0, Math.Min(remainingWork, limit));
        var audience = _capture(objectIds, opcode);
        long admitted = 0;
        foreach (var handle in audience.Handles)
        {
            if (!handle.Connection.IsBoundTo(handle))
                continue;
            if (admitted < room)
            {
                admitted++;
                continue;
            }
            handle.Connection.ShedReliableAudience(
                $"fanout_dispatcher_reliable_overflow ({reason}; limit={limit} retained recipient sends; opcode={opcode})");
        }
        return admitted;
    }
}
