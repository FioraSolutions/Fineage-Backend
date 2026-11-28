using System.Text.Json;
using Fineage.Game.Persistence;

// Usage: Fineage.StateWriteProbe <path>. Rewrites the snapshot at <path> in a tight loop through AtomicJsonFile and
// prints "ready" after the first complete write; the parent kills it with SIGKILL at a random moment (#984).
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: Fineage.StateWriteProbe <path>");
    return 2;
}

var options = new JsonSerializerOptions { WriteIndented = true };
string filler = new('x', 256 * 1024);
for (long i = 1; ; i++)
{
    AtomicJsonFile.Write(args[0], new ProbeSnapshot { SchemaVersion = 1, Value = $"{i}:{filler}:{i}" }, options);
    if (i == 1)
    {
        Console.WriteLine("ready");
        Console.Out.Flush();
    }
}

sealed class ProbeSnapshot
{
    [System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    public string Value { get; set; } = "";
}
