using Fineage.Protocol;
using Fineage.Zone.Bosses;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly BossSchedule _bosses;

    void BootstrapBosses()
    {
        var spawnIds = new HashSet<int>();
        foreach (var map in _spawnCatalog.Maps.Values)
        {
            foreach (var npc in map.Npcs)
                spawnIds.Add(npc.ObjectId);
        }

        foreach (var definition in _bosses.Catalog.Bosses)
        {
            if (spawnIds.Contains(definition.ObjectId))
            {
                throw new InvalidOperationException(
                    $"Boss {definition.NpcId} object id {definition.ObjectId} collides with a spawn row.");
            }
        }

        foreach (var order in _bosses.StartupOrders())
            MaterializeBoss(order);
    }

    void TickBosses()
    {
        foreach (var order in _bosses.CollectDue())
        {
            if (!MaterializeBoss(order))
                continue;
            if (_byId.TryGetValue(order.ObjectId, out var npc))
                FanoutNewNpc(npc);
        }
    }

    /// <summary>
    /// Combat calls this after a boss dies. It records the Mobius respawn window and,
    /// when the boss is still in the world, removes it. Safe to call when combat already despawned the NPC.
    /// Do not call it while holding the world gate; it takes that gate itself.
    /// </summary>
    public bool OnBossKilled(int npcId)
    {
        lock (_gate)
            return KillBossLocked(npcId);
    }

    public Task<string?> TryBossStatusAsync(ZoneSession session, int npcId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (npcId < 0)
            return Task.FromResult<string?>("bad_request");

        BossStatusMsg? message;
        string? error;
        lock (_gate)
            error = BuildBossStatus(npcId, out message);
        if (error != null)
            return Task.FromResult<string?>(error);

        // The schedule is read-only state: Net clients ask with a request opcode and get the rows back here.
        if (message != null)
            NetBridge?.FanoutBossStatus([session.ObjectId], message);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAdminBossAsync(ZoneSession session, AdminBossScheduleMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = ApplyAdminBoss(request);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    /// <summary>Test hook: run the boss respawn tick without the simulation loop.</summary>
    internal void AdvanceBossesForTests()
    {
        lock (_gate)
            TickBosses();
    }

    internal void SaveBosses() => _bosses.Save();

    internal (int Hp, int Mp) BossVitalsForTests(int npcId)
    {
        lock (_gate)
        {
            var definition = _bosses.Catalog.Get(npcId);
            if (!_byId.TryGetValue(definition.ObjectId, out var entity))
                throw new KeyNotFoundException($"Boss {npcId} is not in the world.");
            return (entity.CurHp, entity.CurMp);
        }
    }

    bool KillBossLocked(int npcId)
    {
        if (!_bosses.OnBossKilled(npcId))
            return false;

        if (_bosses.Catalog.TryGet(npcId, out var definition)
            && _byId.TryGetValue(definition.ObjectId, out var entity)
            && entity.Kind == ZoneEntityKind.Npc)
        {
            DespawnNpc(entity);
        }

        return true;
    }

    string? ApplyAdminBoss(AdminBossScheduleMsg request)
    {
        if (request.Action == BossProtocol.ActionMarkDead)
            return KillBossLocked(request.NpcId) ? null : BossProtocol.UnknownBoss;

        if (request.Action != BossProtocol.ActionForceSpawn)
            return "bad_request";

        if (!_bosses.TryForceSpawn(request.NpcId, out var order))
            return BossProtocol.UnknownBoss;

        if (MaterializeBoss(order) && _byId.TryGetValue(order.ObjectId, out var npc))
            FanoutNewNpc(npc);
        return null;
    }

    string? BuildBossStatus(int npcId, out BossStatusMsg? message)
    {
        if (npcId == 0)
        {
            var views = _bosses.Views();
            var entries = new BossStatusEntryMsg[views.Count];
            for (int i = 0; i < views.Count; i++)
                entries[i] = Entry(views[i]);
            message = new BossStatusMsg { Bosses = entries };
            return null;
        }

        if (!_bosses.TryView(npcId, out var view))
        {
            message = null;
            return BossProtocol.UnknownBoss;
        }

        message = new BossStatusMsg { Bosses = [Entry(view)] };
        return null;
    }

    BossStatusEntryMsg Entry(BossView view) => new()
    {
        NpcId = view.NpcId,
        ObjectId = view.ObjectId,
        Kind = (int)view.Kind,
        Status = (int)view.Status,
        RespawnUnixMs = view.RespawnUnixMs,
        CurrentHp = view.CurrentHp,
        MaxHp = view.MaxHp,
        CurrentMp = view.CurrentMp,
        MaxMp = view.MaxMp,
        Enabled = view.Enabled,
        Spawned = _byId.ContainsKey(view.ObjectId)
    };

    bool MaterializeBoss(BossSpawnOrder order)
    {
        if (!_bosses.Catalog.TryGet(order.NpcId, out var definition))
            return false;
        if (!definition.Enabled && !order.IgnoreEnabled)
            return false;
        if (_byId.ContainsKey(definition.ObjectId))
            return false;

        var entity = new ZoneEntity
        {
            ObjectId = definition.ObjectId,
            Kind = ZoneEntityKind.Npc,
            Name = definition.Name,
            Title = definition.Title,
            NpcType = definition.NpcType,
            X = definition.X,
            Y = definition.Y,
            Z = definition.Z,
            Heading = definition.Heading,
            NpcId = definition.NpcId,
            Attackable = definition.Attackable,
            MaxHp = definition.MaxHp,
            CurHp = order.CurrentHp,
            MaxMp = definition.MaxMp,
            CurMp = order.CurrentMp,
            RunSpeed = definition.RunSpeed,
            WalkSpeed = definition.WalkSpeed
        };
        _byId.Add(entity.ObjectId, entity);
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        return true;
    }
}
