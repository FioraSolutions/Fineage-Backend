using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Agent.Contracts;

/// <summary>
/// Official JSON encoding for Agent contract documents. This is the contract wire,
/// not Agent Runtime behaviour and not the player <c>PacketEnvelope</c> protocol.
/// </summary>
public static class AgentContractJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };
        return options;
    }

    public static string Serialize<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, Options);
    }

    public static byte[] SerializeToUtf8<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    public static int MeasureUtf8<T>(T value) => SerializeToUtf8(value).Length;

    public static T Deserialize<T>(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Deserialize<T>(Encoding.UTF8.GetBytes(json));
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "A contract document must be a JSON object.");
        if (!root.TryGetProperty("schemaVersion", out JsonElement versionElement) &&
            !root.TryGetProperty("SchemaVersion", out versionElement))
        {
            throw new AgentContractException(
                AgentRefusalCodes.UnsupportedSchema,
                "schemaVersion is required.");
        }

        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
        {
            throw new AgentContractException(
                AgentRefusalCodes.UnsupportedSchema,
                "schemaVersion must be an integer.");
        }

        if (!AgentContractSchema.IsSupported(version))
        {
            throw new AgentContractException(
                AgentRefusalCodes.UnsupportedSchema,
                $"schemaVersion {version} is not supported (current {AgentContractSchema.Current}).");
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(root.GetRawText(), Options);
        }
        catch (AgentContractException)
        {
            throw;
        }
        catch (JsonException ex) when (ex.InnerException is AgentContractException inner)
        {
            throw inner;
        }
        catch (JsonException ex)
        {
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, ex.Message);
        }

        if (value is null)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Contract document deserialized to null.");
        return value;
    }
}
