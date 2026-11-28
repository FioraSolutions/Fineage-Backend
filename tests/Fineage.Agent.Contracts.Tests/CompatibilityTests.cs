using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Agent.Contracts;
using Xunit;

namespace Fineage.Agent.Contracts.Tests;

public sealed class CompatibilityTests
{
    [Fact]
    public void Old_consumer_reads_a_v1_payload_with_unknown_fields()
    {
        string json = WithFutureFields(AgentContractJson.Serialize(ContractFixtures.Principal()));
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
        var old = JsonSerializer.Deserialize<AgentPrincipalV1Consumer>(json, options);
        Assert.NotNull(old);
        Assert.Equal(1, old.SchemaVersion);
        Assert.Equal("tenant-1", old.TenantId);
        Assert.Equal("principal-1", old.PrincipalId);
        Assert.Equal("agent-1", old.AgentId);
        Assert.Equal("4242", old.CharacterId);
    }

    [Fact]
    public void Current_types_ignore_unknown_fields_at_the_same_schema_version()
    {
        string json = WithFutureFields(AgentContractJson.Serialize(ContractFixtures.ActionRequest()));
        AgentActionRequest request = AgentContractJson.Deserialize<AgentActionRequest>(json);
        Assert.Equal("cmd-1", request.CommandId.Value);
        Assert.Equal("look", request.ToolName);
        Assert.Equal(1, request.SchemaVersion);
    }

    [Fact]
    public void Unsupported_schema_version_is_refused()
    {
        string json = """{"schemaVersion":2,"tenantId":"tenant-1","principalId":"p","agentId":"a"}""";
        AgentContractException ex = Assert.Throws<AgentContractException>(() =>
            AgentContractJson.Deserialize<AgentPrincipal>(json));
        Assert.Equal(AgentRefusalCodes.UnsupportedSchema, ex.Code);
    }

    [Fact]
    public void Missing_schema_version_is_refused()
    {
        string json = """{"tenantId":"tenant-1","principalId":"p","agentId":"a"}""";
        AgentContractException ex = Assert.Throws<AgentContractException>(() =>
            AgentContractJson.Deserialize<AgentPrincipal>(json));
        Assert.Equal(AgentRefusalCodes.UnsupportedSchema, ex.Code);
    }

    [Fact]
    public void Zero_schema_version_is_refused()
    {
        string json = """{"schemaVersion":0,"tenantId":"tenant-1","principalId":"p","agentId":"a"}""";
        AgentContractException ex = Assert.Throws<AgentContractException>(() =>
            AgentContractJson.Deserialize<AgentPrincipal>(json));
        Assert.Equal(AgentRefusalCodes.UnsupportedSchema, ex.Code);
    }

    static string WithFutureFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                property.WriteTo(writer);
            writer.WriteString("futureField", "additive");
            writer.WriteNumber("futureCount", 1);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Frozen v1 consumer shape: only the fields published with this card.</summary>
    sealed class AgentPrincipalV1Consumer
    {
        public int SchemaVersion { get; set; }
        public string TenantId { get; set; } = "";
        public string PrincipalId { get; set; } = "";
        public string AgentId { get; set; } = "";
        public string? CharacterId { get; set; }
        public DateTimeOffset? Expiry { get; set; }
    }
}
