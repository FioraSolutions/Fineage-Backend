using Fineage.Zone.Boats;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>BoatManager</c> + vehicle scripts: boats move on schedules; passengers/boarding use vehicle packets later.</summary>
public sealed partial class ZoneWorld
{
    readonly Lazy<BoatCatalog> _boatCatalog = new(BoatCatalog.LoadDefault);
    readonly Dictionary<int, BoatRuntime> _boatRuntime = [];

    internal BoatCatalog BoatCatalog => _boatCatalog.Value;

    void BootstrapBoats()
    {
        if (!BoatSettings.Enabled || BoatCatalog.Count == 0)
            return;
        foreach (var def in BoatCatalog.All)
        {
            if (_boatRuntime.ContainsKey(def.Id))
                continue;
            int objectId = AllocateQuestNpcObjectId();
            var entity = new ZoneEntity
            {
                ObjectId = objectId,
                Kind = ZoneEntityKind.Npc,
                NpcId = def.BoatNpcId,
                Name = $"boat_{def.Id}",
                NpcType = "Boat",
                Attackable = false,
                X = def.SpawnX,
                Y = def.SpawnY,
                Z = def.SpawnZ,
                Heading = def.SpawnHeading,
                MaxHp = 50_000,
                CurHp = 50_000,
                RunSpeed = 800,
                WalkSpeed = 400,
                Running = true,
                IsBoat = true,
                BoatDefinitionId = def.Id,
                HomeSet = false
            };
            _byId.Add(objectId, entity);
            _aoi.Enter(objectId, entity.X, entity.Y);
            _boatRuntime[def.Id] = new BoatRuntime { LegIndex = 0, PointIndex = 0, DockIndex = 0, Phase = BoatPhase.Leg };
            FanoutNewNpc(entity);
            ZoneLog.Info($"[boat] spawned id={def.Id} obj={objectId} at {def.SpawnX},{def.SpawnY}");
        }
    }

    void TickBoats(long now)
    {
        if (!BoatSettings.Enabled)
            return;
        foreach (var (defId, rt) in _boatRuntime)
        {
            if (!TryBoatEntity(defId, out var boat) || boat.MoveActive)
                continue;
            if (rt.WaitUntilMs > now)
                continue;
            if (BoatCatalog.Get(defId) is not { } def)
                continue;

            switch (rt.Phase)
            {
                case BoatPhase.DockWait:
                    rt.Phase = BoatPhase.Leg;
                    rt.PointIndex = 0;
                    break;
                case BoatPhase.Leg:
                    if (def.Legs.Length == 0)
                        continue;
                    var leg = def.Legs[rt.LegIndex % def.Legs.Length];
                    if (leg.Points.Length == 0)
                    {
                        rt.LegIndex++;
                        continue;
                    }
                    if (rt.PointIndex >= leg.Points.Length)
                    {
                        rt.LegIndex = (rt.LegIndex + 1) % def.Legs.Length;
                        rt.PointIndex = 0;
                        if (def.Docks.Length > 0)
                        {
                            rt.Phase = BoatPhase.DockWait;
                            var dock = def.Docks[rt.DockIndex % def.Docks.Length];
                            rt.DockIndex++;
                            rt.WaitUntilMs = now + Math.Max(1000, dock.WaitMs);
                            if (dock.Points.Length > 0)
                            {
                                var pt = dock.Points[0];
                                boat.Heading = pt.Heading;
                                FanoutBoatDeparture(boat, pt);
                                StartNpcMove(boat, pt.X, pt.Y, pt.Z, now);
                            }
                        }
                        continue;
                    }

                    var point = leg.Points[rt.PointIndex];
                    boat.RunSpeed = point.Speed;
                    boat.WalkSpeed = point.Speed / 2;
                    boat.Running = true;
                    boat.Heading = point.Heading;
                    FanoutBoatDeparture(boat, point);
                    StartNpcMove(boat, point.X, point.Y, point.Z, now);
                    rt.PointIndex++;
                    break;
            }
        }
    }

    bool TryBoatEntity(int defId, out ZoneEntity boat)
    {
        foreach (var e in _byId.Values)
        {
            if (e.IsBoat && e.BoatDefinitionId == defId)
            {
                boat = e;
                return true;
            }
        }
        boat = null!;
        return false;
    }

    void FanoutBoatDeparture(ZoneEntity boat, BoatPathPoint dest)
    {
        // Mobius VehicleDeparture: encoded in client-packet-contracts.md; move state is the Fineage Net path today.
        NetBridge?.FanoutMoveState(
            KnownWatchers(boat.ObjectId),
            boat.ObjectId,
            boat.X,
            boat.Y,
            boat.Z,
            dest.X,
            dest.Y,
            dest.Z,
            boat.Running);
    }
}

enum BoatPhase : byte
{
    Leg,
    DockWait
}

sealed class BoatRuntime
{
    public BoatPhase Phase;
    public int LegIndex;
    public int PointIndex;
    public int DockIndex;
    public long WaitUntilMs;
}