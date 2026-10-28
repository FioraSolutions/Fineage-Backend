using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Fineage.Login;

public static class FineageLogging
{
    public const string LevelVariable = "FINEAGE_LOG_LEVEL";
    public const string FormatVariable = "FINEAGE_LOG_FORMAT";

    static readonly object Gate = new();
    static readonly Regex SecretAssignment = new(
        @"\b(password|token|secret|sessionkey|session_key)\s*=\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static ILoggerFactory? _factory;

    public static void Write(string category, string level, string eventName, params (string Key, object? Value)[] fields)
    {
        LogLevel logLevel = ParseLevel(level);
        ILogger logger = Factory.CreateLogger(category);
        if (!logger.IsEnabled(logLevel))
            return;
        FineageLogState state = FineageLogState.Create(eventName, fields);
        logger.Log(logLevel, 0, state, null, static (value, _) => value.Text);
    }

    public static string FormatKeyValue(LogLevel level, string category, string eventName, params (string Key, object? Value)[] fields)
    {
        FineageLogState state = FineageLogState.Create(eventName, fields);
        return KeyValueConsoleFormatter.Format(level, category, state);
    }

    public static LogLevel ParseLevel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return LogLevel.Information;
        return text.Trim().ToLowerInvariant() switch
        {
            "trace" or "verbose" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" or "information" => LogLevel.Information,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" or "fatal" => LogLevel.Critical,
            "none" or "off" or "silent" => LogLevel.None,
            _ => LogLevel.Information
        };
    }

    public static string ParseFormat(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) && text.Trim().Equals("json", StringComparison.OrdinalIgnoreCase))
            return "json";
        return "kv";
    }

    public static bool IsSecretKey(string key) => FineageLogState.IsSecret(key);

    internal static string Scrub(string text) => SecretAssignment.Replace(text, "$1=[redacted]");

    static ILoggerFactory Factory
    {
        get
        {
            if (_factory != null)
                return _factory;
            lock (Gate)
            {
                _factory ??= Build(
                    Environment.GetEnvironmentVariable(LevelVariable),
                    Environment.GetEnvironmentVariable(FormatVariable));
                return _factory;
            }
        }
    }

    static ILoggerFactory Build(string? levelText, string? formatText)
    {
        LogLevel level = ParseLevel(levelText);
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(level);
            if (ParseFormat(formatText) == "json")
            {
                builder.AddJsonConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.TimestampFormat = "O";
                    options.UseUtcTimestamp = true;
                    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
                });
            }
            else
            {
                builder.AddConsole(options => options.FormatterName = KeyValueConsoleFormatter.FormatterName);
                builder.AddConsoleFormatter<KeyValueConsoleFormatter, ConsoleFormatterOptions>();
            }
        });
    }
}

public sealed class KeyValueConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "fineage-kv";

    public KeyValueConsoleFormatter(IOptionsMonitor<ConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _ = options.CurrentValue;
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        textWriter.WriteLine(Format(logEntry.LogLevel, logEntry.Category, logEntry.State));
    }

    public static string Format(LogLevel level, string category, object? state)
    {
        var sb = new StringBuilder(160);
        sb.Append("ts=").Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        sb.Append(" level=").Append(level);
        sb.Append(" cat=").Append(category);
        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            foreach (KeyValuePair<string, object?> field in values)
            {
                if (field.Key is "{OriginalFormat}" || FineageLogState.IsSecret(field.Key))
                    continue;
                sb.Append(' ').Append(field.Key).Append('=').Append(Quote(field.Value));
            }
        }

        return sb.ToString();
    }

    static string Quote(object? value)
    {
        string text = value switch
        {
            null => "",
            string s => FineageLogging.Scrub(s),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
        if (text.Length == 0)
            return "\"\"";
        bool plain = true;
        foreach (char ch in text)
        {
            if (ch is <= ' ' or '=' or '"' or '\\')
            {
                plain = false;
                break;
            }
        }

        if (plain)
            return text;
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}

public readonly struct FineageLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    readonly KeyValuePair<string, object?>[] _items;

    FineageLogState(KeyValuePair<string, object?>[] items, string text)
    {
        _items = items;
        Text = text;
    }

    public string Text { get; }

    public static FineageLogState Create(string eventName, (string Key, object? Value)[] fields)
    {
        var items = new List<KeyValuePair<string, object?>>(fields.Length + 2)
        {
            new("event", eventName)
        };
        foreach ((string key, object? value) in fields)
        {
            if (string.IsNullOrEmpty(key) || IsSecret(key))
                continue;
            object? stored = value is string text ? FineageLogging.Scrub(text) : value;
            items.Add(new KeyValuePair<string, object?>(key, stored));
        }

        items.Add(new KeyValuePair<string, object?>("{OriginalFormat}", "event={event}"));
        return new FineageLogState(items.ToArray(), "event=" + eventName);
    }

    public static bool IsSecret(string key) =>
        key.Equals("password", StringComparison.OrdinalIgnoreCase)
        || key.Equals("token", StringComparison.OrdinalIgnoreCase)
        || key.Equals("secret", StringComparison.OrdinalIgnoreCase)
        || key.Equals("sessionkey", StringComparison.OrdinalIgnoreCase)
        || key.Equals("session_key", StringComparison.OrdinalIgnoreCase)
        || key.Equals("handoff", StringComparison.OrdinalIgnoreCase)
        || key.Equals("authorization", StringComparison.OrdinalIgnoreCase);

    public int Count => _items.Length;
    public KeyValuePair<string, object?> this[int index] => _items[index];
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, object?>>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
