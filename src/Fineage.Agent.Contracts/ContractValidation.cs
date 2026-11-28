using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fineage.Agent.Contracts;

static class ContractValidation
{
    static readonly Regex ToolNamePattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public const int MaxToolNameLength = 64;
    public const int MaxDescriptionLength = 512;
    public const int MaxEventTypeLength = 64;
    public const int MaxKindLength = 32;
    public const int MaxNameLength = 64;
    public const int MaxRefusalCodeLength = 64;
    public const int MaxDetailLength = 512;
    public const int MaxCursorTokenLength = 256;

    public static void RequireSupportedSchema(int schemaVersion)
    {
        if (!AgentContractSchema.IsSupported(schemaVersion))
        {
            throw new AgentContractException(
                AgentRefusalCodes.UnsupportedSchema,
                $"schemaVersion {schemaVersion} is not supported (current {AgentContractSchema.Current}).");
        }
    }

    public static DateTimeOffset AsUtc(DateTimeOffset value) => value.ToUniversalTime();

    public static DateTimeOffset RequireUtcTimestamp(DateTimeOffset value, string name)
    {
        if (value == default)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} is required.");
        return value.ToUniversalTime();
    }

    public static string RequireToolName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "ToolName is required.");
        string trimmed = name.Trim();
        if (trimmed.Length > MaxToolNameLength || !ToolNamePattern.IsMatch(trimmed))
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "ToolName must be snake_case starting with a letter, at most 64 characters.");
        }

        return trimmed;
    }

    public static string RequireText(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} is required.");
        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} exceeds {maxLength} characters.");
        return trimmed;
    }

    public static string? OptionalText(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} exceeds {maxLength} characters.");
        return trimmed;
    }

    public static JsonElement RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null)
        {
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} is required and must be a JSON object.");
        }

        if (value.ValueKind != JsonValueKind.Object)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} must be a JSON object.");
        return value.Clone();
    }

    public static JsonElement? OptionalObject(JsonElement? value, string name)
    {
        if (value is null)
            return null;
        JsonElement element = value.Value;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} must be a JSON object.");
        return element.Clone();
    }

    public static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T>? items, int maxCount, string name)
    {
        items ??= [];
        if (items.Count > maxCount)
        {
            throw new AgentContractException(
                AgentRefusalCodes.ObservationLimit,
                $"{name} has {items.Count} entries; the limit is {maxCount}.");
        }

        return items.Count == 0 ? [] : items.ToArray();
    }

    public static void RequireRefusalCode(OperationStatus status, string? refusalCode)
    {
        bool needsCode = status is OperationStatus.Refused or OperationStatus.Failed;
        if (needsCode && string.IsNullOrWhiteSpace(refusalCode))
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                $"{status} requires a refusal code.");
        }

        if (!needsCode && !string.IsNullOrWhiteSpace(refusalCode))
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                $"{status} must not carry a refusal code.");
        }
    }

    public static void RequirePayloadLimit<T>(T document, int maxBytes, int entityCount, int maxEntities)
    {
        if (entityCount > maxEntities)
        {
            throw new AgentContractException(
                AgentRefusalCodes.ObservationLimit,
                $"Observation has {entityCount} entities; the limit is {maxEntities}.");
        }

        int bytes = AgentContractJson.MeasureUtf8(document);
        if (bytes > maxBytes)
        {
            throw new AgentContractException(
                AgentRefusalCodes.ObservationLimit,
                $"Observation payload is {bytes} bytes; the limit is {maxBytes}.");
        }
    }
}
