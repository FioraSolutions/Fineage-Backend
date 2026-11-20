using System.Reflection;
using Fineage.Game;
using Fineage.Accounts;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Records every <see cref="IWorldEventPublisher"/> call (method name + args) for Net fanout assertions.</summary>
public class NetBridgeRecorder : DispatchProxy
{
    public readonly List<(string Method, object?[] Args)> Calls = [];

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method?.Name ?? "";
        args ??= [];
        // Prepare* returns the deferred half of the matching Fanout*; record that call when it is sent.
        if (method?.ReturnType == typeof(Action))
        {
            string sent = "Fanout" + name["Prepare".Length..];
            object?[] captured = args;
            return (Action)(() => Record(sent, captured));
        }

        Record(name, args);
        return method?.ReturnType == typeof(bool) ? false : null;
    }

    void Record(string method, object?[] args)
    {
        lock (Calls)
            Calls.Add((method, args));
    }

    public List<object?[]> Of(string method)
    {
        lock (Calls)
            return Calls.Where(c => c.Method == method).Select(c => c.Args).ToList();
    }

    internal static (IWorldEventPublisher Bridge, NetBridgeRecorder Recorder) Create()
    {
        var bridge = DispatchProxy.Create<IWorldEventPublisher, NetBridgeRecorder>();
        return (bridge, (NetBridgeRecorder)(object)bridge);
    }
}

/// <summary>Elven Village style world with Interlude starter-kit characters entered through <see cref="ZoneWorld.TryEnterAsync"/>.</summary>
internal sealed class NetTestKit
{
    public const int ElfX = 46045, ElfY = 41251, ElfZ = -3440;
    public MemoryAccountStore Store { get; }
    public GameData Game { get; } = TestCatalogs.Game;
    public ZoneWorld World { get; }
    public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
    int _n;

    /// <summary>
    /// Moves <see cref="Now"/> to the first simulation tick at or after <paramref name="unixMs"/>. Stages run at tick
    /// time, so that is the tick a deadline at <paramref name="unixMs"/> is honoured on.
    /// </summary>
    public void AdvanceToTickCovering(long unixMs)
    {
        long step = ZoneSimulationSettings.StepMilliseconds;
        Now = DateTimeOffset.FromUnixTimeMilliseconds(Math.DivRem(unixMs, step, out long rest) * step + (rest > 0 ? step : 0));
    }

    public NetTestKit(
        Fineage.Zone.Spawns.SpawnCatalog? spawns = null,
        string? map = null,
        Fineage.Zone.Geo.GeoEngine? geo = null,
        Fineage.Zone.Interest.IAoi? aoi = null,
        Random? deathRandom = null,
        Random? combatRandom = null,
        Random? spawnRandom = null,
        Random? dropRandom = null,
        Fineage.Game.DeathRuleCatalog? deathRules = null,
        Fineage.Game.Services.DeathDropRules? deathDrop = null,
        Fineage.Game.Services.ItemWeights? weights = null,
        MemoryAccountStore? store = null,
        Fineage.Zone.Simulation.IRegionPartition? regions = null,
        Fineage.Zone.Simulation.ISimulationObserver? simulationObserver = null,
        ulong? simulationSeed = null,
        Fineage.Zone.Simulation.IRegionExecutor? regionExecutor = null,
        Fineage.Zone.Bosses.BossSchedule? bosses = null)
    {
        Store = store ?? MemoryAccountStore.InMemory();
        World = new ZoneWorld(aoi, game: Game, accounts: Store, clock: () => Now, spawnCatalog: spawns, activeMapId: map, geo: geo,
            deathRules: deathRules, deathDrop: deathDrop, deathRandom: deathRandom, weights: weights,
            combatRandom: combatRandom, spawnRandom: spawnRandom, dropRandom: dropRandom, regions: regions,
            simulationObserver: simulationObserver, simulationSeed: simulationSeed, regionExecutor: regionExecutor,
            bosses: bosses);
    }

    /// <summary>One stored character row, without entering the world. Use <see cref="EnterAsync(Character)"/> to log in.</summary>
    public readonly record struct Character(string Account, int CharacterId, string Name, int ClassId);

    public Character Create(int classId = 25, int race = 1, int level = 1, int x = ElfX, int y = ElfY, int z = ElfZ)
    {
        string account = "net" + (++_n);
        Assert.Equal(AddAccountStatus.Created, Store.TryAddAccount(account, "hash", "salt", 1, null));
        var kit = CharacterInventory.Rows(Game.CreateStarterInventory(classId));
        var status = Store.TryAddCharacter(account, "Hero" + _n, classId: classId, race: race, sex: 0, out var created,
            items: kit, hairStyle: 0, hairColor: 0, face: 0, hasPosition: true, x: x, y: y, z: z,
            level: level, maxHp: 100, hp: 100, maxMp: 80, mp: 80);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        return new Character(created!.Account, created.Id, created.Name, created.ClassId);
    }

    public async Task<(ZoneSession Session, ZoneEntity Entity)> EnterAsync(Character who)
    {
        var session = new ZoneSession();
        session.Bind(who.CharacterId, who.Account, who.Name, who.ClassId);
        Assert.True(await World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(World.TryGetLivePlayer(session, out var entity));
        return (session, entity);
    }

    public async Task<(ZoneSession Session, ZoneEntity Entity)> EnterAsync(
        int classId = 25, int race = 1, int x = ElfX, int y = ElfY, int z = ElfZ, int level = 1) =>
        await EnterAsync(Create(classId, race, level, x, y, z));
}
