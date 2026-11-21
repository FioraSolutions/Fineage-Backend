using Fineage.Protocol;
using Fineage.Zone.Shops;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly SpawnCatalog _spawnCatalog;
    readonly string _activeMapId;
    readonly bool _includeEnabledMaps;
    readonly Func<DateTimeOffset> _clock;
    readonly Dictionary<int, SpawnSlotState> _spawnSlots = [];
    /// <summary>Due-ordered respawn index so <see cref="TickSpawns"/> does not scan every slot (B10).</summary>
    readonly SortedSet<(long DueUnixMs, int ObjectId)> _spawnDue = [];

    sealed class SpawnSlotState
    {
        public required SpawnNpcDefinition Definition { get; init; }
        public required string MapId { get; init; }
        public DateTimeOffset? RespawnAt { get; set; }
    }

    void BootstrapSpawns()
    {
        var maps = new List<SpawnMapDefinition>();
        var whitelist = ZoneMapFilter.Maps;
        if (whitelist.Count > 0)
        {
            foreach (string id in whitelist)
            {
                if (!_spawnCatalog.Maps.TryGetValue(id, out var map))
                    throw new InvalidOperationException($"FINEAGE_ZONE_MAPS references unknown spawn map '{id}'.");
                maps.Add(map);
            }
        }
        else if (_includeEnabledMaps)
        {
            foreach (var map in _spawnCatalog.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
            {
                if (!map.Enabled)
                    continue;
                maps.Add(map);
            }
            if (maps.Count == 0)
                throw new InvalidOperationException("No enabled spawn maps in datapack.");
        }
        else
        {
            // Explicit single-map mode (FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0 or tests). Active id defaults to
            // SmokeMapId only when the caller opted out of the full world — never the hosted default.
            var active = _spawnCatalog.GetMap(_activeMapId);
            if (!active.Enabled)
                throw new InvalidOperationException($"Active spawn map '{_activeMapId}' is disabled in datapack.");
            maps.Add(active);
        }

        var bounds = ZoneMapFilter.BoundsList;
        foreach (var map in maps)
        {
            foreach (var def in map.Npcs)
            {
                if (!ZoneMapFilter.Contains(bounds, def.X, def.Y))
                    continue;
                if (_spawnSlots.ContainsKey(def.ObjectId))
                    throw new InvalidOperationException($"Duplicate spawn object id {def.ObjectId}.");
                _spawnSlots.Add(def.ObjectId, new SpawnSlotState { Definition = def, MapId = map.MapId });
                MaterializeSpawn(def, map.MapId);
            }
        }
    }

    void ScheduleRespawn(int objectId)
    {
        if (!_spawnSlots.TryGetValue(objectId, out var slot))
            return;

        int delaySeconds = slot.Definition.RespawnDelaySeconds;
        int jitter = slot.Definition.RespawnRandomSeconds;
        if (jitter > 0)
            delaySeconds += _spawnRandom.For(_regions.RegionOf(0, slot.Definition.X, slot.Definition.Y)).Next(0, jitter + 1);

        ClearSpawnDue(slot);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(NowUnixMs()).AddSeconds(Math.Max(1, delaySeconds));
        slot.RespawnAt = at;
        _spawnDue.Add((at.ToUnixTimeMilliseconds(), objectId));
    }

    void ClearSpawnDue(SpawnSlotState slot)
    {
        if (slot.RespawnAt is not { } at)
            return;
        _spawnDue.Remove((at.ToUnixTimeMilliseconds(), slot.Definition.ObjectId));
        slot.RespawnAt = null;
    }

    void TickSpawns()
    {
        long nowMs = NowUnixMs();
        while (_spawnDue.Count > 0)
        {
            var first = _spawnDue.Min;
            if (first.DueUnixMs > nowMs)
                break;
            _spawnDue.Remove(first);
            if (!_spawnSlots.TryGetValue(first.ObjectId, out var slot))
                continue;
            if (slot.RespawnAt is not { } at || at.ToUnixTimeMilliseconds() != first.DueUnixMs)
                continue;

            if (_byId.ContainsKey(slot.Definition.ObjectId))
            {
                ClearSpawnDue(slot);
                continue;
            }

            ClearSpawnDue(slot);
            MaterializeSpawn(slot.Definition, slot.MapId);
            if (_byId.TryGetValue(slot.Definition.ObjectId, out var npc))
                FanoutNewNpc(npc);
        }
    }

    internal void FanoutNewNpcForTests(ZoneEntity npc)
    {
        using (_affinity.Step())
            FanoutNewNpc(npc);
    }

    void FanoutNewNpc(ZoneEntity npc)
    {
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (!VisibleTo(player.ObjectId, npc.ObjectId))
                continue;
            if (!player.Knownlist.Add(npc.ObjectId))
                continue;
            TrackWatcher(player.ObjectId, npc.ObjectId);
            // Net clients: the respawn was added to the knownlist here, so the next move refresh sees no diff and
            // the mob stayed invisible until the player left and re-entered view ("mobs appear late"). Push the
            // EntitySpawn now, like Mobius Spawn.doSpawn → broadcast NpcInfo to the region.
            NetBridge?.FanoutKnownlistDiff(player.ObjectId, [npc.ObjectId], []);
        }
    }

    /// <summary>Test hook: despawn an NPC (corpse decay path) so its respawn timer starts.</summary>
    internal void DespawnNpcForTests(int objectId)
    {
        using (_affinity.Step())
        {
            if (_byId.TryGetValue(objectId, out var npc))
                DespawnNpc(npc);
        }
    }

    /// <summary>Test hook: advance respawn timers without waiting on the simulation loop.</summary>
    internal void AdvanceSpawnsForTests()
    {
        using (_affinity.Step())
        {
            TickCorpseDecay(NowUnixMs());
            TickSpawns();
        }
    }

    /// <summary>
    /// Mobius puts <c>isSuicideAttack</c> skills only in <c>AISkillScope.SUICIDE</c>, not GENERAL/ATTACK.
    /// Fineage <c>aiSkills</c> still list Death Bomb — strip those ids when suicide scope is present.
    /// </summary>
    static IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? FilterAttackSkills(
        IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? attack,
        IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? suicide)
    {
        if (attack is not { Count: > 0 } || suicide is not { Count: > 0 })
            return attack;
        var ban = new HashSet<int>();
        foreach (var s in suicide)
            ban.Add(s.SkillId);
        var kept = attack.Where(a => !ban.Contains(a.SkillId)).ToList();
        return kept.Count > 0 ? kept : null;
    }
}
