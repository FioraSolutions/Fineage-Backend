using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Exercises the host-side release manager without SSH or a live server: legacy-layout bootstrap,
/// atomic current/previous symlinks, and automatic rollback when the new release never becomes ready.
/// The fake readiness server answers 503 while current points at the new release and 200 for the old
/// release, so the test proves the rollback is verified rather than inferred from the switch.
/// </summary>
public sealed class DeployReleaseManagerTests
{
    [Fact]
    public void Ensure_layout_bootstraps_legacy_directories_and_switch_is_atomic()
    {
        string root = NewRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "login"));
            Directory.CreateDirectory(Path.Combine(root, "zone"));
            File.WriteAllText(Path.Combine(root, "login", "Fineage.Login"), "");
            File.WriteAllText(Path.Combine(root, "zone", "Fineage.Zone"), "");

            Result bootstrap = Run("ensure-layout", "--app-root", root);
            Assert.True(bootstrap.ExitCode == 0, bootstrap.Combined);
            Assert.True(Directory.Exists(Path.Combine(root, "releases")));
            Assert.True(IsSymlink(Path.Combine(root, "current")));
            Assert.True(IsSymlink(Path.Combine(root, "login")));
            Assert.True(IsSymlink(Path.Combine(root, "zone")));
            string bootstrapTarget = ReadLink(Path.Combine(root, "current"));
            Assert.StartsWith("releases/bootstrap-", bootstrapTarget);

            Directory.CreateDirectory(Path.Combine(root, "releases", "next", "login"));
            Directory.CreateDirectory(Path.Combine(root, "releases", "next", "zone"));
            Result switched = Run("switch", "--app-root", root, "--release-id", "next");
            Assert.True(switched.ExitCode == 0, switched.Combined);
            Assert.Equal("releases/next", ReadLink(Path.Combine(root, "current")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_readiness_rolls_back_to_previous_known_good_release()
    {
        string root = NewRoot();
        int port = FreePort();
        using var server = new ReadinessServer(root, port);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "releases", "old", "login"));
            Directory.CreateDirectory(Path.Combine(root, "releases", "old", "zone"));
            WriteFakeScript(Path.Combine(root, "run-login.sh"));
            WriteFakeScript(Path.Combine(root, "run-zone.sh"));
            File.WriteAllText(Path.Combine(root, "fineage.env"),
                $"FINEAGE_LOGIN_HTTP_PORT={port}\nFINEAGE_ZONE_STATUS_PORT={port}\n" +
                "FINEAGE_DATABASE_URL=\"Host=127.0.0.1;Port=5432;Database=test;Username=test;Password=test\"\n");
            Link(root, "current", "releases/old");
            Link(root, "previous", "releases/old");

            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(Path.Combine(staging, "login"));
            Directory.CreateDirectory(Path.Combine(staging, "zone"));
            WriteFakeScript(Path.Combine(staging, "login", "Fineage.Login"));
            WriteFakeScript(Path.Combine(staging, "zone", "Fineage.Zone"));

            Result deploy = await RunAsync(
                new Dictionary<string, string>
                {
                    ["FINEAGE_DEPLOY_READY_TIMEOUT"] = "2",
                    ["FINEAGE_DEPLOY_SMOKE"] = "skip",
                    ["FINEAGE_RESTART"] = "scripts"
                },
                "deploy", "--app-root", root, "--staging", staging, "--release-id", "new", "--restart", "scripts");

            Assert.NotEqual(0, deploy.ExitCode);
            Assert.Contains("readiness", deploy.Combined, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("releases/old", ReadLink(Path.Combine(root, "current")));
            Assert.True(Directory.Exists(Path.Combine(root, "releases", "new")), "failed release must be kept for inspection");
            using JsonDocument status = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "last-deploy.json")));
            Assert.Equal("rolled_back", status.RootElement.GetProperty("result").GetString());
            Assert.Equal("releases/old", status.RootElement.GetProperty("previous").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "fineage-release-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static void WriteFakeScript(string path)
    {
        File.WriteAllText(path, "#!/usr/bin/env bash\nexit 0\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    static void Link(string root, string name, string target) =>
        File.CreateSymbolicLink(Path.Combine(root, name), target);

    static bool IsSymlink(string path) =>
        new FileInfo(path).LinkTarget != null;

    static string ReadLink(string path) =>
        new FileInfo(path).LinkTarget ?? throw new InvalidOperationException($"{path} is not a symlink");

    static Result Run(params string[] args) => RunAsync(new Dictionary<string, string>(), args).GetAwaiter().GetResult();

    static async Task<Result> RunAsync(IReadOnlyDictionary<string, string> env, params string[] args)
    {
        var info = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(Path.Combine(RepoRoot(), "deploy", "release-manager.sh"));
        foreach (string arg in args)
            info.ArgumentList.Add(arg);
        foreach ((string key, string value) in env)
            info.Environment[key] = value;

        using var process = Process.Start(info) ?? throw new InvalidOperationException("bash did not start");
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new Result(process.ExitCode, stdout, stderr);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "scripts", "run-local-net.sh")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    sealed record Result(int ExitCode, string Stdout, string Stderr)
    {
        public string Combined => Stdout + Stderr;
    }

    /// <summary>HTTP readiness double whose answer follows the current release symlink.</summary>
    sealed class ReadinessServer : IDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts = new();
        readonly Task _loop;
        readonly string _root;

        public ReadinessServer(string root, int port)
        {
            _root = root;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        async Task AcceptAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    break;
                }
                _ = Task.Run(() => RespondAsync(client));
            }
        }

        async Task RespondAsync(TcpClient client)
        {
            using var _ = client;
            try
            {
                await using NetworkStream stream = client.GetStream();
                var buffer = new byte[2048];
                int read = await stream.ReadAsync(buffer, _cts.Token);
                string head = Encoding.ASCII.GetString(buffer, 0, read);
                string current = new FileInfo(Path.Combine(_root, "current")).LinkTarget ?? "";
                bool ready = !current.EndsWith("/new", StringComparison.Ordinal);
                string body = ready
                    ? "{\"status\":\"ready\",\"ready\":true,\"checks\":{\"database\":true},\"reasons\":[]}"
                    : "{\"status\":\"not_ready\",\"ready\":false,\"checks\":{\"database\":false},\"reasons\":[\"database: simulated down\"]}";
                string status = ready ? "200 OK" : "503 Service Unavailable";
                byte[] payload = Encoding.UTF8.GetBytes(body);
                string headers =
                    $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _cts.Token);
                await stream.WriteAsync(payload, _cts.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }
}
