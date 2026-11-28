using System.Text.Json;
using Fineage.Agent.Contracts;

namespace Fineage.Agent.Contracts.Tests;

public static class ContractFixtures
{
    public static readonly DateTimeOffset Deadline = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset OccurredAt = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

    public static AgentPrincipal Principal(bool bound = true) =>
        AgentPrincipal.Create(
            new TenantId("tenant-1"),
            new PrincipalId("principal-1"),
            new AgentId("agent-1"),
            bound ? new CharacterId("4242") : null,
            Deadline);

    public static JsonElement Object(string json = """{"x":1}""") =>
        JsonDocument.Parse(json).RootElement.Clone();

    public static AgentObservation Observation(int entityCount = 1, JsonElement? details = null)
    {
        var entities = new List<ObservedEntity>(entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            entities.Add(new ObservedEntity(1000 + i, "npc", 1, 2, 3, "Guard", null));
        }

        return AgentObservation.Create(
            new SnapshotVersion(7),
            new CorrelationId("corr-1"),
            new CharacterId("4242"),
            new ObservedSelf(4242, 10, 20, 30, 0),
            entities,
            details,
            AgentCursor.Create(new SnapshotVersion(7), "c7"));
    }

    public static ObservationDelta Delta() =>
        ObservationDelta.Create(
            new SnapshotVersion(6),
            new SnapshotVersion(7),
            new CorrelationId("corr-1"),
            [new ObservedEntity(1001, "npc", 1, 2, 3, "Guard", null)],
            [1000],
            AgentCursor.Create(new SnapshotVersion(7), "c7"));

    public static AgentActionRequest ActionRequest() =>
        AgentActionRequest.Create(
            new CommandId("cmd-1"),
            new CorrelationId("corr-1"),
            new IdempotencyKey("idem-1"),
            Principal(),
            "look",
            Object("""{"radius":200}"""),
            Deadline);

    public static AgentActionReceipt ActionReceipt(bool accepted = true) =>
        accepted
            ? AgentActionReceipt.Accepted(new CommandId("cmd-1"), new CorrelationId("corr-1"), new OperationId("op-1"))
            : AgentActionReceipt.Refused(
                new CommandId("cmd-1"),
                new CorrelationId("corr-1"),
                new OperationId("op-1"),
                AgentRefusalCodes.UnknownTool,
                "look is not published");

    public static AgentOperation Operation(OperationStatus status = OperationStatus.Succeeded) =>
        AgentOperation.Create(
            new OperationId("op-1"),
            new CommandId("cmd-1"),
            new CorrelationId("corr-1"),
            new IdempotencyKey("idem-1"),
            "look",
            status,
            OccurredAt,
            status is OperationStatus.Accepted or OperationStatus.Running ? null : OccurredAt.AddSeconds(1),
            status is OperationStatus.Refused or OperationStatus.Failed ? AgentRefusalCodes.UnknownTool : null,
            status is OperationStatus.Refused or OperationStatus.Failed ? "look is not published" : null);

    public static AgentEvent Event() =>
        AgentEvent.Create("entity_entered", new CorrelationId("corr-1"), new SnapshotVersion(7), OccurredAt, Object());

    public static UsageEvent Usage() =>
        UsageEvent.Create(
            new CorrelationId("corr-1"),
            "look",
            BillingClass.Metered,
            PriceUnit.Call,
            quantity: 1,
            credits: new CreditAmount(5),
            OccurredAt,
            new OperationId("op-1"));

    public static BillingQuote Quote() =>
        BillingQuote.Create(
            "look",
            BillingClass.Metered,
            PriceUnit.Call,
            quantity: 1,
            credits: new CreditAmount(5),
            isBillable: true,
            Deadline);

    public static ToolDescriptor Tool() =>
        ToolDescriptor.Create(
            "look",
            "Project nearby entities.",
            BillingClass.Included,
            PriceUnit.Call,
            isBillable: false,
            RiskClass.Read,
            Object("""{"type":"object"}"""));
}
