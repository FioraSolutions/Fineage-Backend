using System.Security.Cryptography;
using System.Text.Json;

namespace Fineage.Agent.Operations;

/// <summary>
/// Canonical fingerprint of a tool call used to detect idempotency-key payload conflicts.
/// </summary>
public static class PayloadFingerprint
{
    public static string Compute(AgentActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Compute(request.ToolName, request.Arguments, request.ExpectedSnapshotVersion);
    }

    public static string Compute(string toolName, JsonElement arguments, SnapshotVersion? expectedSnapshotVersion)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("toolName", toolName);
            if (expectedSnapshotVersion is { } expected)
                writer.WriteNumber("expectedSnapshotVersion", expected.Value);
            writer.WritePropertyName("arguments");
            WriteCanonical(writer, arguments);
            writer.WriteEndObject();
        }

        byte[] digest = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var names = new List<string>();
                foreach (JsonProperty property in element.EnumerateObject())
                    names.Add(property.Name);
                names.Sort(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    writer.WritePropertyName(name);
                    WriteCanonical(writer, element.GetProperty(name));
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteRawValue(element.GetRawText());
                break;
        }
    }
}
