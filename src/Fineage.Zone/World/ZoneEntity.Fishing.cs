namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    /// <summary>Mobius <c>Player.isFishing</c>: the line is out; the fisher cannot move (immobilized).</summary>
    public bool Fishing { get; set; }

    /// <summary>Bait position while <see cref="Fishing"/> (Mobius <c>getFishX/Y/Z</c>).</summary>
    public int FishX { get; set; }
    public int FishY { get; set; }
    public int FishZ { get; set; }

    /// <summary>Mobius <c>ShotType.FISH_SOULSHOTS</c> on the rod: the next pump or reel deals double.</summary>
    public bool FishShotCharged { get; set; }
}
