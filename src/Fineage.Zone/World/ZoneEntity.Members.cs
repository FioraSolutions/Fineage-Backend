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
    /// <summary>Mobius AISkillScope.DEBUFF rows (debuff hate target).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiDebuffSkills
    {
        get => NpcTemplate?.AiDebuffSkills;
        set => EnsureNpcTemplate().AiDebuffSkills = value;
    }

    /// <summary>Mobius AISkillScope.IMMOBILIZE (sleep/root/stun/paralyze).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiImmobilizeSkills
    {
        get => NpcTemplate?.AiImmobilizeSkills;
        set => EnsureNpcTemplate().AiImmobilizeSkills = value;
    }

    /// <summary>Mobius AISkillScope.COT silence.</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiSilenceSkills
    {
        get => NpcTemplate?.AiSilenceSkills;
        set => EnsureNpcTemplate().AiSilenceSkills = value;
    }

    /// <summary>Mobius AISkillScope.SUICIDE (Death Bomb when HP &lt; 30%).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiSuicideSkills
    {
        get => NpcTemplate?.AiSuicideSkills;
        set => EnsureNpcTemplate().AiSuicideSkills = value;
    }

    public int MinSkillChance
    {
        get => NpcTemplate?.MinSkillChance ?? 7;
        init => EnsureNpcTemplate().MinSkillChance = value;
    }

    public int MaxSkillChance
    {
        get => NpcTemplate?.MaxSkillChance ?? 15;
        init => EnsureNpcTemplate().MaxSkillChance = value;
    }

    ZoneNpcTemplatePayload EnsureNpcTemplate() => NpcTemplate ??= new ZoneNpcTemplatePayload();
    /// <summary>Mobius auto soulshot set: shot item ids recharged after each discharge.</summary>
    public HashSet<int> AutoShots => EnsureStatus().AutoShots;

    ZonePlayerStatusPayload EnsureStatus() => Status ??= new ZonePlayerStatusPayload();
    /// <summary>Player skills/abnormals payload; null until first skill access (#353 composition).</summary>
    public ZonePlayerSkillsPayload? Skills { get; set; }

    /// <summary>Mobius ShortCuts keyed by world slot (slot + page * 12).</summary>
    public Dictionary<int, Fineage.Accounts.StoredShortcut> Shortcuts => EnsureSkills().Shortcuts;

    /// <summary>Trainer-learned skills (persisted; Mobius character_skills). Merged into KnownSkills.</summary>
    public Dictionary<int, int> LearnedSkills => EnsureSkills().LearnedSkills;

    /// <summary>
    /// Clan-granted skills (Mobius <c>Clan.addSkillEffects</c>). Live session map — not in character_skills.
    /// Folds through the same passive catalog as trainer masteries once populated.
    /// </summary>
    public Dictionary<int, int> ClanSkills => EnsureSkills().ClanSkills;

    /// <summary>
    /// Equipped item SA + complete armor-set skills (Mobius Inventory temporary skills). Not persisted.
    /// </summary>
    public Dictionary<int, int> GearSkills => EnsureSkills().GearSkills;

    /// <summary>True while an abnormal with combat stat funcs is active (UserInfo refresh on change).</summary>
    public bool HasStatAbnormal
    {
        get => Skills?.HasStatAbnormal ?? false;
        set => EnsureSkills().HasStatAbnormal = value;
    }
    /// <summary>Shared move payload; null until first move access (#353 composition).</summary>
}
