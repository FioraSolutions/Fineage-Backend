using System.Collections.Frozen;
using System.Diagnostics;
using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// Startup template and opcode tables are frozen. Lookups must agree with a Dictionary and must not allocate.
/// </summary>
public sealed class StaticTemplateLookupBenchTests(ITestOutputHelper output)
{
    const int LookupPasses = 200;
    const int BuildRepeats = 8;

    [Fact]
    public void Frozen_template_lookups_match_dictionary_and_do_not_allocate()
    {
        var game = GameData.LoadDefault();
        var npcs = NpcCatalog.LoadDefault();
        var itemIds = game.Items.Templates.Select(t => t.Id).ToArray();
        var skillIds = game.Skills.Skills.Select(s => s.SkillId).ToArray();
        var npcIds = npcs.ById.Keys.ToArray();
        var opcodeIds = OpcodeValues();

        Assert.NotEmpty(itemIds);
        Assert.NotEmpty(skillIds);
        Assert.NotEmpty(npcIds);
        Assert.NotEmpty(opcodeIds);

        var itemDict = game.Items.Templates.ToDictionary(t => t.Id);
        var skillDict = game.Skills.Skills.ToDictionary(s => s.SkillId);
        var npcDict = npcs.ById.ToDictionary(p => p.Key, p => p.Value);
        var opcodeDict = new Dictionary<ushort, string>(opcodeIds.Length);
        foreach (ushort id in opcodeIds)
        {
            Assert.True(NetOpcodeTable.TryGetName(id, out var name));
            opcodeDict.Add(id, name);
        }

        var itemFrozen = itemDict.ToFrozenDictionary();
        var skillFrozen = skillDict.ToFrozenDictionary();
        var npcFrozen = npcDict.ToFrozenDictionary();
        var opcodeFrozen = opcodeDict.ToFrozenDictionary();

        Agree(itemIds, itemDict.ContainsKey, id => game.Items.TryGet(id, out _));
        Agree(skillIds, skillDict.ContainsKey, id => game.Skills.Find(id) != null);
        Agree(npcIds, npcDict.ContainsKey, npcs.ById.ContainsKey);
        foreach (ushort id in opcodeIds)
            Assert.Equal(opcodeDict[id], opcodeFrozen[id]);
        Assert.False(game.Items.TryGet(-1, out _));
        Assert.Null(game.Skills.Find(-1));
        Assert.False(npcs.ById.ContainsKey(-1));
        Assert.False(NetOpcodeTable.TryGetName(0x0FFE, out _));

        var itemKeys = Mix(itemIds);
        var skillKeys = Mix(skillIds);
        var npcKeys = Mix(npcIds);
        var opcodeKeys = Mix(opcodeIds.Select(id => (int)id).ToArray());

        var itemBuildDict = MeasureBuild(() => new Dictionary<int, ItemTemplate>(itemDict));
        var itemBuildFrozen = MeasureBuild(() => itemDict.ToFrozenDictionary());
        var skillBuildDict = MeasureBuild(() => new Dictionary<int, SkillDefinition>(skillDict));
        var skillBuildFrozen = MeasureBuild(() => skillDict.ToFrozenDictionary());
        var npcBuildDict = MeasureBuild(() => new Dictionary<int, NpcTemplate>(npcDict));
        var npcBuildFrozen = MeasureBuild(() => npcDict.ToFrozenDictionary());
        var opcodeBuildDict = MeasureBuild(() => new Dictionary<ushort, string>(opcodeDict));
        var opcodeBuildFrozen = MeasureBuild(() => opcodeDict.ToFrozenDictionary());

        var itemLookupDict = MeasureLookup(itemKeys, itemDict.ContainsKey);
        var itemLookupFrozen = MeasureLookup(itemKeys, itemFrozen.ContainsKey);
        var skillLookupDict = MeasureLookup(skillKeys, skillDict.ContainsKey);
        var skillLookupFrozen = MeasureLookup(skillKeys, skillFrozen.ContainsKey);
        var npcLookupDict = MeasureLookup(npcKeys, npcDict.ContainsKey);
        var npcLookupFrozen = MeasureLookup(npcKeys, npcFrozen.ContainsKey);
        var opcodeLookupDict = MeasureLookup(opcodeKeys, id => opcodeDict.ContainsKey((ushort)id));
        var opcodeLookupFrozen = MeasureLookup(opcodeKeys, id => opcodeFrozen.ContainsKey((ushort)id));

        output.WriteLine(
            $"templates debug buildRepeats={BuildRepeats} lookupPasses={LookupPasses} " +
            $"items={itemIds.Length} skills={skillIds.Length} npcs={npcIds.Length} opcodes={opcodeIds.Length}");
        Report("items", itemBuildDict, itemBuildFrozen, itemLookupDict, itemLookupFrozen);
        Report("skills", skillBuildDict, skillBuildFrozen, skillLookupDict, skillLookupFrozen);
        Report("npcs", npcBuildDict, npcBuildFrozen, npcLookupDict, npcLookupFrozen);
        Report("opcodes", opcodeBuildDict, opcodeBuildFrozen, opcodeLookupDict, opcodeLookupFrozen);

        Assert.Equal(0, itemLookupFrozen.Bytes);
        Assert.Equal(0, skillLookupFrozen.Bytes);
        Assert.Equal(0, npcLookupFrozen.Bytes);
        Assert.Equal(0, opcodeLookupFrozen.Bytes);
        Assert.Equal(0, itemLookupDict.Bytes);
        Assert.Equal(0, skillLookupDict.Bytes);
        Assert.Equal(0, npcLookupDict.Bytes);
        Assert.Equal(0, opcodeLookupDict.Bytes);
    }

    void Report(string name, Sample buildDict, Sample buildFrozen, Sample lookupDict, Sample lookupFrozen) =>
        output.WriteLine(
            $"{name}: build dict {buildDict.Bytes} bytes/{buildDict.Ns:F0} ns, " +
            $"build frozen {buildFrozen.Bytes} bytes/{buildFrozen.Ns:F0} ns; " +
            $"lookup dict {lookupDict.Bytes} bytes total/{lookupDict.Ns:F1} ns per lookup, " +
            $"lookup frozen {lookupFrozen.Bytes} bytes total/{lookupFrozen.Ns:F1} ns per lookup");

    static void Agree(int[] ids, Func<int, bool> dictionary, Func<int, bool> frozen)
    {
        foreach (int id in ids)
            Assert.Equal(dictionary(id), frozen(id));
        Assert.Equal(dictionary(-1), frozen(-1));
    }

    static int[] Mix(int[] ids)
    {
        var keys = new int[ids.Length + 3];
        ids.CopyTo(keys, 0);
        keys[^3] = -1;
        keys[^2] = int.MinValue;
        keys[^1] = int.MaxValue;
        var random = new Random(867);
        for (int i = keys.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (keys[i], keys[j]) = (keys[j], keys[i]);
        }

        return keys;
    }

    static ushort[] OpcodeValues()
    {
        var values = new List<ushort>();
        foreach (var field in typeof(NetOpcode).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(ushort))
                values.Add((ushort)field.GetRawConstantValue()!);
        }

        if (values.Count == 0)
            throw new InvalidOperationException("NetOpcode published no opcode constants.");
        return values.ToArray();
    }

    static Sample MeasureBuild(Action build)
    {
        for (int i = 0; i < 2; i++)
            build();
        PinAllocationBaseline();
        var sw = new Stopwatch();
        long start = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < BuildRepeats; i++)
            build();
        sw.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        return new Sample(bytes / BuildRepeats, sw.Elapsed.TotalNanoseconds / BuildRepeats);
    }

    static void PinAllocationBaseline()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    static Sample MeasureLookup(int[] keys, Func<int, bool> lookup)
    {
        for (int i = 0; i < 4; i++)
        {
            for (int k = 0; k < keys.Length; k++)
                lookup(keys[k]);
        }

        PinAllocationBaseline();

        int hits = 0;
        var sw = new Stopwatch();
        long start = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < LookupPasses; i++)
        {
            for (int k = 0; k < keys.Length; k++)
            {
                if (lookup(keys[k]))
                    hits++;
            }
        }

        sw.Stop();
        if (hits < 0)
            throw new InvalidOperationException("lookup benchmark was eliminated");
        return new Sample(
            GC.GetAllocatedBytesForCurrentThread() - start,
            sw.Elapsed.TotalNanoseconds / (LookupPasses * (double)keys.Length));
    }

    readonly record struct Sample(long Bytes, double Ns);
}
