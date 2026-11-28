namespace Fineage.Agent.Contracts;

/// <summary>
/// Schema version and compatibility policy for every Agent contract document.
/// </summary>
/// <remarks>
/// Every document carries <c>schemaVersion</c>. The current published version is
/// <see cref="Current"/>. A consumer accepts a payload whose version is in
/// <c>1..Current</c> and <strong>ignores unknown fields</strong>, so an older consumer
/// can read a newer payload that only added properties at the same major version.
/// A missing version, a version below 1, or a version above <see cref="Current"/> is
/// refused with <see cref="AgentRefusalCodes.UnsupportedSchema"/> — never accepted
/// silently. Removing, renaming, or changing the meaning of a field requires bumping
/// <see cref="Current"/>. Additive optional fields do not.
/// </remarks>
public static class AgentContractSchema
{
    public const int Current = 1;

    public static bool IsSupported(int schemaVersion) =>
        schemaVersion >= 1 && schemaVersion <= Current;
}
