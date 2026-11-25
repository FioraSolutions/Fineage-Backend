using System.Diagnostics;
using System.Text;

namespace Fineage.Zone.World;

/// <summary>
/// Evidence captured the first time a step outlasts the watchdog: where <see cref="ZoneWorld.StepSimulation"/> is,
/// which registered wait each thread is in, and the managed stacks of the process.
/// </summary>
internal static class StepHangDiagnostics
{
    public static string Capture(ZoneWorld world)
    {
        var text = new StringBuilder();
        text.Append("step hang evidence phase=").Append(world.StepPhase);
        text.Append(" work=").Append(world.SimulationTicks.CurrentWork ?? "none");
        text.Append(" tick=").Append(world.SimulationTicks.Tick);
        var bodies = world.SimulationTicks.InFlightRegionBodies;
        text.Append(" in-flight=").Append(bodies.Count == 0 ? "none" : string.Join(", ", bodies));
        text.Append("; ").Append(world.EconomyLockDescription);
        text.Append("; ").Append(world.FanoutQueueDescription);
        text.Append("; ").Append(StepWaitRegistry.Describe());
        text.AppendLine();
        text.Append(CaptureManagedStacks());
        return text.ToString();
    }

    /// <summary>
    /// Managed stacks of every thread, from <c>dotnet-stack report</c> attached to this process.
    /// A missing tool is recorded; the phase and wait lines above still stand.
    /// </summary>
    static string CaptureManagedStacks()
    {
        string? tool = ResolveDotnetStack();
        if (tool == null)
            return "managed stacks: dotnet-stack was not found on PATH";

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = $"report -p {Environment.ProcessId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            if (!process.Start())
                return "managed stacks: dotnet-stack did not start";

            // The child attaches over the diagnostic pipe and walks every thread, including this one.
            // Bound the wait so a stuck attach cannot become a second hang.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return "managed stacks: dotnet-stack did not finish within 20s";
            }

            string output = stdout.GetAwaiter().GetResult();
            string error = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0 && output.Length == 0)
                return "managed stacks: dotnet-stack failed: " + Trim(error);
            return "managed stacks:\n" + Trim(output.Length > 0 ? output : error);
        }
        catch (Exception ex)
        {
            return "managed stacks: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string Trim(string text)
    {
        const int limit = 48_000;
        if (text.Length <= limit)
            return text;
        return text[..limit] + "\n… truncated";
    }

    static string? ResolveDotnetStack()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "dotnet-stack.exe" : "dotnet-stack");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string tools = Path.Combine(home, ".dotnet", "tools", OperatingSystem.IsWindows() ? "dotnet-stack.exe" : "dotnet-stack");
        return File.Exists(tools) ? tools : null;
    }
}
