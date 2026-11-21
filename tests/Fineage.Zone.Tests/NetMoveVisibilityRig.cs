using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Real Net sessions on one loopback zone with a hand-driven clock: the world sim is not started, so movement only
/// advances when a test calls <see cref="AdvanceAsync"/>. No NPCs and no geodata, so every frame on a socket is
/// about the players the test placed. AOI is a plain <see cref="RangeAoi"/> (<see cref="Range"/> to see,
/// +<see cref="RangeAoi.ForgetSlack"/> to forget).
/// </summary>
internal sealed class NetMoveVisibilityRig : IAsyncDisposable
{
    public const int Range = 2000;
    public const int X0 = NetTestKit.ElfX, Y0 = NetTestKit.ElfY, Z0 = NetTestKit.ElfZ;

    readonly List<NetZoneSession> _sessions = [];
    int _n;

    NetMoveVisibilityRig(MemoryAccountStore store, ZoneWorld world, NetZoneHost host, CancellationToken ct)
    {
        Store = store;
        World = world;
        Host = host;
        Ct = ct;
    }

    public long NowMs { get; set; }
    public MemoryAccountStore Store { get; }
    public ZoneWorld World { get; }
    public NetZoneHost Host { get; }
    public CancellationToken Ct { get; }

    public DateTimeOffset Clock() => DateTimeOffset.FromUnixTimeMilliseconds(NowMs);

    /// <param name="spawns">Optional NPC catalog (observer-parity tests put mobs next to the players).</param>
    /// <param name="mapId">Map of <paramref name="spawns"/> to load.</param>
    public static async Task<NetMoveVisibilityRig> StartAsync(CancellationToken ct, SpawnCatalog? spawns = null, string? mapId = null)
    {
        var store = MemoryAccountStore.InMemory();
        NetMoveVisibilityRig? rig = null;
        DateTimeOffset Clock() => rig?.Clock() ?? DateTimeOffset.UtcNow;
        var empty = spawns ?? SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = new(SpawnCatalog.DefaultMapId, true, [])
        });
        var world = new ZoneWorld(new RangeAoi(Range), accounts: store, clock: Clock, spawnCatalog: empty, geo: GeoEngine.Empty,
            activeMapId: mapId ?? SpawnCatalog.DefaultMapId,
            ledgerJournal: new MemoryLedgerJournal());
        var host = await NetZoneHost.StartAsync(world, store, clock: Clock);
        long step = ZoneSimulationSettings.StepMilliseconds;
        // Stages run at tick time; starting on a tick boundary keeps AdvanceAsync's whole-step totals exact.
        rig = new NetMoveVisibilityRig(store, world, host, ct) { NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / step * step };
        return rig;
    }

    /// <summary>A fresh Human Fighter entered at <paramref name="x"/>,<paramref name="y"/>; its enter burst is kept on <see cref="NetZoneSession.EnterFrames"/>.</summary>
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

    /// <summary>Moves the clock in <paramref name="stepMs"/> slices, running one live combat/movement tick per slice.</summary>
    public async Task AdvanceAsync(int totalMs, int stepMs = 100)
    {
        for (int done = 0; done < totalMs; done += stepMs)
        {
            NowMs += Math.Min(stepMs, totalMs - done);
            await World.RunPlayerCombatTickAsync(Ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions)
            await s.DisposeAsync();
        await Host.DisposeAsync();
    }

    /// <summary>The live entity, read as a step: the command pump owns the world while the test runs.</summary>
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

        /// <summary>Next <c>MoveState</c> about <paramref name="mover"/>, skipping everything else.</summary>
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

    /// <summary>The entity a presence frame is about, or null for frames that do not describe one entity.</summary>
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
