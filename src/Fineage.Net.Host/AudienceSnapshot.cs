namespace Fineage.Net.Host;

/// <summary>
/// Watchers resolved once from the published presence table (issue #867 PR3).
/// The send path uses these handles and does not take the presence lock again.
/// </summary>
public readonly struct AudienceSnapshot
{
    public static AudienceSnapshot Empty { get; } = new([], 0);

    readonly ConnectionHandle[] _handles;

    public AudienceSnapshot(ConnectionHandle[] handles, int missing)
    {
        _handles = handles ?? [];
        Missing = missing;
    }

    public ReadOnlySpan<ConnectionHandle> Handles => _handles;

    public int Count => _handles.Length;

    /// <summary>Watcher object ids that had no live connection handle in the published table.</summary>
    public int Missing { get; }
}
