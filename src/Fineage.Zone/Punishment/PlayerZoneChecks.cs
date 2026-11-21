using Fineage.Zone.World;

namespace Fineage.Zone.Punishment;

/// <summary>Shared eligibility checks for wedding <c>.gotolove</c> and similar teleports.</summary>
public static class PlayerZoneChecks
{
    public static bool IsPlayerJailed(ZoneWorld world, int objectId)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.PunishmentManager.IsJailed(objectId, world.PunishmentNowUnixMs());
    }

    public static bool IsInFestivalArena(ZoneWorld world, int objectId)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.PlayerParticipation.InFestival(objectId);
    }

    public static bool IsInDimensionalRift(ZoneWorld world, int objectId)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.PlayerParticipation.InDimensionalRift(objectId);
    }

    public static void RegisterFestivalParticipation(ZoneWorld world, int objectId, bool inside)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.PlayerParticipation.SetFestival(objectId, inside);
    }

    public static void RegisterRiftParticipation(ZoneWorld world, int objectId, bool inside)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.PlayerParticipation.SetDimensionalRift(objectId, inside);
    }

    public static bool IsOlympiadObserver(ZoneWorld world, int objectId)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.OlympiadObserving(objectId);
    }

    public static bool IsInGrandBossZone(ZoneWorld world, int objectId) => false;
}