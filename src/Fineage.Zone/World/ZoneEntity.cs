using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Game.Shops;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    public int ObjectId { get; init; }
    public ZoneEntityKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>NPC template payload; null until first template access (#353 composition).</summary>
    public ZoneNpcTemplatePayload? NpcTemplate { get; set; }

    /// <summary>Mobius instance type (Merchant, Guard, Monster, …).</summary>
    public string NpcType
    {
        get => NpcTemplate?.NpcType ?? "";
        init => EnsureNpcTemplate().NpcType = value ?? "";
    }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Facing 0..65535 (Mobius heading); updated while moving and when swinging at a target.</summary>
    public int Heading { get; set; }
    /// <summary>Players: changes on class transfer (Mobius setClassId).</summary>
    public int ClassId { get; set; }
    public int NpcId
    {
        get => NpcTemplate?.NpcId ?? 0;
        init => EnsureNpcTemplate().NpcId = value;
    }

    public bool Attackable
    {
        get => NpcTemplate?.Attackable ?? false;
        init => EnsureNpcTemplate().Attackable = value;
    }
    /// <summary>Shared vitals payload; null until first HP/MP/XP access (#353 composition).</summary>
    public ZoneEntityVitalsPayload? Vitals { get; set; }

    public int CurHp
    {
        get => Vitals?.CurHp ?? 0;
        set => EnsureVitals().CurHp = value;
    }

    public int MaxHp
    {
        get => Vitals?.MaxHp ?? 0;
        set => EnsureVitals().MaxHp = value;
    }

    public int CurMp
    {
        get => Vitals?.CurMp ?? 0;
        set => EnsureVitals().CurMp = value;
    }

    public int MaxMp
    {
        get => Vitals?.MaxMp ?? 0;
        set => EnsureVitals().MaxMp = value;
    }

    public int Level
    {
        get => Vitals?.Level ?? 1;
        set => EnsureVitals().Level = value;
    }

    public long Exp
    {
        get => Vitals?.Exp ?? 0;
        set => EnsureVitals().Exp = value;
    }

    /// <summary>Mobius <c>_expBeforeDeath</c>: set on death penalty, cleared when a resurrection restores exp.</summary>
    public long ExpBeforeDeath
    {
        get => Vitals?.ExpBeforeDeath ?? 0;
        set => EnsureVitals().ExpBeforeDeath = value;
    }

    /// <summary>Mobius <c>_reviveRequested</c>.</summary>
    public bool ReviveRequested
    {
        get => Vitals?.ReviveRequested ?? false;
        set => EnsureVitals().ReviveRequested = value;
    }

    /// <summary>Mobius <c>_revivePower</c> (restore percent).</summary>
    public double RevivePower
    {
        get => Vitals?.RevivePower ?? 0;
        set => EnsureVitals().RevivePower = value;
    }

    public int Sp
    {
        get => Vitals?.Sp ?? 0;
        set => EnsureVitals().Sp = value;
    }
    /// <summary>NPC kill reward from the Mobius template (acquire exp/sp). Zero for players.</summary>
    public long RewardExp
    {
        get => NpcTemplate?.RewardExp ?? 0;
        init => EnsureNpcTemplate().RewardExp = value;
    }

    public int RewardSp
    {
        get => NpcTemplate?.RewardSp ?? 0;
        init => EnsureNpcTemplate().RewardSp = value;
    }
    public QuestJournal Quests { get; set; } = new();
    /// <summary>Player identity payload; null until first race/clan/PvP access (#353 composition).</summary>
    public ZonePlayerIdentityPayload? Identity { get; set; }

    public int Race
    {
        get => Identity?.Race ?? 0;
        init => EnsureIdentity().Race = value;
    }

    public int Sex
    {
        get => Identity?.Sex ?? 0;
        init => EnsureIdentity().Sex = value;
    }

    public bool Hero
    {
        get => Identity?.Hero ?? false;
        set => EnsureIdentity().Hero = value;
    }

    public bool InClan
    {
        get => Identity?.InClan ?? false;
        set => EnsureIdentity().InClan = value;
    }

    public bool ClanLeader
    {
        get => Identity?.ClanLeader ?? false;
        set => EnsureIdentity().ClanLeader = value;
    }

    public int PledgeClass
    {
        get => Identity?.PledgeClass ?? 0;
        set => EnsureIdentity().PledgeClass = value;
    }

    public int CastleId
    {
        get => Identity?.CastleId ?? 0;
        set => EnsureIdentity().CastleId = value;
    }

    public int HideoutId
    {
        get => Identity?.HideoutId ?? 0;
        set => EnsureIdentity().HideoutId = value;
    }

    public bool PvpFlagged
    {
        get => Identity?.PvpFlagged ?? false;
        set => EnsureIdentity().PvpFlagged = value;
    }

    /// <summary>Unix ms when the PvP flag expires (Mobius <c>_pvpFlagLasts</c>).</summary>
    public long PvpFlagUntilMs
    {
        get => Identity?.PvpFlagUntilMs ?? 0;
        set => EnsureIdentity().PvpFlagUntilMs = value;
    }

    public int PkKills
    {
        get => Identity?.PkKills ?? 0;
        set => EnsureIdentity().PkKills = value;
    }

    public int PvpKills
    {
        get => Identity?.PvpKills ?? 0;
        set => EnsureIdentity().PvpKills = value;
    }

    /// <summary>Mobius karma (Chaotic); death item drop uses karma rates when &gt; 0 and PK ≥ limit.</summary>
    public int Karma
    {
        get => Identity?.Karma ?? 0;
        set => EnsureIdentity().Karma = value;
    }

    ZonePlayerIdentityPayload EnsureIdentity() => Identity ??= new ZonePlayerIdentityPayload();

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
    /// <summary>When true, regen rates double (classic sitting rest).</summary>
    public bool Sitting
    {
        get => Vitals?.Sitting ?? false;
        set => EnsureVitals().Sitting = value;
    }

    ZoneEntityVitalsPayload EnsureVitals() => Vitals ??= new ZoneEntityVitalsPayload();
}
