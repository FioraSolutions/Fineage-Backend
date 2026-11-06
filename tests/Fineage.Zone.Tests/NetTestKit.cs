using System.Reflection;
using Fineage.Game;
using Fineage.Login;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Records every <see cref="INetPresenceBridge"/> call (method name + args) for Net fanout assertions.</summary>
public class NetBridgeRecorder : DispatchProxy
{
    public readonly List<(string Method, object?[] Args)> Calls = [];

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        lock (Calls)
            Calls.Add((method?.Name ?? "", args ?? []));
        return method?.ReturnType == typeof(bool) ? false : null;
    }

    public List<object?[]> Of(string method)
    {
        lock (Calls)
            return Calls.Where(c => c.Method == method).Select(c => c.Args).ToList();
    }

    internal static (INetPresenceBridge Bridge, NetBridgeRecorder Recorder) Create()
    {
        var bridge = DispatchProxy.Create<INetPresenceBridge, NetBridgeRecorder>();
        return (bridge, (NetBridgeRecorder)(object)bridge);
    }
}

/// <summary>Elven Village style world with Interlude starter-kit characters entered through <see cref="ZoneWorld.TryEnterAsync"/>.</summary>
internal sealed class NetTestKit
{
    public const int ElfX = 46045, ElfY = 41251, ElfZ = -3440;
    public MemoryAccountStore Store { get; }
    public GameData Game { get; } = GameData.LoadDefault();
    public ZoneWorld World { get; }
    public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
    int _n;

    public NetTestKit(
        Fineage.Zone.Spawns.SpawnCatalog? spawns = null,
        string? map = null,
        Fineage.Zone.Geo.GeoEngine? geo = null,
        Fineage.Zone.Interest.IAoi? aoi = null,
        Random? deathRandom = null,
        Fineage.Game.DeathRuleCatalog? deathRules = null,
        Fineage.Game.Services.DeathDropRules? deathDrop = null,
        Fineage.Game.Services.ItemWeights? weights = null,
        MemoryAccountStore? store = null)
    {
        Store = store ?? MemoryAccountStore.InMemory();
        World = new ZoneWorld(aoi, game: Game, accounts: Store, clock: () => Now, spawnCatalog: spawns, activeMapId: map, geo: geo,
            deathRules: deathRules, deathDrop: deathDrop, deathRandom: deathRandom, weights: weights);
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
