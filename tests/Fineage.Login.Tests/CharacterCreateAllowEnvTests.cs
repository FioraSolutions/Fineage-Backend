using Fineage.Protocol;
using Xunit;

namespace Fineage.Login.Tests;

[CollectionDefinition("Create allow environment", DisableParallelization = true)]
public sealed class CreateAllowEnvironmentCollection;

/// <summary>
/// Process env is shared, so these cases do not run beside each other.
/// </summary>
[Collection("Create allow environment")]
public sealed class CharacterCreateAllowEnvTests
{
    [Fact]
    public void Legacy_human_only_env_still_accepts_every_playtest_starter()
    {
        WithAllowEnv("0", "0,10", () =>
        {
            Assert.True(CharacterCreateOptions.EnvironmentUsesLegacyHumanOnly());
            var options = CharacterCreateOptions.FromEnvironment();
            Assert.Null(options.AllowedRaces);
            Assert.Null(options.AllowedClasses);

            var service = Service(options);
            Assert.True(service.Register("alice", "secret1").Ok);
            foreach (var (classId, race) in CharacterCreateOptions.PlaytestStarters)
            {
                var created = service.CreateHuman("alice", "E" + classId, classId, race: race);
                Assert.True(created.Ok, $"class {classId} race {race}: {created.Code} {created.Message}");
                Assert.Equal(race, created.Value!.Race);
                Assert.Equal(classId, created.Value.ClassId);
            }

            Assert.Equal(LobbyProtocol.RaceNotSupported, service.CreateHuman("alice", "Mismatch", classId: 44, race: 0).Code);
            Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "Second", classId: 1, race: 0).Code);
        });
    }

    [Fact]
    public void Reversed_legacy_class_list_is_also_ignored()
    {
        WithAllowEnv("0", "10, 0", () =>
        {
            var options = CharacterCreateOptions.FromEnvironment();
            Assert.Null(options.AllowedRaces);
            Assert.Null(options.AllowedClasses);
            var service = Service(options);
            Assert.True(service.Register("alice", "secret1").Ok);
            Assert.True(service.CreateHuman("alice", "Dwarf", classId: 53, race: 4).Ok);
        });
    }

    [Fact]
    public void A_non_legacy_allow_list_still_rejects_other_races()
    {
        WithAllowEnv("1", "18", () =>
        {
            Assert.False(CharacterCreateOptions.EnvironmentUsesLegacyHumanOnly());
            var options = CharacterCreateOptions.FromEnvironment();
            Assert.Equal([1], options.AllowedRaces!.Order());
            Assert.Equal([18], options.AllowedClasses!.Order());

            var service = Service(options);
            Assert.True(service.Register("alice", "secret1").Ok);
            var elf = service.CreateHuman("alice", "ElfF", classId: 18, race: 1);
            Assert.True(elf.Ok, elf.Message);
            Assert.Equal(LobbyProtocol.ClassNotSupported, service.CreateHuman("alice", "Human", classId: 0, race: 0).Code);
            Assert.Equal(LobbyProtocol.ClassNotSupported, service.CreateHuman("alice", "OrcF", classId: 44, race: 3).Code);
        });
    }

    [Fact]
    public void Login_project_copies_the_datapack_create_needs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? root = null;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
            {
                root = dir.FullName;
                break;
            }

            dir = dir.Parent;
        }

        Assert.NotNull(root);
        string csproj = File.ReadAllText(Path.Combine(root!, "src", "Fineage.Login", "Fineage.Login.csproj"));
        foreach (string file in new[]
                 {
                     "items.json",
                     "starter-kits.json",
                     "classes.json",
                     "experience.json",
                     "stat-bonuses.json",
                     "skills.json",
                     "skill-trees.json"
                 })
        {
            Assert.Contains(file, csproj, StringComparison.Ordinal);
        }
    }

    static void WithAllowEnv(string? races, string? classes, Action body)
    {
        string? priorRaces = Environment.GetEnvironmentVariable("FINEAGE_ALLOWED_RACES");
        string? priorClasses = Environment.GetEnvironmentVariable("FINEAGE_ALLOWED_CLASSES");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_ALLOWED_RACES", races);
            Environment.SetEnvironmentVariable("FINEAGE_ALLOWED_CLASSES", classes);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ALLOWED_RACES", priorRaces);
            Environment.SetEnvironmentVariable("FINEAGE_ALLOWED_CLASSES", priorClasses);
        }
    }

    static LoginService Service(CharacterCreateOptions fromEnv) =>
        new(
            JsonAccountStore.InMemory(),
            new HandoffTokens(HandoffTokens.DefaultSecret),
            create: new CharacterCreateOptions
            {
                AllowedRaces = fromEnv.AllowedRaces,
                AllowedClasses = fromEnv.AllowedClasses,
                CharMaxNumber = 0
            });
}
