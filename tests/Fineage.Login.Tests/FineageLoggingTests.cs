using Fineage.Login;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class FineageLoggingTests
{
    [Fact]
    public void Key_value_line_keeps_session_fields_and_drops_secrets()
    {
        string line = FineageLogging.FormatKeyValue(
            LogLevel.Information,
            "Fineage.Zone",
            "EnterWorld",
            ("session", 7),
            ("account", "hero"),
            ("character", "Fighter"),
            ("packet", 3),
            ("password", "hunter2"),
            ("token", "session-key"),
            ("message", "password=hunter2 token=session-key"));

        Assert.Contains("level=Information", line);
        Assert.Contains("cat=Fineage.Zone", line);
        Assert.Contains("event=EnterWorld", line);
        Assert.Contains("session=7", line);
        Assert.Contains("account=hero", line);
        Assert.Contains("character=Fighter", line);
        Assert.Contains("packet=3", line);
        Assert.Contains("password=[redacted]", line);
        Assert.Contains("token=[redacted]", line);
        Assert.DoesNotContain("hunter2", line);
        Assert.DoesNotContain("session-key", line);
    }

    [Theory]
    [InlineData(null, LogLevel.Information)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("off", LogLevel.None)]
    [InlineData("nope", LogLevel.Information)]
    public void Log_level_parses_env_text(string? text, LogLevel expected) =>
        Assert.Equal(expected, FineageLogging.ParseLevel(text));

    [Theory]
    [InlineData(null, "kv")]
    [InlineData("JSON", "json")]
    [InlineData("kv", "kv")]
    public void Log_format_defaults_to_key_value(string? text, string expected) =>
        Assert.Equal(expected, FineageLogging.ParseFormat(text));
}
