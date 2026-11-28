using System.Xml.Linq;
using Fineage.Agent.Contracts;
using Xunit;

namespace Fineage.Agent.Contracts.Tests;

public sealed class AssemblyDependencyTests
{
    static readonly string[] GameAssemblies =
    [
        "Fineage.Accounts",
        "Fineage.Game",
        "Fineage.Login",
        "Fineage.MobiusImport",
        "Fineage.Net.Contract",
        "Fineage.Net.Host",
        "Fineage.Protocol",
        "Fineage.Zone",
        "Fineage.LoadTest",
        "Fineage.MobiusDiff",
    ];

    [Fact]
    public void Game_assemblies_do_not_reference_Agent_Contracts()
    {
        string repo = FindRepoRoot();
        var violations = new List<string>();
        foreach (string csproj in Directory.GetFiles(repo, "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(csproj);
            if (!GameAssemblies.Contains(name, StringComparer.Ordinal))
                continue;
            var document = XDocument.Load(csproj);
            foreach (XElement reference in document.Descendants("ProjectReference"))
            {
                string include = reference.Attribute("Include")?.Value ?? "";
                if (include.Contains("Fineage.Agent.Contracts", StringComparison.Ordinal))
                    violations.Add($"{name} references Fineage.Agent.Contracts via {include}");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Contracts_project_has_no_MCP_or_model_package()
    {
        string csproj = Path.Combine(FindRepoRoot(), "src", "Fineage.Agent.Contracts", "Fineage.Agent.Contracts.csproj");
        var document = XDocument.Load(csproj);
        var packages = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        Assert.Empty(packages);

        string text = File.ReadAllText(csproj);
        string[] banned = ["MCP", "OpenAI", "Anthropic", "Azure.AI", "Microsoft.Extensions.AI", "Ollama"];
        foreach (string token in banned)
            Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Fineage.Backend.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find Fineage.Backend.slnx from the test output directory.");
    }
}
