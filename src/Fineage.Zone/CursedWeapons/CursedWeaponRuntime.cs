namespace Fineage.Zone.CursedWeapons;

/// <summary>One Zariche / Akamanah life cycle (Mobius <c>CursedWeapon.java</c>).</summary>
public sealed class CursedWeaponRuntime
{
    public const int MobiusDropDenom = 100_000;
    public const int KarmaWhileWielding = -9_999_999;

    public CursedWeaponRuntime(CursedWeaponDefinition definition)
    {
        Definition = definition;
    }

    public CursedWeaponDefinition Definition { get; }

    public bool Activated { get; set; }
    public bool Dropped { get; set; }
    public int OwnerObjectId { get; set; }
    public int GroundObjectId { get; set; }
    public int PlayerReputation { get; set; }
    public int PlayerPkKills { get; set; }
    public int NbKills { get; set; }
    public long EndTimeUnixMs { get; set; }

    public bool IsActive => Activated || Dropped;

    public int SkillLevel(int skillMaxLevel)
    {
        int raw = 1 + NbKills / Math.Max(1, Definition.StageKills);
        return Math.Clamp(raw, 1, Math.Max(1, skillMaxLevel));
    }

    public long TimeLeftMs(long nowUnixMs) => Math.Max(0, EndTimeUnixMs - nowUnixMs);

    public void Reset()
    {
        Activated = false;
        Dropped = false;
        OwnerObjectId = 0;
        GroundObjectId = 0;
        PlayerReputation = 0;
        PlayerPkKills = 0;
        NbKills = 0;
        EndTimeUnixMs = 0;
    }
}