using System.Diagnostics.Metrics;
using Fineage.Agent.Contracts;
using Fineage.Agent.Operations;
using Xunit;

namespace Fineage.Agent.Operations.Tests;

public sealed class AgentOperationTelemetryTests : IDisposable
{
    public AgentOperationTelemetryTests() => AgentOperationTelemetry.ResetForTests();

    public void Dispose() => AgentOperationTelemetry.ResetForTests();

    [Fact]
    public void Running_gauge_and_duration_histogram_move_with_the_operation_lifecycle()
    {
        using var listener = new DurationListener();
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        var apply = new OperationHarness.ApplyCounter();
        Assert.Equal(0, AgentOperationTelemetry.RunningCount);

        AgentActionReceipt receipt = registry.Accept(OperationHarness.Request(tool: "drop_item"));
        Assert.Equal(1, AgentOperationTelemetry.RunningCount);

        registry.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(0, AgentOperationTelemetry.RunningCount);
        Assert.True(listener.Count > 0);
        Assert.Contains("drop_item", listener.Operations);
        Assert.True(listener.LastMilliseconds >= 0);
    }
}

sealed class DurationListener : IDisposable
{
    readonly MeterListener _listener = new();

    public int Count { get; private set; }
    public double LastMilliseconds { get; private set; }
    public List<string> Operations { get; } = [];

    public DurationListener()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AgentOperationTelemetry.MeterName &&
                instrument.Name == AgentOperationTelemetry.DurationInstrument)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            Count++;
            LastMilliseconds = measurement;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == AgentOperationTelemetry.OperationTag && tag.Value is string name)
                    Operations.Add(name);
            }
        });
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();
}
