using System.Text.Json;
using Fineage.Agent.Contracts;
using Xunit;

namespace Fineage.Agent.Contracts.Tests;

public sealed class SerializationRoundTripTests
{
    [Fact]
    public void Every_v1_document_round_trips_through_official_json()
    {
        AssertRoundTrip(ContractFixtures.Principal());
        AssertRoundTrip(ContractFixtures.Observation());
        AssertRoundTrip(ContractFixtures.Delta());
        AssertRoundTrip(ContractFixtures.ActionRequest());
        AssertRoundTrip(ContractFixtures.ActionReceipt());
        AssertRoundTrip(ContractFixtures.ActionReceipt(accepted: false));
        AssertRoundTrip(ContractFixtures.Operation());
        AssertRoundTrip(ContractFixtures.Operation(OperationStatus.Refused));
        AssertRoundTrip(ContractFixtures.Event());
        AssertRoundTrip(ContractFixtures.Usage());
        AssertRoundTrip(ContractFixtures.Quote());
        AssertRoundTrip(ContractFixtures.Tool());
        AssertRoundTrip(AgentCursor.Create(new SnapshotVersion(3), "tok"));
    }

    [Fact]
    public void Published_v1_fixtures_still_deserialize()
    {
        Assert.Equal(1, AgentContractJson.Deserialize<AgentPrincipal>(ReadFixture("agent-principal.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<AgentObservation>(ReadFixture("agent-observation.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<ObservationDelta>(ReadFixture("observation-delta.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<AgentActionRequest>(ReadFixture("agent-action-request.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<AgentActionReceipt>(ReadFixture("agent-action-receipt.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<AgentOperation>(ReadFixture("agent-operation.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<AgentEvent>(ReadFixture("agent-event.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<UsageEvent>(ReadFixture("usage-event.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<BillingQuote>(ReadFixture("billing-quote.v1.json")).SchemaVersion);
        Assert.Equal(1, AgentContractJson.Deserialize<ToolDescriptor>(ReadFixture("tool-descriptor.v1.json")).SchemaVersion);
    }

    [Fact]
    public void Money_fields_are_written_as_json_integers()
    {
        using var usage = JsonDocument.Parse(AgentContractJson.Serialize(ContractFixtures.Usage()));
        Assert.Equal(JsonValueKind.Number, usage.RootElement.GetProperty("credits").ValueKind);
        Assert.True(usage.RootElement.GetProperty("credits").TryGetInt64(out long credits));
        Assert.Equal(5, credits);
        Assert.True(usage.RootElement.GetProperty("quantity").TryGetInt64(out _));

        using var quote = JsonDocument.Parse(AgentContractJson.Serialize(ContractFixtures.Quote()));
        Assert.True(quote.RootElement.GetProperty("credits").TryGetInt64(out long quoted));
        Assert.Equal(5, quoted);
    }

    [Fact]
    public void Frozen_action_request_fixture_stays_valid_without_expected_snapshot()
    {
        AgentActionRequest request = AgentContractJson.Deserialize<AgentActionRequest>(ReadFixture("agent-action-request.v1.json"));
        Assert.Null(request.ExpectedSnapshotVersion);
        AgentActionRequest withExpected = AgentActionRequest.Create(
            request.CommandId,
            request.CorrelationId,
            request.IdempotencyKey,
            request.Principal,
            request.ToolName,
            request.Arguments,
            request.Deadline,
            new SnapshotVersion(7));
        string json = AgentContractJson.Serialize(withExpected);
        Assert.Contains("expectedSnapshotVersion", json, StringComparison.Ordinal);
        Assert.Equal(7, AgentContractJson.Deserialize<AgentActionRequest>(json).ExpectedSnapshotVersion!.Value.Value);
    }

    [Theory]
    [InlineData(OperationStatus.Expired)]
    [InlineData(OperationStatus.Cancelled)]
    [InlineData(OperationStatus.Unknown)]
    public void Additive_operation_statuses_round_trip(OperationStatus status)
    {
        AssertRoundTrip(ContractFixtures.Operation(status));
    }

    static void AssertRoundTrip<T>(T value)

    {
        string json = AgentContractJson.Serialize(value);
        T copy = AgentContractJson.Deserialize<T>(json);
        Assert.Equal(json, AgentContractJson.Serialize(copy));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(AgentContractSchema.Current, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    static string ReadFixture(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }
}
