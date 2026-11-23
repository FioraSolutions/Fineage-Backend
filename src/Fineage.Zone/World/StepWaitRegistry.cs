using System.Collections.Concurrent;

namespace Fineage.Zone.World;

/// <summary>
/// The blocking wait each thread is inside, if it registered one. The step watchdog prints this on the first
/// trip so a stuck step names the monitor, semaphore, or channel it is sitting on.
/// </summary>
internal static class StepWaitRegistry
{
    static readonly ConcurrentDictionary<int, string> Waiting = new();

    public static void Push(string name) =>
        Waiting[Environment.CurrentManagedThreadId] = name;

    public static void Pop(string name)
    {
        int id = Environment.CurrentManagedThreadId;
        if (Waiting.TryGetValue(id, out string? current) && current == name)
            Waiting.TryRemove(id, out _);
    }

    public static string Describe()
    {
        if (Waiting.IsEmpty)
            return "no registered blocking waits";
        return string.Join("; ", Waiting
            .OrderBy(pair => pair.Key)
            .Select(pair => $"thread {pair.Key} waiting on {pair.Value}"));
    }
}
