namespace Fineage.Game.RequiredData;

/// <summary>One row of <c>data/required-data-registry.json</c>.</summary>
public sealed record RequiredDataEntry(
    RequiredDataKind Kind,
    string Id,
    string File,
    RequiredDataPolicy Policy,
    string KeyName,
    string Note,
    IReadOnlyList<RequiredDataKnownAbsent> KnownAbsent)
{
    /// <summary>True when <paramref name="key"/> is a documented original-data hole (see <see cref="KnownAbsent"/>).</summary>
    public bool IsKnownAbsent(string key)
    {
        foreach (var row in KnownAbsent)
        {
            if (string.Equals(row.Key, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

/// <summary>
/// A reference the originals themselves leave dangling (Mobius CT0 points at an id it never defines).
/// Listed by key with the reason so the cross-check can tell it from a new import hole; the action that
/// needs it still refuses with a named error at runtime.
/// </summary>
public sealed record RequiredDataKnownAbsent(string Key, string Reason);
