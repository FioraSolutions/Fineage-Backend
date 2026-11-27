using System.Text.Json;
using Fineage.Zone.Time;

namespace Fineage.Zone.Punishment;

/// <summary>Appends GM punishment actions to <c>gm-audit.jsonl</c> under the state directory.</summary>
public sealed class GmAuditLog
{
    readonly string _path;
    readonly IZoneTime _time;
    readonly object _gate = new();

    public GmAuditLog(string path, IZoneTime? time = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("GM audit path is required.", nameof(path));
        _path = path;
        _time = time ?? SystemZoneTime.Instance;
    }

    public static GmAuditLog ForStateDirectory(string stateDirectory, IZoneTime? time = null)
    {
        string dir = string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory;
        return new GmAuditLog(Path.Combine(dir, "gm-audit.jsonl"), time);
    }

    public void Append(string gmName, int gmObjectId, string action, string target, string detail)
    {
        var row = new
        {
            // Audit timestamps are wall-only telemetry; gameplay never reads them.
            atUnixMs = _time.UtcNow.ToUnixTimeMilliseconds(),
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