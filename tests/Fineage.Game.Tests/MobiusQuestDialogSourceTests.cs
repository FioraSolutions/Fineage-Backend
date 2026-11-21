using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Vendored Mobius quest HTML tree exists for gen scripts in CI without .cache.</summary>
public sealed class MobiusQuestDialogSourceTests
{
    [Fact]
    public void Vendored_tree_has_Q169_offspring_dialogs()
    {
        var root = Path.Combine(RepoRoot(), "data/mobius-quest-dialogs/scripts/quests");
        var folder = Path.Combine(root, "Q00169_OffspringOfNightmares");
        Assert.True(Directory.Exists(folder), $"missing {folder}");
        Assert.True(File.Exists(Path.Combine(folder, "30145-00.htm")));
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "scripts", "run-local-net.sh")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}