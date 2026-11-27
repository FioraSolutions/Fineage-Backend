using System.Runtime.InteropServices;
using System.Text.Json;

namespace Fineage.Game.Persistence;

/// <summary>A world-state snapshot that carries the on-disk schema version.</summary>
public interface IVersionedStateSnapshot
{
    int SchemaVersion { get; set; }
}

/// <summary>
/// A required world-state sidecar is missing, unreadable, malformed, or written by an unsupported schema.
/// The message always names the owning system and the exact file so an operator can act on it.
/// </summary>
public sealed class StateFileException : IOException
{
    public StateFileException(string system, string filePath, string detail, Exception? inner = null)
        : base($"World state '{system}' at '{filePath}' is unusable: {detail}", inner)
    {
        System = system;
        FilePath = filePath;
    }

    public string System { get; }

    public string FilePath { get; }
}

/// <summary>
/// Explicit operator policy for world-state sidecars.
/// <para>
/// A missing file is a fresh state directory on first boot (the documented default). Set
/// <c>FINEAGE_STATE_REQUIRE_EXISTING=1</c> once the directory has been initialised to fail loudly when a sidecar
/// disappears. A corrupt/unreadable file never falls back to an empty state unless
/// <c>FINEAGE_STATE_ALLOW_FRESH_ON_CORRUPT=1</c> is set explicitly; that override logs the ignored path.
/// </para>
/// </summary>
public static class StateFilePolicy
{
    public const string RequireExistingEnvVar = "FINEAGE_STATE_REQUIRE_EXISTING";
    public const string AllowFreshOnCorruptEnvVar = "FINEAGE_STATE_ALLOW_FRESH_ON_CORRUPT";

    public static bool RequireExisting => IsTruthy(Environment.GetEnvironmentVariable(RequireExistingEnvVar));

    public static bool AllowFreshOnCorrupt => IsTruthy(Environment.GetEnvironmentVariable(AllowFreshOnCorruptEnvVar));

    static bool IsTruthy(string? text) =>
        text is "1" or "true" or "TRUE" or "yes" or "YES" or "on" or "ON";
}

/// <summary>
/// The one durable writer for JSON world state. The temp file lives beside the target, is fsync'ed before the
/// atomic rename, and the directory is fsync'ed after it where the platform supports it.
/// </summary>
public static class AtomicJsonFile
{
    /// <summary>Test seam: runs after the temp file is durably written, before the atomic replacement.</summary>
    internal static Action<string>? BeforeReplaceForTests { get; set; }

    /// <summary>Test seam: runs after the atomic replacement, before the directory fsync.</summary>
    internal static Action<string>? BeforeDirectorySyncForTests { get; set; }

    public static T Load<T>(
        string path,
        string system,
        int expectedSchemaVersion,
        JsonSerializerOptions options,
        Func<T> createFresh,
        bool? requireExisting = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createFresh);

        if (!File.Exists(path))
        {
            if (requireExisting ?? StateFilePolicy.RequireExisting)
                throw new StateFileException(system, path, "required state file is missing");
            return createFresh();
        }

        try
        {
            return LoadCore<T>(path, system, expectedSchemaVersion, options);
        }
        catch (StateFileException ex) when (StateFilePolicy.AllowFreshOnCorrupt)
        {
            Console.Error.WriteLine($"[STATE] {ex.Message} (FINEAGE_STATE_ALLOW_FRESH_ON_CORRUPT=1, starting fresh)");
            return createFresh();
        }
    }

    static T LoadCore<T>(string path, string system, int expectedSchemaVersion, JsonSerializerOptions options)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StateFileException(system, path, "could not read the file: " + ex.Message, ex);
        }

        int schemaVersion;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new StateFileException(system, path, "root JSON value must be an object");
            schemaVersion = ReadSchemaVersion(document.RootElement, system, path);
        }
        catch (JsonException ex)
        {
            throw new StateFileException(system, path, "invalid JSON: " + ex.Message, ex);
        }

        if (schemaVersion != expectedSchemaVersion)
        {
            throw new StateFileException(
                system,
                path,
                $"unsupported schemaVersion {schemaVersion}; this build supports {expectedSchemaVersion}");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, options)
                ?? throw new StateFileException(system, path, "deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new StateFileException(system, path, "does not match the snapshot schema: " + ex.Message, ex);
        }
    }

    static int ReadSchemaVersion(JsonElement root, string system, string path)
    {
        // New files write "schemaVersion"; files from before issue #984 wrote "version".
        if (root.TryGetProperty("schemaVersion", out var current) &&
            current.ValueKind == JsonValueKind.Number &&
            current.TryGetInt32(out int value))
            return value;
        if (root.TryGetProperty("version", out var legacy) &&
            legacy.ValueKind == JsonValueKind.Number &&
            legacy.TryGetInt32(out value))
            return value;
        throw new StateFileException(system, path, "missing schemaVersion");
    }

    public static void Write<T>(string path, T snapshot, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"State file path '{path}' has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            Path.GetFileName(fullPath) + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(tempPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.WriteThrough
            }))
            {
                JsonSerializer.Serialize(stream, snapshot, options);
                stream.Flush(flushToDisk: true);
            }

            BeforeReplaceForTests?.Invoke(tempPath);
            // Same-directory rename: readers see either the old or the new complete file, never a partial one.
            File.Move(tempPath, fullPath, overwrite: true);
            BeforeDirectorySyncForTests?.Invoke(fullPath);
            SyncDirectory(directory);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    static void SyncDirectory(string directory)
    {
        // Windows has no portable directory fsync; NTFS journaling covers the rename cache ordering there.
        if (OperatingSystem.IsWindows())
            return;

        int fd = Native.open(directory, 0);
        if (fd < 0)
            throw new IOException($"Could not open state directory '{directory}' for fsync (errno {Marshal.GetLastWin32Error()}).");
        try
        {
            if (Native.fsync(fd) == 0)
                return;

            int errno = Marshal.GetLastWin32Error();
            // EINVAL/EOPNOTSUPP/ENOTSUP: this filesystem does not support directory fsync.
            if (errno is 22 or 95 or 45 or 102)
                return;
            throw new IOException($"Could not fsync state directory '{directory}' (errno {errno}).");
        }
        finally
        {
            _ = Native.close(fd);
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    static class Native
    {
        const string Libc = "libc";

        [DllImport(Libc, SetLastError = true)]
        internal static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport(Libc, SetLastError = true)]
        internal static extern int fsync(int fd);

        [DllImport(Libc, SetLastError = true)]
        internal static extern int close(int fd);
    }
}
