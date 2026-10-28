using Fineage.Game.Mobius;

if (args.Contains("--selftest"))
{
    Environment.ExitCode = MobiusImportSelfTest.Run();
    if (Environment.ExitCode == 0)
        Console.WriteLine("selftest ok");
    return;
}

string? outDir = Arg(args, "--out") ?? "data";
string? rootOverride = Arg(args, "--root");
string xmlRoot;
try
{
    xmlRoot = !string.IsNullOrWhiteSpace(rootOverride)
        ? Path.GetFullPath(rootOverride)
        : MobiusXmlRoot.Resolve();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
    return;
}

if (!Directory.Exists(xmlRoot))
{
    Console.Error.WriteLine("Mobius XML root not found: " + xmlRoot);
    Environment.ExitCode = 1;
    return;
}

var targets = MobiusImportTargets.None;
if (HasFlag(args, "--npcs"))
    targets |= MobiusImportTargets.Npcs;
if (HasFlag(args, "--items"))
    targets |= MobiusImportTargets.Items;
if (HasFlag(args, "--spawns"))
    targets |= MobiusImportTargets.Spawns;
if (HasFlag(args, "--skills"))
    targets |= MobiusImportTargets.Skills;

if (targets == MobiusImportTargets.None)
    targets = MobiusImportTargets.Npcs | MobiusImportTargets.Items;

var npcIds = ParseIds(Arg(args, "--npc-ids")) ?? [20001];
var itemIds = ParseIds(Arg(args, "--item-ids")) ?? [57, 2369];
var spawnPaths = ParseList(Arg(args, "--spawn-files")) ??
                 ["TalkingIsland/TalkingIslandMonsters.xml"];

try
{
    var result = MobiusCatalogImporter.Run(new MobiusImportOptions
    {
        XmlRoot = xmlRoot,
        OutputDirectory = Path.GetFullPath(outDir),
        WriteNpcs = targets.HasFlag(MobiusImportTargets.Npcs),
        WriteItems = targets.HasFlag(MobiusImportTargets.Items),
        WriteSpawns = targets.HasFlag(MobiusImportTargets.Spawns),
        WriteSkills = targets.HasFlag(MobiusImportTargets.Skills),
        NpcIds = npcIds,
        ItemIds = itemIds,
        SpawnRelativePaths = spawnPaths
    });

    foreach (string path in result.WrittenPaths)
        Console.WriteLine("wrote " + path);
}
catch (Exception ex)
{
    Console.Error.WriteLine("import failed: " + ex.Message);
    Environment.ExitCode = 1;
}

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    if (i < 0 || i + 1 >= args.Length)
        return null;
    return args[i + 1];
}

static bool HasFlag(string[] args, string name) => args.Contains(name);

static int[]? ParseIds(string? text)
{
    if (string.IsNullOrWhiteSpace(text))
        return null;
    return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(int.Parse)
        .ToArray();
}

static string[]? ParseList(string? text)
{
    if (string.IsNullOrWhiteSpace(text))
        return null;
    return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
