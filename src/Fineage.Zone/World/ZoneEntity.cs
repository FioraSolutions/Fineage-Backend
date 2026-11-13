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
    /// <summary>Next Mobius Fear onActionTime. Shared by players and mobs so a mob flees the caster, not its hate target.</summary>
    public long FearFleeAtUnixMs { get; set; }
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
    /// <summary>When true, regen rates double (classic sitting rest).</summary>
    public bool Sitting
    {
        get => Vitals?.Sitting ?? false;
        set => EnsureVitals().Sitting = value;
    }

    ZoneEntityVitalsPayload EnsureVitals() => Vitals ??= new ZoneEntityVitalsPayload();
}
