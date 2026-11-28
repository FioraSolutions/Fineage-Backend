namespace Fineage.Agent.Contracts;

/// <summary>Explicit contract refusal. There is no silent fallback.</summary>
public sealed class AgentContractException : Exception
{
    public AgentContractException(string code, string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A refusal code is required.", nameof(code));
        Code = code;
    }

    public string Code { get; }
}
