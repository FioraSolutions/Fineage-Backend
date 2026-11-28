using System.Diagnostics.Metrics;
using Fineage.Agent.Identity;

namespace Fineage.Agent.Identity.Tests;

/// <summary>Listens to <c>agent_policy_denials_total</c> for the duration of a test.</summary>
sealed class DenialCounter : IDisposable
{
    readonly MeterListener _listener = new();
    readonly object _gate = new();
    readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);

    public DenialCounter()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AgentIdentityTelemetry.MeterName &&
                instrument.Name == AgentIdentityTelemetry.PolicyDenialsInstrument)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(OnLong);
        _listener.Start();
    }

    public long this[string reason]
    {
        get
        {
            lock (_gate)
                return _counts.GetValueOrDefault(reason);
        }
    }

    public long Total
    {
        get
        {
            lock (_gate)
                return _counts.Values.Sum();
        }
    }

    void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        string reason = "unknown";
        foreach (var tag in tags)
        {
            if (tag.Key == AgentIdentityTelemetry.ReasonTag && tag.Value is string text && text.Length > 0)
            {
                reason = text;
                break;
            }
        }

        lock (_gate)
            _counts[reason] = _counts.GetValueOrDefault(reason) + value;
    }

    public void Dispose() => _listener.Dispose();
}
