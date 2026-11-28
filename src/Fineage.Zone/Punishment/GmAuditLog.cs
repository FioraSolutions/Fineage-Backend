using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Fineage.Zone.Time;

namespace Fineage.Zone.Punishment;

/// <summary>
/// One immutable line in <c>gm-audit.jsonl</c>. It records who acted, what they did, the target, the
/// state before and after, when it happened, and which client/server surface it came from.
/// </summary>
public sealed record GmAuditRecord(
    int Version,
    long AtUnixMs,
    string Gm,
    int GmObjectId,
    string Action,
    string Target,
    string Before,
    string After,
    string Source,
    string Detail)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Appends versioned GM/admin actions to <c>gm-audit.jsonl</c> under the state directory.
/// The file is append-only and bounded: once it reaches <c>maxBytes</c> it rotates to
/// <c>.1</c> … <c>.N</c>, discarding only the oldest archive. Writes are serialized per file path,
/// so two world/test instances pointing at the same audit file cannot interleave partial lines.
/// </summary>
public sealed class GmAuditLog
{
    public const long DefaultMaxBytes = 8 * 1024 * 1024;
    public const int DefaultMaxArchives = 2;

    static readonly ConcurrentDictionary<string, object> PathGates = new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    readonly string _path;
    readonly IZoneTime _time;
    readonly string _pathKey;
    readonly long _maxBytes;
    readonly int _maxArchives;
    readonly object _gate;

    public GmAuditLog(
        string path,
        IZoneTime? time = null,
        long maxBytes = DefaultMaxBytes,
        int maxArchives = DefaultMaxArchives)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("GM audit path is required.", nameof(path));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Audit rotation size must be positive.");
        if (maxArchives < 0)
            throw new ArgumentOutOfRangeException(nameof(maxArchives), maxArchives, "Audit archive count cannot be negative.");

        _path = path;
        _time = time ?? SystemZoneTime.Instance;
        _pathKey = Path.GetFullPath(path);
        _maxBytes = maxBytes;
        _maxArchives = maxArchives;
        _gate = PathGates.GetOrAdd(_pathKey, static _ => new object());
    }

    public string PathForTests => _path;

    public static GmAuditLog ForStateDirectory(string stateDirectory, IZoneTime? time = null)
    {
        string dir = string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory;
        return new GmAuditLog(Path.Combine(dir, "gm-audit.jsonl"), time);
    }

    /// <summary>Legacy punishment call shape. New admin surfaces should pass the full record.</summary>
    public void Append(string gmName, int gmObjectId, string action, string target, string detail) =>
        Append(new GmAuditRecord(
            GmAuditRecord.CurrentVersion,
            // Audit timestamps are wall-only telemetry; gameplay never reads them.
            _time.UtcNow.ToUnixTimeMilliseconds(),
            gmName,
            gmObjectId,
            action,
            target,
            "",
            "",
            "punishment",
            detail));

    public void Append(GmAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        byte[] bytes = Encoding.UTF8.GetBytes(line);

        lock (_gate)
        {
            string? dir = Path.GetDirectoryName(_pathKey);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            RotateIfNeeded(bytes.Length);
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }

    void RotateIfNeeded(int incomingBytes)
    {
        long length = File.Exists(_pathKey) ? new FileInfo(_pathKey).Length : 0;
        if (length == 0 || length + incomingBytes <= _maxBytes)
            return;

        if (_maxArchives == 0)
        {
            File.Delete(_pathKey);
            return;
        }

        // Oldest archive is overwritten: total disk use stays bounded by (maxArchives + 1) files.
        for (int archive = _maxArchives; archive >= 1; archive--)
        {
            string source = archive == 1 ? _pathKey : $"{_pathKey}.{archive - 1}";
            string destination = $"{_pathKey}.{archive}";
            if (File.Exists(source))
                File.Move(source, destination, overwrite: true);
        }
    }
}
