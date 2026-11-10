namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    /// <summary>Player penalty/shot payload; null until first status access (#353 composition).</summary>
    public ZonePlayerStatusPayload? Status { get; set; }

    public int ExpertiseBonus
    {
        get => Status?.ExpertiseBonus ?? 0;
        set => EnsureStatus().ExpertiseBonus = value;
    }

    /// <summary>Weapon expertise penalty, 0–4. Updated when gear changes and on enter.</summary>
    public int WeaponGradePenalty
    {
        get => Status?.WeaponGradePenalty ?? 0;
        set => EnsureStatus().WeaponGradePenalty = value;
    }

    /// <summary>Armor expertise penalty, 0–4. Updated when gear changes and on enter.</summary>
    public int ArmorGradePenalty
    {
        get => Status?.ArmorGradePenalty ?? 0;
        set => EnsureStatus().ArmorGradePenalty = value;
    }

    /// <summary>
    /// Grade Penalty level, 0–4 — the level skill 4267 is added at (Mobius
    /// <c>addSkill(4267, max(weaponPenalty, armorPenalty))</c>). The stat fold needs this single level, not the two raw
    /// penalties. Derived from gear, so never persisted.
    /// </summary>
    public int GradePenaltyLevel
    {
        get => Status?.GradePenaltyLevel ?? 0;
        set => EnsureStatus().GradePenaltyLevel = value;
    }

    /// <summary>
    /// Death Penalty level, 0–15 (Mobius <c>Player._deathPenaltyBuffLevel</c>). Persisted: it survives logout and
    /// shrinks only on its own recovery rules. Folds skill 5076 into the stats and is reported on
    /// <c>EtcStatusUpdate</c>.
    /// </summary>
    public int DeathPenaltyLevel
    {
        get => Status?.DeathPenaltyLevel ?? 0;
        set => EnsureStatus().DeathPenaltyLevel = value;
    }

    /// <summary>Mobius henna dye id in slot 1 (0 = empty). Persisted.</summary>
    public int HennaDye1
    {
        get => Status?.HennaDye1 ?? 0;
        set => EnsureStatus().HennaDye1 = value;
    }

    public int HennaDye2
    {
        get => Status?.HennaDye2 ?? 0;
        set => EnsureStatus().HennaDye2 = value;
    }

    public int HennaDye3
    {
        get => Status?.HennaDye3 ?? 0;
        set => EnsureStatus().HennaDye3 = value;
    }

    /// <summary>Cached henna STR delta (Mobius <c>getHennaStatSTR</c>). Recomputed via <c>RefreshHenna</c>.</summary>
    public int HennaStr
    {
        get => Status?.HennaStr ?? 0;
        set => EnsureStatus().HennaStr = value;
    }

    public int HennaCon
    {
        get => Status?.HennaCon ?? 0;
        set => EnsureStatus().HennaCon = value;
    }

    public int HennaDex
    {
        get => Status?.HennaDex ?? 0;
        set => EnsureStatus().HennaDex = value;
    }

    public int HennaInt
    {
        get => Status?.HennaInt ?? 0;
        set => EnsureStatus().HennaInt = value;
    }

    public int HennaMen
    {
        get => Status?.HennaMen ?? 0;
        set => EnsureStatus().HennaMen = value;
    }

    public int HennaWit
    {
        get => Status?.HennaWit ?? 0;
        set => EnsureStatus().HennaWit = value;
    }

    /// <summary>
    /// Weight Penalty level, 0–4 (Mobius <c>Player._curWeightPenalty</c>). Derived from the load and recomputed on
    /// every inventory change and on enter — never stored. Skips persistence on purpose: it is a function of the bag.
    /// </summary>
    public int WeightPenaltyLevel
    {
        get => Status?.WeightPenaltyLevel ?? 0;
        set => EnsureStatus().WeightPenaltyLevel = value;
    }

    /// <summary>
    /// Fractional HP/MP regen left over from the previous tick. Mobius keeps vitals as doubles, so a halved rate
    /// (Weight Penalty 4270) still accumulates; here the integer vitals need the remainder carried explicitly.
    /// </summary>
    public double RegenCarryHp
    {
        get => Status?.RegenCarryHp ?? 0;
        set => EnsureStatus().RegenCarryHp = value;
    }

    public double RegenCarryMp
    {
        get => Status?.RegenCarryMp ?? 0;
        set => EnsureStatus().RegenCarryMp = value;
    }
}
