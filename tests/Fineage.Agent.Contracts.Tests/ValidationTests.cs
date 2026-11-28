using System.Text.Json;
using Fineage.Agent.Contracts;
using Xunit;

namespace Fineage.Agent.Contracts.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Observation_refuses_more_than_MaxEntities()
    {
        AgentContractException ex = Assert.Throws<AgentContractException>(() =>
            ContractFixtures.Observation(AgentObservation.MaxEntities + 1));
        Assert.Equal(AgentRefusalCodes.ObservationLimit, ex.Code);
    }

    [Fact]
    public void Observation_accepts_MaxEntities()
    {
        AgentObservation observation = ContractFixtures.Observation(AgentObservation.MaxEntities);
        Assert.Equal(AgentObservation.MaxEntities, observation.Entities.Count);
        Assert.True(AgentContractJson.MeasureUtf8(observation) <= AgentObservation.MaxPayloadBytes);
    }

    [Fact]
    public void Observation_refuses_a_payload_over_MaxPayloadBytes()
    {
        string big = new string('a', AgentObservation.MaxPayloadBytes);
        var details = JsonDocument.Parse($"{{\"note\":\"{big}\"}}").RootElement.Clone();
        AgentContractException ex = Assert.Throws<AgentContractException>(() =>
            ContractFixtures.Observation(details: details));
        Assert.Equal(AgentRefusalCodes.ObservationLimit, ex.Code);
    }

    [Fact]
    public void Credit_amount_and_quantity_refuse_non_integers_and_negatives()
    {
        Assert.Equal(
            AgentRefusalCodes.InvalidContract,
            Assert.Throws<AgentContractException>(() => new CreditAmount(-1)).Code);

        AgentContractException fractional = Assert.Throws<AgentContractException>(() =>
            AgentContractJson.Deserialize<UsageEvent>(
                AgentContractJson.Serialize(ContractFixtures.Usage()).Replace("\"credits\":5", "\"credits\":5.5")));
        Assert.Equal(AgentRefusalCodes.InvalidContract, fractional.Code);

        Assert.Equal(
            AgentRefusalCodes.InvalidContract,
            Assert.Throws<AgentContractException>(() =>
                UsageEvent.Create(
                    new CorrelationId("corr-1"),
                    "look",
                    BillingClass.Metered,
                    PriceUnit.Call,
                    quantity: -1,
                    credits: new CreditAmount(0),
                    ContractFixtures.OccurredAt)).Code);
    }

    [Fact]
    public void Receipt_and_operation_refuse_a_silent_success_shape()
    {
        Assert.Throws<AgentContractException>(() =>
            new AgentActionReceipt(
                AgentContractSchema.Current,
                new CommandId("cmd-1"),
                new CorrelationId("corr-1"),
                new OperationId("op-1"),
                OperationStatus.Succeeded,
                null,
                null));

        Assert.Throws<AgentContractException>(() =>
            AgentActionReceipt.Refused(
                new CommandId("cmd-1"),
                new CorrelationId("corr-1"),
                new OperationId("op-1"),
                "  ",
                null));

        Assert.Throws<AgentContractException>(() =>
            AgentOperation.Create(
                new OperationId("op-1"),
                new CommandId("cmd-1"),
                new CorrelationId("corr-1"),
                new IdempotencyKey("idem-1"),
                "look",
                OperationStatus.Succeeded,
                ContractFixtures.OccurredAt,
                completedAt: null));
    }

    [Fact]
    public void Non_billable_quote_cannot_carry_credits()
    {
        AgentContractException ex = Assert.Throws<AgentContractException>(() =>
            BillingQuote.Create(
                "look",
                BillingClass.None,
                PriceUnit.Call,
                1,
                new CreditAmount(5),
                isBillable: false,
                ContractFixtures.Deadline));
        Assert.Equal(AgentRefusalCodes.InvalidContract, ex.Code);
    }

    [Fact]
    public void Empty_or_forged_identifiers_are_refused()
    {
        Assert.Throws<AgentContractException>(() => new TenantId(" "));
        Assert.Throws<AgentContractException>(() => new CommandId(""));
        Assert.Throws<AgentContractException>(() => new SnapshotVersion(-1));
        AgentContractException tool = Assert.Throws<AgentContractException>(() =>
            ToolDescriptor.Create("LookAround", "desc", BillingClass.None, PriceUnit.Call, false, RiskClass.Read));
        Assert.Equal(AgentRefusalCodes.InvalidContract, tool.Code);
    }

    [Fact]
    public void Action_arguments_must_be_a_json_object()
    {
        Assert.Throws<AgentContractException>(() =>
            AgentActionRequest.Create(
                new CommandId("cmd-1"),
                new CorrelationId("corr-1"),
                new IdempotencyKey("idem-1"),
                ContractFixtures.Principal(),
                "look",
                JsonDocument.Parse("[]").RootElement.Clone(),
                ContractFixtures.Deadline));
    }
}
