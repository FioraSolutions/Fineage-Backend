using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Game.Economy;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Loopback Net sessions with a fixed simulation start and stepped clock for the two TCP smoke tests.
/// </summary>
internal sealed class NetMoveVisibilityRig : IAsyncDisposable
{
    public const int Range = 2000;
    public const int X0 = NetTestKit.ElfX, Y0 = NetTestKit.ElfY, Z0 = NetTestKit.ElfZ;

    readonly List<NetZoneSession> _sessions = [];
    readonly ManualSimulationDriver _driver;
    readonly MutableClock _clock;
    int _n;

    NetMoveVisibilityRig(MemoryAccountStore store, ZoneWorld world, NetZoneHost host, ManualSimulationDriver driver, MutableClock clock,
        CancellationToken ct)
    {
        Store = store;
        World = world;
        Host = host;
        _driver = driver;
        _clock = clock;
        Ct = ct;
    }

    public MemoryAccountStore Store { get; }
    public ZoneWorld World { get; }
    public NetZoneHost Host { get; }
    public CancellationToken Ct { get; }

    public long NowMs => _clock.Now.ToUnixTimeMilliseconds();

    public DateTimeOffset Clock() => _clock.Now;

    public static async Task<NetMoveVisibilityRig> StartAsync(CancellationToken ct, SpawnCatalog? spawns = null, string? mapId = null)
    {
        var store = MemoryAccountStore.InMemory();
        var clock = new MutableClock { Now = ManualSimulationDriver.FixedStartUtc };
        NetMoveVisibilityRig? rig = null;
        DateTimeOffset Clock() => rig?.Clock() ?? ManualSimulationDriver.FixedStartUtc;
        var empty = spawns ?? SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = new(SpawnCatalog.DefaultMapId, true, [])
        });
        var world = new ZoneWorld(new RangeAoi(Range), accounts: store, clock: Clock, spawnCatalog: empty, geo: GeoEngine.Empty,
            activeMapId: mapId ?? SpawnCatalog.DefaultMapId,
            ledgerJournal: new MemoryLedgerJournal(),
            allowAmbientBoats: false);
        var host = await NetZoneHost.StartAsync(world, store, clock: Clock);
        var driver = new ManualSimulationDriver(world, clock);
        rig = new NetMoveVisibilityRig(store, world, host, driver, clock, ct);
        return rig;
    }

    public async Task<Player> JoinAsync(int x, int y, int z = Z0)
    {
        int n = ++_n;
        var session = await Host.ConnectAsync();
        _sessions.Add(session);
        await session.HelloAsync(Ct);
        string account = "mv" + n;
        var ticket = session.Ticket(account, "Mover" + n, Clock);
        Assert.True(Store.TrySavePresence(account, ticket.CharacterId, x, y, z, null));
        await session.AuthWithTicketAsync(account, ticket.Token, Ct);
        await session.EnterWorldAsync(Ct);
        Assert.NotEqual(0UL, session.EnteredEntityValue);
        var player = new Player(this, session, session.EnteredEntityValue);
        Assert.Equal(x, player.Entity.X);
        Assert.Equal(y, player.Entity.Y);
        return player;
    }

    public void StepTicks(int count) => _driver.Step(count);

    /// <summary>Advances the simulation by whole scheduler steps (multiples of <see cref="ZoneSimulationSettings.StepMilliseconds"/>).</summary>
    public async Task AdvanceAsync(int totalMs, int stepMs = 200)
    {
        if (totalMs % stepMs != 0)
            throw new ArgumentException($"totalMs must be a multiple of {stepMs}.", nameof(totalMs));
        if (stepMs % ZoneSimulationSettings.StepMilliseconds != 0)
            throw new ArgumentException($"stepMs must be a multiple of {ZoneSimulationSettings.StepMilliseconds}.", nameof(stepMs));
        StepTicks(totalMs / ZoneSimulationSettings.StepMilliseconds);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions)
            await s.DisposeAsync();
        await Host.DisposeAsync();
    }

    public ZoneEntity EntityOf(int objectId)
    {
        var entity = World.RunStepForTest(() => World.TryGetEntityById(objectId, out var found) ? found : null);
        Assert.True(entity != null, $"entity {objectId} not in world");
        return entity;
    }

    internal sealed class Player(NetMoveVisibilityRig rig, NetZoneSession session, ulong entityValue)
    {
        public NetZoneSession Net { get; } = session;
        public ulong EntityValue { get; } = entityValue;
        public int ObjectId => (int)EntityValue;

        public ZoneEntity Entity => rig.EntityOf(ObjectId);

        public Task MoveAsync(int x, int y, int z = Z0) =>
            Net.SendAsync(NetOpcode.MoveInput, NetCodec.EncodeMoveInput(new MoveInputMsg { DestX = x, DestY = y, DestZ = z }), rig.Ct);

        public Task StopAsync() => Net.SendAsync(NetOpcode.StopMove, NetCodec.EncodeStopMove(new StopMoveMsg()), rig.Ct);

        public Task ActionAsync(int actionId) =>
            Net.SendAsync(NetOpcode.RequestActionUse, NetCodec.EncodeActionUse(new ActionUseNetMsg { ActionId = actionId }), rig.Ct);

        public Task ValidateAsync(int x, int y, int z = Z0) =>
            Net.SendAsync(NetOpcode.ValidatePosition,
                NetCodec.EncodeValidatePosition(new ValidatePositionMsg { X = x, Y = y, Z = z }), rig.Ct);

        public Task<List<PacketEnvelope>> DrainAsync(int quietMs = 250) => Net.DrainAsync(rig.Ct, quietMs);

        public async Task<MoveStateMsg> NextMoveOfAsync(Player mover)
        {
            while (true)
            {
                var msg = NetCodec.DecodeMoveState((await Net.ExpectAsync(NetOpcode.MoveState, rig.Ct)).Payload);
                if (msg.EntityValue == mover.EntityValue)
                    return msg;
            }
        }

        public async Task<EntitySpawnMsg> NextSpawnOfAsync(Player who)
        {
            while (true)
            {
                var msg = NetCodec.DecodeEntitySpawn((await Net.ExpectAsync(NetOpcode.EntitySpawn, rig.Ct)).Payload);
                if (msg.EntityValue == who.EntityValue)
                    return msg;
            }
        }

        public async Task NextDespawnOfAsync(Player who)
        {
            while (true)
            {
                var msg = NetCodec.DecodeEntityDespawn((await Net.ExpectAsync(NetOpcode.EntityDespawn, rig.Ct)).Payload);
                if (msg.EntityValue == who.EntityValue)
                    return;
            }
        }

        public async Task<PositionCorrectionMsg> NextCorrectionAsync() =>
            NetCodec.DecodePositionCorrection((await Net.ExpectAsync(NetOpcode.PositionCorrection, rig.Ct)).Payload);
    }

    public static ulong? Subject(PacketEnvelope f) => f.Opcode switch
    {
        NetOpcode.MoveState => NetCodec.DecodeMoveState(f.Payload).EntityValue,
        NetOpcode.EntitySpawn => NetCodec.DecodeEntitySpawn(f.Payload).EntityValue,
        NetOpcode.EntityDespawn => NetCodec.DecodeEntityDespawn(f.Payload).EntityValue,
        NetOpcode.ChangeWaitType => NetCodec.DecodeChangeWaitType(f.Payload).EntityValue,
        NetOpcode.ChangeMoveType => NetCodec.DecodeChangeMoveType(f.Payload).EntityValue,
        NetOpcode.SocialAction => NetCodec.DecodeSocialAction(f.Payload).EntityValue,
        NetOpcode.PositionCorrection => NetCodec.DecodePositionCorrection(f.Payload).EntityValue,
        NetOpcode.TeleportToLocation => (ulong)(uint)NetCodec.DecodeTeleportToLocation(f.Payload).ObjectId,
        _ => null
    };

    public static List<PacketEnvelope> About(IEnumerable<PacketEnvelope> frames, Player who) =>
        frames.Where(f => Subject(f) == who.EntityValue).ToList();
}