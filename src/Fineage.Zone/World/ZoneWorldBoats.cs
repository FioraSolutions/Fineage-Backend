using Fineage.Net.Contract;
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
            _boatRuntime[def.Id] = new BoatRuntime
            {
                ObjectId = objectId,
                LegIndex = 0,
                PointIndex = 0,
                DockIndex = 0,
                Phase = BoatPhase.Leg
            };
            FanoutNewNpc(entity);
            BroadcastBoatMove(entity, entity.X, entity.Y, entity.Z);
            ZoneLog.Info($"[boat] spawned id={def.Id} obj={objectId} at {def.SpawnX},{def.SpawnY}");
        }
    }

    internal IEnumerable<ZoneEntity> BoatsForTests() => _byId.Values.Where(e => e.IsBoat);

    internal void BroadcastBoatMoveForTests(ZoneEntity boat, int destX, int destY, int destZ) =>
        BroadcastBoatMove(boat, destX, destY, destZ);

    void TickBoats(long now)
    {
        if (!BoatSettings.Enabled)
            return;
        foreach (var (_, rt) in _boatRuntime)
        {
            if (!_byId.TryGetValue(rt.ObjectId, out var boat) || !boat.IsBoat || boat.MoveActive)
                continue;
            if (rt.WaitUntilMs > now)
                continue;
            if (BoatCatalog.Get(boat.BoatDefinitionId) is not { } def)
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

    void FanoutBoatDeparture(ZoneEntity boat, BoatPathPoint dest)
    {
        var watchers = NetWatchersOfPeers(boat);
        if (watchers is not { Count: > 0 } || NetBridge == null)
            return;
        NetBridge.FanoutVehicleDeparture(watchers, VehicleDepartureFrame(boat, dest.X, dest.Y, dest.Z));
    }

    void BroadcastBoatMove(ZoneEntity boat, int destX, int destY, int destZ)
    {
        var watchers = NetWatchersOfPeers(boat);
        if (watchers is not { Count: > 0 } || NetBridge == null)
            return;
        if (!boat.MoveActive && boat.X == destX && boat.Y == destY && boat.Z == destZ)
        {
            NetBridge.FanoutVehicleInfo(watchers, VehicleInfoFrame(boat));
            return;
        }

        NetBridge.FanoutVehicleDeparture(watchers, VehicleDepartureFrame(boat, destX, destY, destZ));
    }

    static VehicleInfoNetMsg VehicleInfoFrame(ZoneEntity boat) => new()
    {
        BoatObjectId = boat.ObjectId,
        X = boat.X,
        Y = boat.Y,
        Z = boat.Z,
        Heading = boat.Heading
    };

    static VehicleDepartureNetMsg VehicleDepartureFrame(ZoneEntity boat, int destX, int destY, int destZ) => new()
    {
        BoatObjectId = boat.ObjectId,
        MoveSpeed = (int)(boat.Running ? boat.RunSpeed : boat.WalkSpeed),
        RotationSpeed = DefaultBoatRotationSpeed,
        DestX = destX,
        DestY = destY,
        DestZ = destZ
    };

    /// <summary>Mobius <c>VehiclePathPoint</c> default rotation when the datapack omits it.</summary>
    internal const int DefaultBoatRotationSpeed = 4000;
}

enum BoatPhase : byte
{
    Leg,
    DockWait
}

sealed class BoatRuntime
{
    public int ObjectId;
    public BoatPhase Phase;
    public int LegIndex;
    public int PointIndex;
    public int DockIndex;
    public long WaitUntilMs;
}