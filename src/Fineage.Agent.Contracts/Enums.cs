using System.Text.Json.Serialization;

namespace Fineage.Agent.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<BillingClass>))]
public enum BillingClass
{
    None = 0,
    Included = 1,
    Metered = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<PriceUnit>))]
public enum PriceUnit
{
    Credit = 1,
    Token = 2,
    Call = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<RiskClass>))]
public enum RiskClass
{
    Read = 1,
    Act = 2,
    Privileged = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationStatus>))]
public enum OperationStatus
{
    Accepted = 1,
    Running = 2,
    Succeeded = 3,
    Refused = 4,
    Failed = 5,
    Expired = 6,
    Cancelled = 7,
    Unknown = 8,
}
