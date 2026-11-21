using System.Text.Json;

namespace Fineage.Zone.Punishment;

/// <summary>Appends GM punishment actions to <c>gm-audit.jsonl</c> under the state directory.</summary>
public sealed class GmAuditLog
{
    readonly string _path;
    readonly object _gate = new();

    public GmAuditLog(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("GM audit path is required.", nameof(path));
        _path = path;
    }

    public static GmAuditLog ForStateDirectory(string stateDirectory)
    {
        string dir = string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory;
        return new GmAuditLog(Path.Combine(dir, "gm-audit.jsonl"));
    }

    public void Append(string gmName, int gmObjectId, string action, string target, string detail)
    {
        var row = new
        {
            atUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            gm = gmName,
            gmObjectId,
            action,
            target,
            detail
        };
        string line = JsonSerializer.Serialize(row);
        lock (_gate)
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }
}