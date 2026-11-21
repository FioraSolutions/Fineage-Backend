namespace Fineage.Net.Contract;

/// <summary>Interlude <c>MonRaceInfo</c> (0xDD) — monster derby runners and per-segment speeds.</summary>
public sealed class MonRaceInfoNetMsg
{
    public int CodeA { get; set; }
    public int CodeB { get; set; }
    public MonRaceRunnerNetMsg[] Runners { get; set; } = [];
}

public sealed class MonRaceRunnerNetMsg
{
    public int ObjectId { get; set; }
    public int NpcIdPlusDisplay { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    public int EndZ { get; set; }
    public double CollisionHeight { get; set; }
    public double CollisionRadius { get; set; }
    public int Unknown120 { get; set; } = 120;
    public byte[] Speeds { get; set; } = new byte[20];
}