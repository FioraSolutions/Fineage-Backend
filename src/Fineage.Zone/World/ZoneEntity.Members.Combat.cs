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
    public List<ZoneAbnormalEntry> Abnormals => EnsureSkills().Abnormals;

    /// <summary>Known skill id to level (auto-get tree on enter).</summary>
    public Dictionary<int, int> KnownSkills => EnsureSkills().KnownSkills;

    /// <summary>Skill id to unix-ms when reuse expires.</summary>
    public Dictionary<int, long> SkillReuseUntilMs => EnsureSkills().SkillReuseUntilMs;

    /// <summary>
    /// Item id to unix-ms when the item may be used again (Mobius <c>ItemInstance.getReuseDelay</c> / reuse stamp).
    /// Item skills carry a reuse delay; the stamp lives on the character so a stack shares one timer.
    /// </summary>
    public Dictionary<int, long> ItemReuseUntilMs => EnsureSkills().ItemReuseUntilMs;

    ZonePlayerSkillsPayload EnsureSkills() => Skills ??= new ZonePlayerSkillsPayload();
    public HashSet<int> Knownlist { get; } = [];
    public int ClanId
    {
        get => Identity?.ClanId ?? 0;
        set => EnsureIdentity().ClanId = value;
    }
    /// <summary>Attack stance end, unix milliseconds. Leave and dismiss are refused until this passes.</summary>
    public long CombatUntilUnixMs
    {
        get => Combat?.CombatUntilUnixMs ?? 0;
        set => EnsureCombat().CombatUntilUnixMs = value;
    }
    /// <summary>Catacomb or necropolis id while inside a Seven Signs gate. 0 is outside. Runtime only, same as Mobius <c>isIn7sDungeon</c>.</summary>
    public int DungeonId { get; set; }
    /// <summary>0 is the open world. An Olympiad stadium uses its zone id so the pair does not share knownlist with the town.</summary>
    public int InstanceId { get; set; }
    /// <summary>Private warehouse. Null on NPCs.</summary>
    public PrivateWarehouse? Warehouse { get; set; }
    /// <summary>First character created on the account. Mobius <c>Player.isNewbie</c> when the account had no characters yet.</summary>
    public bool Newbie
    {
        get => Identity?.Newbie ?? true;
        set => EnsureIdentity().Newbie = value;
    }
    /// <summary>Mobius <c>Creature.isCastingNow</c>: unix ms until the current cast ends. Movement is refused before this.</summary>
    public long CastingUntilMs
    {
        get => Combat?.CastingUntilMs ?? 0;
        set => EnsureCombat().CastingUntilMs = value;
    }
    /// <summary>Skill damage queued for Mobius <c>onMagicHitTimer</c> (applied at hitTime, not on key press).</summary>
    public PendingSkillHit? PendingHit
    {
        get => Combat?.PendingHit;
        set => EnsureCombat().PendingHit = value;
    }
    /// <summary>NPC: most-hated player (Mobius <c>AggroList</c> top, simplified to last attacker).</summary>
    public int AggroTargetId
    {
        get => Aggro?.AggroTargetId ?? 0;
        set => EnsureAggro().AggroTargetId = value;
    }

    /// <summary>NPC: next melee swing time, unix ms.</summary>
    public long NextMobAttackMs
    {
        get => Aggro?.NextMobAttackMs ?? 0;
        set => EnsureAggro().NextMobAttackMs = value;
    }

    /// <summary>NPC: last chase destination broadcast (Mobius MoveToPawn re-sent when the pawn moves).</summary>
    public int ChaseDestX
    {
        get => Aggro?.ChaseDestX ?? 0;
        set => EnsureAggro().ChaseDestX = value;
    }

    public int ChaseDestY
    {
        get => Aggro?.ChaseDestY ?? 0;
        set => EnsureAggro().ChaseDestY = value;
    }

    public bool Chasing
    {
        get => Aggro?.Chasing ?? false;
        set => EnsureAggro().Chasing = value;
    }

    /// <summary>NPC: last AI tick, unix ms (movement integration).</summary>
    public long LastAiTickMs
    {
        get => Aggro?.LastAiTickMs ?? 0;
        set => EnsureAggro().LastAiTickMs = value;
    }

    /// <summary>NPC: spawn point captured on first aggro (Mobius <c>Attackable.returnHome</c> target).</summary>
    public bool HomeSet
    {
        get => Aggro?.HomeSet ?? false;
        set => EnsureAggro().HomeSet = value;
    }

    public int HomeX
    {
        get => Aggro?.HomeX ?? 0;
        set => EnsureAggro().HomeX = value;
    }

    public int HomeY
    {
        get => Aggro?.HomeY ?? 0;
        set => EnsureAggro().HomeY = value;
    }

    public int HomeZ
    {
        get => Aggro?.HomeZ ?? 0;
        set => EnsureAggro().HomeZ = value;
    }

    /// <summary>NPC: walking back to <see cref="HomeX"/>/<see cref="HomeY"/> after forgetting its target.</summary>
    public bool ReturningHome
    {
        get => Aggro?.ReturningHome ?? false;
        set => EnsureAggro().ReturningHome = value;
    }

    ZoneNpcAggroPayload EnsureAggro() => Aggro ??= new ZoneNpcAggroPayload();
    /// <summary>NPC corpse: unix ms when DecayTaskManager removes it. 0 = alive / not scheduled.</summary>
    public long DecayAtMs
    {
        get => Combat?.DecayAtMs ?? 0;
        set => EnsureCombat().DecayAtMs = value;
    }

    ZoneEntityCombatPayload EnsureCombat() => Combat ??= new ZoneEntityCombatPayload();

    /// <summary>NPC merchant tax payload; null until first merchant access (#353 composition).</summary>
    public ZoneNpcMerchantPayload? Merchant { get; set; }

    /// <summary>Mobius merchant price config castle. 0 when the NPC is not in a taxed town.</summary>
    public int MerchantCastleId
    {
        get => Merchant?.MerchantCastleId ?? 0;
        set => EnsureMerchant().MerchantCastleId = value;
    }

    /// <summary>Town name for TerritoryStatus (%territory%); null = castle name.</summary>
    public string? MerchantTown
    {
        get => Merchant?.MerchantTown;
        set => EnsureMerchant().MerchantTown = value;
    }

    /// <summary>Town base tax percent from Mobius <c>MerchantPriceConfig.xml</c>. Castle tax is separate.</summary>
    public int MerchantBaseTaxPercent
    {
        get => Merchant?.MerchantBaseTaxPercent ?? 0;
        set => EnsureMerchant().MerchantBaseTaxPercent = value;
    }

    ZoneNpcMerchantPayload EnsureMerchant() => Merchant ??= new ZoneNpcMerchantPayload();

    /// <summary>Buylist or multisell this player opened. Null until a shop bypass.</summary>
    public ShopSession? Shop { get; set; }
}
