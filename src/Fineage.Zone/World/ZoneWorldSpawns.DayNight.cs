using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    bool? _spawnPeriodNight;

    bool SpawnPeriodActive(SpawnNpcDefinition def) =>
        SpawnPeriodOfDayParse.IsActive(def.PeriodOfDay, IsNight);

    void TickDayNightSpawns()
    {
        bool night = IsNight;
        if (_spawnPeriodNight == night)
            return;
        _spawnPeriodNight = night;
        RefreshDayNightSpawns();
    }

    void RefreshDayNightSpawns()
    {
        foreach (var (objectId, slot) in _spawnSlots)
        {
            bool want = SpawnPeriodActive(slot.Definition);
            bool alive = _byId.ContainsKey(objectId);
            if (want && !alive && slot.RespawnAt == null)
                MaterializeSpawn(slot.Definition, slot.MapId);
            else if (!want && alive && _byId.TryGetValue(objectId, out var npc))
                DespawnSpawnSlot(npc);
        }
    }

    /// <summary>Remove a fixed spawn without starting its respawn timer (day/night hand-off).</summary>
    void DespawnSpawnSlot(ZoneEntity npc)
    {
        int objectId = npc.ObjectId;
        if (_spawnSlots.TryGetValue(objectId, out var slot))
            ClearSpawnDue(slot);
        if (_byId.Remove(objectId))
        {
            npc.DropNetWatcherAudience();
            _aoi.Leave(objectId);
            foreach (var player in Players())
            {
                if (player.Session == null)
                    continue;
                if (player.Knownlist.Remove(objectId))
                    NetBridge?.FanoutKnownlistDiff(player.ObjectId, [], [objectId]);
            }
        }
    }
}