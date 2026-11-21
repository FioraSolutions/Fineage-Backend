using Fineage.Protocol;
using Fineage.Zone.Bosses;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly BossActor _bosses;

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

        foreach (var order in _bosses.Schedule.StartupOrders())
            MaterializeBoss(order);
    }

    void SpawnBosses(IReadOnlyList<BossSpawnOrder> due)
    {
        foreach (var order in due)
        {
            if (!MaterializeBoss(order))
                continue;
            if (_byId.TryGetValue(order.ObjectId, out var npc))
                FanoutNewNpc(npc);
        }
    }

    /// <summary>
    /// A scheduled boss died in combat: the boss actor rolls its respawn window from now when it next steps. Deferred
    /// to the barrier inside a region body.
    /// </summary>
    void NoteBossDeath(ZoneEntity npc)
    {
        if (!_bosses.Catalog.TryGet(npc.NpcId, out var definition) || definition.ObjectId != npc.ObjectId)
            return;
        var kill = new BossKill(npc.NpcId, NowUnixMs());
        AtBarrier(() => _bosses.Kill(kill));
    }

    /// <summary>
    /// Admin and test hook: marks the boss dead now, opening its respawn window, and, when the boss
    /// is still in the world, removes it. Do not call it while holding the world gate; it takes that gate itself.
    /// </summary>
    public bool OnBossKilled(int npcId)
    {
        return KillBossLocked(npcId);
    }

    public Task<string?> TryBossStatusAsync(ZoneSession session, int npcId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (npcId < 0)
            return Task.FromResult<string?>("bad_request");

        BossStatusMsg? message;
        string? error = BuildBossStatus(npcId, out message);
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

        string? error = ApplyAdminBoss(request);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    /// <summary>Test hook: run the boss actor's stage now, without the simulation loop.</summary>
    internal void AdvanceBossesForTests()
    {
        using (_affinity.Step())
            AtHookTick(tick =>
            {
                SpawnBosses(_bosses.Step(tick));
                return 0;
            });
    }

    internal void SaveBosses() => _bosses.Schedule.Save();

    internal (int Hp, int Mp) BossVitalsForTests(int npcId)
    {
        using (_affinity.Step())
        {
            var definition = _bosses.Catalog.Get(npcId);
            if (!_byId.TryGetValue(definition.ObjectId, out var entity))
                throw new KeyNotFoundException($"Boss {npcId} is not in the world.");
            return (entity.CurHp, entity.CurMp);
        }
    }

    bool KillBossLocked(int npcId)
    {
        if (!_bosses.Catalog.TryGet(npcId, out var definition))
            return false;

        _bosses.Kill(new BossKill(npcId, NowUnixMs()));
        if (_byId.TryGetValue(definition.ObjectId, out var entity) && entity.Kind == ZoneEntityKind.Npc)
            DespawnNpc(entity);
        return true;
    }

    string? ApplyAdminBoss(AdminBossScheduleMsg request)
    {
        if (request.Action == BossProtocol.ActionMarkDead)
            return KillBossLocked(request.NpcId) ? null : BossProtocol.UnknownBoss;

        if (request.Action != BossProtocol.ActionForceSpawn)
            return "bad_request";

        if (!_bosses.Schedule.TryForceSpawn(request.NpcId, out var order))
            return BossProtocol.UnknownBoss;

        if (MaterializeBoss(order) && _byId.TryGetValue(order.ObjectId, out var npc))
            FanoutNewNpc(npc);
        return null;
    }

    string? BuildBossStatus(int npcId, out BossStatusMsg? message)
    {
        if (npcId == 0)
        {
            var views = _bosses.Schedule.Views();
            var entries = new BossStatusEntryMsg[views.Count];
            for (int i = 0; i < views.Count; i++)
                entries[i] = Entry(views[i]);
            message = new BossStatusMsg { Bosses = entries };
            return null;
        }

        if (!_bosses.Schedule.TryView(npcId, out var view))
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
        if (!_bosses.Schedule.ShouldSpawn(definition) && !order.IgnoreEnabled)
            return false;
        if (_byId.ContainsKey(definition.ObjectId))
            return false;

        if (!NpcMaterializeCatalog.ById.TryGetValue(definition.NpcId, out var template))
        {
            throw new InvalidOperationException(
                $"Boss npcId={definition.NpcId} object={definition.ObjectId} has no npc template in npcs.json");
        }

        var row = SpawnCatalog.FromTemplate(
            definition.ObjectId,
            template,
            definition.X,
            definition.Y,
            definition.Z,
            definition.Heading,
            name: definition.Name,
            maxHp: definition.MaxHp,
            attackable: definition.Attackable,
            runSpeed: definition.RunSpeed,
            walkSpeed: definition.WalkSpeed);
        MaterializeNpc(row, mapId: null, order.CurrentHp, order.CurrentMp);
        return true;
    }
}
