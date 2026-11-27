namespace Fineage.Game.RequiredData;

/// <summary>One row of <c>data/required-data-registry.json</c>.</summary>
public sealed record RequiredDataEntry(
    RequiredDataKind Kind,
    string Id,
    string File,
    RequiredDataPolicy Policy,
    string KeyName,
    string Note);
