namespace Fineage.Game.Mobius;

/// <summary>
/// Resolves the Mobius CT0 Interlude datapack XML tree used for catalog import.
/// </summary>
public static class MobiusXmlRoot
{
    public const string EnvVar = "FINEAGE_MOBIUS_XML_ROOT";

    /// <summary>
    /// Default path after a sparse clone at <c>ref/mobius-ct0-interlude/</c> (see docs/MOBIUS_XML_REF.md).
    /// </summary>
    public const string DefaultRelativeFromRepo = "ref/mobius-ct0-interlude/dist/game/data";

    /// <summary>Legacy layout if the clone root is the datapack <c>data</c> folder itself.</summary>
    public const string LegacyRelativeFromRepo = "ref/mobius-ct0-interlude/data";

    public static string Resolve(string? startDirectory = null)
    {
        if (TryResolve(startDirectory, out var root, out _))
            return root;
        throw new DirectoryNotFoundException(
            $"Mobius XML root not found. Set {EnvVar} or clone the reference under ref/mobius-ct0-interlude/ (see docs/MOBIUS_XML_REF.md).");
    }

    public static bool TryResolve(string? startDirectory, out string root, out string? error)
    {
        root = "";
        error = null;

        string? env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            root = Path.GetFullPath(env.Trim());
            if (!Directory.Exists(root))
            {
                error = $"{EnvVar}={root} does not exist.";
                return false;
            }

            return true;
        }

        startDirectory ??= Directory.GetCurrentDirectory();
        var repo = FindRepoRoot(startDirectory);
        if (repo != null)
        {
            foreach (var relative in new[] { DefaultRelativeFromRepo, LegacyRelativeFromRepo })
            {
                var candidate = Path.Combine(repo, relative);
                if (Directory.Exists(candidate))
                {
                    root = candidate;
                    return true;
                }
            }
        }

        error = "Mobius reference tree is missing. Set " + EnvVar + " or run the sparse clone from docs/MOBIUS_XML_REF.md.";
        return false;
    }

    public static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
