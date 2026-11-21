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

  public static bool IsInFestivalArena(ZoneWorld world, int objectId) => false;

  public static bool IsInDimensionalRift(ZoneWorld world, int objectId) => false;

  public static bool IsOlympiadObserver(ZoneWorld world, int objectId) => false;

  public static bool IsInGrandBossZone(ZoneWorld world, int objectId) => false;
}