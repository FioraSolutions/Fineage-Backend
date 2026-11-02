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
    public JsonAccountStore Store { get; } = JsonAccountStore.InMemory();
    public GameData Game { get; } = GameData.LoadDefault();
    public ZoneWorld World { get; }
    public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
    int _n;

    public NetTestKit(Fineage.Zone.Spawns.SpawnCatalog? spawns = null, string? map = null)
    {
        World = new ZoneWorld(game: Game, accounts: Store, clock: () => Now, spawnCatalog: spawns, activeMapId: map);
    }

    public async Task<(ZoneSession Session, ZoneEntity Entity)> EnterAsync(int classId = 25, int race = 1, int x = ElfX, int y = ElfY, int z = ElfZ)
    {
        string account = "net" + (++_n);
        Assert.Equal(AddAccountStatus.Created, Store.TryAddAccount(account, "hash", "salt", 1, null));
        var kit = CharacterInventory.Rows(Game.CreateStarterInventory(classId));
        var status = Store.TryAddCharacter(account, "Hero" + _n, classId: classId, race: race, sex: 0, out var created,
            items: kit, hairStyle: 0, hairColor: 0, face: 0, hasPosition: true, x: x, y: y, z: z,
            level: 1, maxHp: 100, hp: 100, maxMp: 80, mp: 80);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        var session = new ZoneSession(new MemoryStream());
        session.Bind(created!.Id, created.Account, created.Name, created.ClassId);
        Assert.True(await World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(World.TryGetLivePlayer(session, out var entity));
        return (session, entity);
    }
}
