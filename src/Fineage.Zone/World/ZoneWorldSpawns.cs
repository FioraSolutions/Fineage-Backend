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
        else
        {
            var active = _spawnCatalog.GetMap(_activeMapId);
            if (!active.Enabled)
                throw new InvalidOperationException($"Active spawn map '{_activeMapId}' is disabled in datapack.");
            maps.Add(active);
            if (_includeEnabledMaps)
            {
                foreach (var map in _spawnCatalog.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
                {
                    if (!map.Enabled || map.MapId == _activeMapId)
                        continue;
                    maps.Add(map);
                }
            }
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

    void MaterializeSpawn(SpawnNpcDefinition def, string mapId)
    {
        if (_byId.ContainsKey(def.ObjectId))
            return;

        var tax = MerchantTowns.For(mapId);
        var entity = new ZoneEntity
        {
            ObjectId = def.ObjectId,
            Kind = ZoneEntityKind.Npc,
            Name = def.Name,
            Title = def.Title,
            NpcType = def.NpcType,
            X = def.X,
            Y = def.Y,
            Z = def.Z,
            Heading = def.Heading,
            NpcId = def.NpcId,
            Attackable = def.Attackable,
            MaxHp = def.MaxHp,
            CurHp = def.MaxHp,
            Level = def.Level,
            RewardExp = def.Exp,
            RewardSp = def.Sp,
            RunSpeed = def.RunSpeed,
            WalkSpeed = def.WalkSpeed,
            MerchantCastleId = tax.CastleId,
            MerchantBaseTaxPercent = tax.BaseTaxPercent
        };
        _byId.Add(entity.ObjectId, entity);
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
    }

    void ScheduleRespawn(int objectId)
    {
        if (!_spawnSlots.TryGetValue(objectId, out var slot))
            return;

        int delaySeconds = slot.Definition.RespawnDelaySeconds;
        int jitter = slot.Definition.RespawnRandomSeconds;
        if (jitter > 0)
            delaySeconds += _spawnRandom.Next(0, jitter + 1);

        slot.RespawnAt = _clock().AddSeconds(Math.Max(1, delaySeconds));
    }

    void TickSpawns(List<Pending> outbound)
    {
        var now = _clock();
        foreach (var slot in _spawnSlots.Values)
        {
            if (slot.RespawnAt == null || now < slot.RespawnAt)
                continue;
            if (_byId.ContainsKey(slot.Definition.ObjectId))
            {
                slot.RespawnAt = null;
                continue;
            }

            MaterializeSpawn(slot.Definition, slot.MapId);
            slot.RespawnAt = null;
            if (_byId.TryGetValue(slot.Definition.ObjectId, out var npc))
                FanoutNewNpc(npc, outbound);
        }
    }

    void FanoutNewNpc(ZoneEntity npc, List<Pending> outbound)
    {
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (!VisibleTo(player.ObjectId, npc.ObjectId))
                continue;
            if (!player.Knownlist.Add(npc.ObjectId))
                continue;
            outbound.Add(Pack(player.Session, MsgType.NpcInfo, NpcInfo(npc)));
        }
    }

    /// <summary>Test hook: advance respawn timers without waiting on the simulation loop.</summary>
    internal void AdvanceSpawnsForTests()
    {
        lock (_gate)
        {
            TickCorpseDecay(_clock().ToUnixTimeMilliseconds(), []);
            TickSpawns([]);
        }
    }
}
