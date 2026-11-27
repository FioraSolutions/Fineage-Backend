using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class AtomicJsonFileTests
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    sealed class Sample : IVersionedStateSnapshot
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        public string Value { get; set; } = "";
    }

    static string TempPath(out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "fineage-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "sample.json");
    }

    static Sample Load(string path) =>
        AtomicJsonFile.Load(path, "sample", 1, JsonOptions, static () => new Sample());

    [Fact]
    public void Write_flushes_schema_version_and_round_trips()
    {
        string path = TempPath(out string directory);
        try
        {
            AtomicJsonFile.Write(path, new Sample { Value = "one" }, JsonOptions);

            string json = File.ReadAllText(path);
            Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
            Assert.Equal("one", Load(path).Value);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Crash_before_replace_keeps_the_old_snapshot_and_never_exposes_a_partial_file()
    {
        string path = TempPath(out string directory);
        try
        {
            AtomicJsonFile.Write(path, new Sample { Value = "old" }, JsonOptions);
            string tempSeen = "";
            AtomicJsonFile.BeforeReplaceForTests = temp =>
            {
                tempSeen = temp;
                // The temp file is complete and parseable before the rename; only then do we inject the crash.
                Assert.Equal("new", JsonSerializer.Deserialize<Sample>(File.ReadAllText(temp), JsonOptions)!.Value);
                throw new IOException("injected crash before atomic replace");
            };

            Assert.Throws<IOException>(() => AtomicJsonFile.Write(path, new Sample { Value = "new" }, JsonOptions));

            Assert.NotEmpty(tempSeen);
            Assert.False(File.Exists(tempSeen));
            Assert.Equal("old", Load(path).Value);
        }
        finally
        {
            AtomicJsonFile.BeforeReplaceForTests = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Crash_after_replace_leaves_the_new_complete_snapshot()
    {
        string path = TempPath(out string directory);
        try
        {
            AtomicJsonFile.Write(path, new Sample { Value = "old" }, JsonOptions);
            AtomicJsonFile.BeforeDirectorySyncForTests = _ =>
                throw new IOException("injected crash after atomic replace");

            Assert.Throws<IOException>(() => AtomicJsonFile.Write(path, new Sample { Value = "new" }, JsonOptions));

            Assert.Equal("new", Load(path).Value);
            Assert.True(File.Exists(path));
        }
        finally
        {
            AtomicJsonFile.BeforeDirectorySyncForTests = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_required_sidecar_names_the_system_path()
    {
        string path = TempPath(out string directory);
        try
        {
            var ex = Assert.Throws<StateFileException>(() =>
                AtomicJsonFile.Load(path, "sample", 1, JsonOptions, static () => new Sample(), requireExisting: true));

            Assert.Equal("sample", ex.System);
            Assert.Equal(path, ex.FilePath);
            Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_sidecar_names_the_system_path_and_failure_reason()
    {
        string path = TempPath(out string directory);
        try
        {
            File.WriteAllText(path, "{ not json");

            var ex = Assert.Throws<StateFileException>(() => Load(path));

            Assert.Equal("sample", ex.System);
            Assert.Equal(path, ex.FilePath);
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
            Assert.Contains("invalid JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unsupported_schema_version_is_rejected()
    {
        string path = TempPath(out string directory);
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":99,\"value\":\"future\"}");

            var ex = Assert.Throws<StateFileException>(() => Load(path));

            Assert.Contains("schemaVersion 99", ex.Message, StringComparison.Ordinal);
            Assert.Contains("sample", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
