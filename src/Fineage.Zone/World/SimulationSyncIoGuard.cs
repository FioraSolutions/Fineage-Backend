using System.Diagnostics;
using System.Globalization;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Records synchronous account-store and ledger calls made on the simulation thread. A command or enter scope
/// collects one summary line; every on-thread call also lands in <c>fineage.tick.stage.ms</c> as
/// <c>sync_io.&lt;operation&gt;</c>.
/// </summary>
sealed class SimulationSyncIoGuard
{
    readonly WorldAffinity _affinity;
    readonly object _gate = new();
    readonly Dictionary<string, (int Count, double Ms)> _bucket = new(StringComparer.Ordinal);
    int _depth;

    public SimulationSyncIoGuard(WorldAffinity affinity) => _affinity = affinity;

    public IDisposable EnterScope() => new Scope(this);

    public IDisposable CommandScope() => new Scope(this);

    public void Record(string operation, double milliseconds)
    {
        if (!_affinity.OnStep)
            return;
        if (double.IsNaN(milliseconds) || milliseconds < 0)
            milliseconds = 0;
        FineageTelemetry.StageMilliseconds.Record(
            milliseconds,
            new KeyValuePair<string, object?>("stage", "sync_io." + operation));
        if (Volatile.Read(ref _depth) <= 0)
            return;
        lock (_gate)
        {
            var (count, ms) = _bucket.GetValueOrDefault(operation);
            _bucket[operation] = (count + 1, ms + milliseconds);
        }
    }

    /// <summary>One line describing the store calls recorded since the last consume, or null when there were none.</summary>
    public string? ConsumeSummary()
    {
        lock (_gate)
        {
            if (_bucket.Count == 0)
                return null;
            string text = string.Join(", ", _bucket
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"{pair.Key}={pair.Value.Count.ToString(CultureInfo.InvariantCulture)}/{pair.Value.Ms.ToString("F1", CultureInfo.InvariantCulture)}ms"));
            _bucket.Clear();
            return text;
        }
    }

    sealed class Scope : IDisposable
    {
        readonly SimulationSyncIoGuard _owner;

        public Scope(SimulationSyncIoGuard owner)
        {
            _owner = owner;
            Interlocked.Increment(ref owner._depth);
        }

        public void Dispose() => Interlocked.Decrement(ref _owner._depth);
    }
}
