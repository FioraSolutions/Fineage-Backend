using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.LoadTest.Consistency;

/// <summary>
/// Opt-in bounds for the consistency frame trace. The trace exists to explain an oracle FAIL, not to replace the
/// oracle: it only records decoded packet summaries for the observer/subject of the first issues, never payloads.
/// </summary>
public sealed class ConsistencyTraceOptions
{
    public bool Enabled { get; set; }

    /// <summary>Maximum issues written. The remaining issues stay aggregate-only.</summary>
    public int MaxIssues { get; set; } = 24;

    /// <summary>Maximum decoded frames kept per issue (lifecycle frames are kept before movement frames).</summary>
    public int MaxFramesPerIssue { get; set; } = 200;

    /// <summary>Hard cap on the JSONL file size.</summary>
    public int MaxBytes { get; set; } = 512 * 1024;

    /// <summary>Seconds before and after the issue anchor that count as the relevant time window.</summary>
    public int WindowSeconds { get; set; } = 8;

    public void Validate()
    {
        if (!Enabled)
            throw new InvalidOperationException("consistency trace options are disabled");
        if (MaxIssues < 1)
            throw new InvalidOperationException($"consistencyTrace.maxIssues must be >= 1, got {MaxIssues}");
        if (MaxFramesPerIssue < 0)
            throw new InvalidOperationException($"consistencyTrace.maxFramesPerIssue must be >= 0, got {MaxFramesPerIssue}");
        if (MaxBytes < 256)
            throw new InvalidOperationException($"consistencyTrace.maxBytes must be >= 256, got {MaxBytes}");
        if (WindowSeconds < 1)
            throw new InvalidOperationException($"consistencyTrace.windowSeconds must be >= 1, got {WindowSeconds}");
    }
}

/// <summary>Bounded per-issue evidence for the consistency report directory.</summary>
public sealed class ConsistencyTrace
{
    public DateTimeOffset GeneratedAt { get; init; }
    public int TotalIssues { get; init; }
    public int TracedIssues { get; init; }
    public int FrameCount { get; init; }
    /// <summary>Exact UTF-8 JSONL size the writer produces for <see cref="Issues"/>.</summary>
    public int Bytes { get; init; }
    /// <summary>True when an issue, frame or byte bound dropped evidence.</summary>
    public bool Truncated { get; init; }
    public int WindowSeconds { get; init; }
    public IReadOnlyList<ConsistencyTraceIssue> Issues { get; init; } = [];
}

public sealed class ConsistencyTraceIssue
{
    public ConsistencyKind Kind { get; init; }
    public int ObserverBotId { get; init; }
    public int? OtherBotId { get; init; }
    public int? SubjectObjectId { get; init; }
    public string Detail { get; init; } = "";
    public DateTimeOffset AnchorAt { get; init; }
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public int FramesAvailable { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<ConsistencyTraceFrame> Frames { get; init; } = [];
}

/// <summary>
/// Decoded summary of one inbound zone frame. Coordinates, ids, generations and timestamps are enough to
/// reconstruct an AOI lifecycle mismatch; the raw payload and account/character secrets are deliberately absent.
/// </summary>
public sealed class ConsistencyTraceFrame
{
    public DateTimeOffset At { get; init; }
    /// <summary><c>observer</c> or <c>subject</c>: which bot's observation log this frame came from.</summary>
    public string Source { get; init; } = "";
    public ushort Opcode { get; init; }
    public string Op { get; init; } = "";
    public int? EntityId { get; init; }
    public uint? Generation { get; init; }
    public string? EntityKind { get; init; }
    public bool? IsSelf { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? Z { get; init; }
    public int? FromX { get; init; }
    public int? FromY { get; init; }
    public int? TargetId { get; init; }
    public int? SkillId { get; init; }
    public int? Damage { get; init; }
    public int? Hp { get; init; }
    public int? MaxHp { get; init; }
    public int PayloadBytes { get; init; }
}

public static class ConsistencyTraceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string SerializeIssue(ConsistencyTraceIssue issue) =>
        JsonSerializer.Serialize(issue, Options);

    public static string ToJsonLines(ConsistencyTrace trace)
    {
        if (trace.Issues.Count == 0)
            return "";
        var sb = new StringBuilder(trace.Bytes);
        foreach (var issue in trace.Issues)
            sb.Append(SerializeIssue(issue)).Append('\n');
        return sb.ToString();
    }

    public static int Utf8Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}
