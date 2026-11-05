namespace Fineage.Game.Tests;

/// <summary>
/// Access to the Mobius reference tree (<c>ref/mobius-ct0-interlude</c>).
/// </summary>
/// <remarks>
/// <c>ref/</c> is gitignored, so a clean CI checkout has no copy. Tests that compare against verbatim
/// Mobius page files return early when the tree is absent instead of failing — the same convention as
/// <c>GeoEngineTests.Real_talking_island_region_loads_when_present</c>. The assertions still run on any
/// developer machine that has the reference sources.
/// </remarks>
internal static class MobiusRef
{
    /// <summary>Root of the reference tree, or null when the checkout has no <c>ref/</c>.</summary>
    public static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
            dir = dir.Parent;

        if (dir == null)
            return null;

        string root = Path.Combine(dir.FullName, "ref", "mobius-ct0-interlude");
        return Directory.Exists(root) ? root : null;
    }

    /// <summary>Quest script folder by exact name, or null when it is not available.</summary>
    public static string? QuestDir(string folder)
    {
        string? root = Root();
        if (root == null)
            return null;

        string dir = Path.Combine(root, "data", "scripts", "quests", folder);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Quest script folder by numeric quest id (first <c>Q00{id}_*</c> match), or null.</summary>
    public static string? QuestDir(int questId)
    {
        string? root = Root();
        if (root == null)
            return null;

        string scripts = Path.Combine(root, "data", "scripts", "quests");
        if (!Directory.Exists(scripts))
            return null;

        string[] matches = Directory.GetDirectories(scripts, $"Q00{questId}_*");
        return matches.Length > 0 ? matches[0] : null;
    }
}
