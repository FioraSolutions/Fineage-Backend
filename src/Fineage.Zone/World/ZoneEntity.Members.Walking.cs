namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    public string? WalkingRouteName { get; set; }
    public int WalkingPointIndex { get; set; }
    public bool WalkingForward { get; set; } = true;
    public long WalkingWaitUntilMs { get; set; }
    public bool IsBoat { get; set; }
    public int BoatDefinitionId { get; set; }
}