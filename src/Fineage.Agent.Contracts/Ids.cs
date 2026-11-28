using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Agent.Contracts;

[JsonConverter(typeof(JsonConverter))]
public readonly record struct TenantId
{
    public const int MaxLength = 64;
    public string Value { get; }

    public TenantId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(TenantId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<TenantId>
    {
        protected override TenantId FromString(string value) => new(value);
        protected override string ToIdString(TenantId value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct PrincipalId
{
    public const int MaxLength = 64;
    public string Value { get; }

    public PrincipalId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(PrincipalId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<PrincipalId>
    {
        protected override PrincipalId FromString(string value) => new(value);
        protected override string ToIdString(PrincipalId value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct AgentId
{
    public const int MaxLength = 64;
    public string Value { get; }

    public AgentId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(AgentId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<AgentId>
    {
        protected override AgentId FromString(string value) => new(value);
        protected override string ToIdString(AgentId value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct CharacterId
{
    public const int MaxLength = 64;
    public string Value { get; }

    public CharacterId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(CharacterId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<CharacterId>
    {
        protected override CharacterId FromString(string value) => new(value);
        protected override string ToIdString(CharacterId value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct CommandId
{
    public const int MaxLength = 128;
    public string Value { get; }

    public CommandId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(CommandId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<CommandId>
    {
        protected override CommandId FromString(string value) => new(value);
        protected override string ToIdString(CommandId value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct CorrelationId
{
    public const int MaxLength = 128;
    public string Value { get; }

    public CorrelationId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(CorrelationId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<CorrelationId>
    {
        protected override CorrelationId FromString(string value) => new(value);
        protected override string ToIdString(CorrelationId value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct IdempotencyKey
{
    public const int MaxLength = 128;
    public string Value { get; }

    public IdempotencyKey(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(IdempotencyKey));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<IdempotencyKey>
    {
        protected override IdempotencyKey FromString(string value) => new(value);
        protected override string ToIdString(IdempotencyKey value) => value.Value;
    }
}

[JsonConverter(typeof(JsonConverter))]
public readonly record struct OperationId
{
    public const int MaxLength = 128;
    public string Value { get; }

    public OperationId(string value) => Value = ContractIds.Normalize(value, MaxLength, nameof(OperationId));
    public override string ToString() => Value;
    public sealed class JsonConverter : StringIdConverter<OperationId>
    {
        protected override OperationId FromString(string value) => new(value);
        protected override string ToIdString(OperationId value) => value.Value;
    }
}

/// <summary>Monotonic observation version. Integer only.</summary>
[JsonConverter(typeof(JsonConverter))]
public readonly record struct SnapshotVersion
{
    public long Value { get; }

    public SnapshotVersion(long value)
    {
        if (value < 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "SnapshotVersion must be >= 0.");
        Value = value;
    }

    public override string ToString() => Value.ToString();

    public sealed class JsonConverter : JsonConverter<SnapshotVersion>
    {
        public override SnapshotVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out long value))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "SnapshotVersion must be an integer.");
            return new SnapshotVersion(value);
        }

        public override void Write(Utf8JsonWriter writer, SnapshotVersion value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }
}

/// <summary>
/// Integer AI credits. Isolated from the auditable economy ledger; never a floating-point amount.
/// </summary>
[JsonConverter(typeof(JsonConverter))]
public readonly record struct CreditAmount
{
    public long Value { get; }

    public CreditAmount(long value)
    {
        if (value < 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "CreditAmount must be >= 0.");
        Value = value;
    }

    public override string ToString() => Value.ToString();

    public sealed class JsonConverter : JsonConverter<CreditAmount>
    {
        public override CreditAmount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out long value))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "CreditAmount must be an integer.");
            return new CreditAmount(value);
        }

        public override void Write(Utf8JsonWriter writer, CreditAmount value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }
}

static class ContractIds
{
    public static string Normalize(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} is required.");
        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{name} exceeds {maxLength} characters.");
        return trimmed;
    }
}

public abstract class StringIdConverter<T> : JsonConverter<T>
{
    protected abstract T FromString(string value);
    protected abstract string ToIdString(T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Identity values must be JSON strings.");
        string? value = reader.GetString();
        if (value is null)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Identity values must be JSON strings.");
        return FromString(value);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToIdString(value));
}
