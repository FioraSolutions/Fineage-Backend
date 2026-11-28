using Fineage.LoadTest.Reporting;
using Xunit;

namespace Fineage.LoadTest.Tests;

/// <summary>
/// A soak appends to a log that holds earlier runs, and a bot dropping its socket is a scenario step, not a server
/// fault. Both cases used to fail a clean run: the scanner read the whole file and treated any line containing
/// "Exception" as a fault, including the zone's own <c>warn: net disconnect … exception=IOException</c>.
/// </summary>
public sealed class LogScannerTests : IDisposable
{
    readonly List<string> _files = [];

    string WriteLog(params string[] lines)
    {
        string path = Path.Combine(Path.GetTempPath(), $"fineage-logscan-{Guid.NewGuid():N}.log");
        File.WriteAllLines(path, lines);
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _files)
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    static List<LogIssue> Scan(string path, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        LogScanner.ScanFiles([path], from, to);

    [Fact]
    public void Warn_level_disconnect_is_a_diagnostic_not_a_fault()
    {
        string path = WriteLog(
            "2026-11-26T08:34:28.5203940+00:00 warn: net disconnect loop=pump connectionId=1 endpoint=zone outboundFrames=0 "
            + "exception=IOException: System.IO.IOException: Unable to write data to the transport connection: Broken pipe.",
            " ---> System.Net.Sockets.SocketException (32): Broken pipe",
            "   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.CreateException(SocketError error, Boolean forAsyncThrow)");

        Assert.Empty(Scan(path));
    }

    [Fact]
    public void Error_level_exception_is_a_fault()
    {
        string path = WriteLog(
            "ts=2026-11-26T09:00:00.0000000+00:00 level=Error cat=Fineage.Zone event=log message=\""
            + "apply failed: System.InvalidOperationException: entity 5 is not in the world\"");

        List<LogIssue> issues = Scan(path);
        LogIssue issue = Assert.Single(issues);
        Assert.Equal("exception", issue.Key);
        Assert.Equal(1, issue.Count);
    }

    [Fact]
    public void Stack_trace_frames_under_one_error_entry_count_once()
    {
        string path = WriteLog(
            "2026-11-26T09:00:00.0000000+00:00 error: apply failed: System.InvalidOperationException: boom",
            "   at Fineage.Zone.World.ZoneWorld.RunStep(Action`1 step) in /src/ZoneWorld.cs:line 1",
            " ---> System.Net.Sockets.SocketException (32): Broken pipe",
            "   at Fineage.Net.Contract.NetFraming.WriteManyAsync(Stream stream)");
        List<LogIssue> issues = Scan(path);
        Assert.Equal(1, Assert.Single(issues).Count);
    }

    [Fact]
    public void Entries_outside_the_run_window_are_ignored()
    {
        string path = WriteLog(
            "ts=2026-11-26T08:00:00.0000000+00:00 level=Error cat=Fineage.Zone event=log message=\"old run: System.Exception: boom\"",
            "ts=2026-11-26T10:00:00.0000000+00:00 level=Information cat=Fineage.Zone event=log message=\"this run is clean\"");

        DateTimeOffset from = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 11, 26, 11, 0, 0, TimeSpan.Zero);

        Assert.Empty(Scan(path, from, to));
        Assert.Single(Scan(path));
    }

    [Fact]
    public void Handler_error_rule_is_a_fault_at_any_level()
    {
        string path = WriteLog(
            "ts=2026-11-26T09:30:00.0000000+00:00 level=Warning cat=Fineage.Zone event=log message=\"handler_error opcode=0x2f\"");

        List<LogIssue> issues = Scan(path);
        Assert.Equal("handler_error", Assert.Single(issues).Key);
    }

    [Fact]
    public void Startup_banner_before_the_first_timestamped_entry_is_not_an_issue()
    {
        string path = WriteLog(
            "[ZONE] world postgres whole world (all configured spawn maps)",
            "ts=2026-11-26T09:30:00.0000000+00:00 level=Information cat=Fineage.Zone event=log message=\"ready\"");

        Assert.Empty(Scan(path));
    }
}
