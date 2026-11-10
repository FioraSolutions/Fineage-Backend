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
    /// <summary>Player store/manufacture payload; null until first store access (#353 composition).</summary>
    public ZonePlayerStorePayload? Store { get; set; }

    /// <summary>Mobius <c>PrivateStoreType</c> (None / Sell / Buy).</summary>
    public byte PrivateStoreType
    {
        get => Store?.PrivateStoreType ?? 0;
        set => EnsureStore().PrivateStoreType = value;
    }

    /// <summary>Store title shown above the sitting pawn (Mobius PrivateStoreMsgSell/Buy).</summary>
    public string PrivateStoreMessage
    {
        get => Store?.PrivateStoreMessage ?? "";
        set => EnsureStore().PrivateStoreMessage = value ?? "";
    }

    /// <summary>Active sell listings while <see cref="PrivateStoreType"/> is Sell.</summary>
    public List<PrivateStoreSellListing> PrivateSellList => EnsureStore().PrivateSellList;

    /// <summary>Active buy listings while <see cref="PrivateStoreType"/> is Buy (by template id).</summary>
    public List<PrivateStoreBuyListing> PrivateBuyList => EnsureStore().PrivateBuyList;

    /// <summary>Active manufacture listings while <see cref="PrivateStoreType"/> is Manufacture.</summary>
    public List<ManufactureListing> ManufactureList => EnsureStore().ManufactureList;

    /// <summary>Mobius recipe book: known <c>RecipeList</c> ids (make-self / book open).</summary>
    public HashSet<int> KnownRecipes => EnsureStore().KnownRecipes;

    ZonePlayerStorePayload EnsureStore() => Store ??= new ZonePlayerStorePayload();

    /// <summary>Player social payload; null until first friend/block access (#353 composition).</summary>
    public ZonePlayerSocialPayload? Social { get; set; }

    /// <summary>Friend objectId → name snapshot (Mobius friend list).</summary>
    public Dictionary<int, string> Friends => EnsureSocial().Friends;

    /// <summary>Blocked objectId → name snapshot (Mobius ignore list).</summary>
    public Dictionary<int, string> Blocked => EnsureSocial().Blocked;

    /// <summary>Mobius block-all / message refusal mode.</summary>
    public bool BlockAll
    {
        get => Social?.BlockAll ?? false;
        set => EnsureSocial().BlockAll = value;
    }

    ZonePlayerSocialPayload EnsureSocial() => Social ??= new ZonePlayerSocialPayload();
    /// <summary>NPC Mobius physical attack (0 = unknown → level placeholder). Players read the class template.</summary>
    public double PAtk
    {
        get => NpcTemplate?.PAtk ?? 0;
        init => EnsureNpcTemplate().PAtk = value;
    }

    /// <summary>NPC Mobius <c>vitals hpRegen</c>: HP restored per 3 s regen tick (players use the class table).</summary>
    public double HpRegen
    {
        get => NpcTemplate?.HpRegen ?? 0;
        init => EnsureNpcTemplate().HpRegen = value;
    }

    /// <summary>NPC Mobius <c>vitals mpRegen</c>: MP restored per 3 s regen tick.</summary>
    public double MpRegen
    {
        get => NpcTemplate?.MpRegen ?? 0;
        init => EnsureNpcTemplate().MpRegen = value;
    }

    /// <summary>NPC Mobius physical defence (0 = unknown).</summary>
    public double PDef
    {
        get => NpcTemplate?.PDef ?? 0;
        init => EnsureNpcTemplate().PDef = value;
    }

    /// <summary>NPC Mobius attack speed (Pspd, 0 = default 253).</summary>
    public int PAtkSpd
    {
        get => NpcTemplate?.PAtkSpd ?? 0;
        init => EnsureNpcTemplate().PAtkSpd = value;
    }

    /// <summary>NPC template physical attack range (Mobius atkRange: 40 melee, 1100 archers); 0 = default.</summary>
    public int AttackRange
    {
        get => NpcTemplate?.AttackRange ?? 0;
        init => EnsureNpcTemplate().AttackRange = value;
    }

    /// <summary>Mobius <c>ai isAggressive</c>; attacks players inside <see cref="AggroRange"/> on its own.</summary>
    public bool Aggressive
    {
        get => NpcTemplate?.Aggressive ?? false;
        init => EnsureNpcTemplate().Aggressive = value;
    }

    public int AggroRange
    {
        get => NpcTemplate?.AggroRange ?? 0;
        init => EnsureNpcTemplate().AggroRange = value;
    }

    /// <summary>Mobius <c>clanHelpRange</c> + <c>clanList</c>: same-clan mobs in range join the fight.</summary>
    public int ClanHelpRange
    {
        get => NpcTemplate?.ClanHelpRange ?? 0;
        init => EnsureNpcTemplate().ClanHelpRange = value;
    }

    public IReadOnlyList<string>? Clans
    {
        get => NpcTemplate?.Clans;
        init => EnsureNpcTemplate().Clans = value;
    }

    public IReadOnlyList<int>? IgnoreNpcIds
    {
        get => NpcTemplate?.IgnoreNpcIds;
        init => EnsureNpcTemplate().IgnoreNpcIds = value;
    }
    /// <summary>NPC aggro/chase payload; null until first aggro access (#353 composition).</summary>
    public ZoneNpcAggroPayload? Aggro { get; set; }

    /// <summary>NPC: Mobius AggroList (attacker objectId → hate). <see cref="AggroTargetId"/> is the most hated.</summary>
    public Dictionary<int, long> HateList => EnsureAggro().HateList;

    /// <summary>
    /// NPC: damage dealt per attacker (Mobius <c>AggroInfo.getDamage</c>). Exp/SP are paid as a share of this, so a
    /// kill belongs to everyone who hit the mob, not only to the last blow.
    /// </summary>
    public Dictionary<int, long> DamageBy => EnsureAggro().DamageBy;

    public long NextAggroScanMs
    {
        get => Aggro?.NextAggroScanMs ?? 0;
        set => EnsureAggro().NextAggroScanMs = value;
    }

    public long NextClanCallMs
    {
        get => Aggro?.NextClanCallMs ?? 0;
        set => EnsureAggro().NextClanCallMs = value;
    }

    /// <summary>Attack stance the watchers were last told (Mobius AutoAttackStart / AutoAttackStop).</summary>
    public bool StanceShown
    {
        get => Aggro?.StanceShown ?? false;
        set => EnsureAggro().StanceShown = value;
    }
    /// <summary>NPC Mobius base accuracy / evasion / crit / random spread / DEX (players use the class template).</summary>
    public double BaseAccuracy
    {
        get => NpcTemplate?.BaseAccuracy ?? 0;
        init => EnsureNpcTemplate().BaseAccuracy = value;
    }

    public double BaseEvasion
    {
        get => NpcTemplate?.BaseEvasion ?? 0;
        init => EnsureNpcTemplate().BaseEvasion = value;
    }

    public int BaseCritical
    {
        get => NpcTemplate?.BaseCritical ?? 0;
        init => EnsureNpcTemplate().BaseCritical = value;
    }

    public int RandomDamage
    {
        get => NpcTemplate?.RandomDamage ?? 0;
        init => EnsureNpcTemplate().RandomDamage = value;
    }
    /// <summary>Shared combat timing payload; null until first combat access (#353 composition).</summary>
    public ZoneEntityCombatPayload? Combat { get; set; }

    /// <summary>
    /// Mobius <c>getDEX()</c>: feeds <c>FuncMoveSpeed</c> (run/walk/swim speed × DEX bonus), accuracy, evasion and
    /// crit. Players copy it from the class template and re-copy it on class change.
    /// </summary>
    public int Dex
    {
        get => Combat?.Dex ?? 30;
        set => EnsureCombat().Dex = value;
    }
    /// <summary>NPC Mobius shield defence / block rate (players read the equipped shield).</summary>
    public int ShieldDef
    {
        get => NpcTemplate?.ShieldDef ?? 0;
        init => EnsureNpcTemplate().ShieldDef = value;
    }

    public int ShieldRate
    {
        get => NpcTemplate?.ShieldRate ?? 0;
        init => EnsureNpcTemplate().ShieldRate = value;
    }

    /// <summary>NPC Mobius magic attack / defence.</summary>
    public double MAtk
    {
        get => NpcTemplate?.MAtk ?? 0;
        init => EnsureNpcTemplate().MAtk = value;
    }

    public double MDef
    {
        get => NpcTemplate?.MDef ?? 0;
        init => EnsureNpcTemplate().MDef = value;
    }
    /// <summary>Mobius ChargedShot SOULSHOTS: weapon is charged; next swing doubles P.Atk and clears it.</summary>
    public bool SoulshotCharged
    {
        get => Status?.SoulshotCharged ?? false;
        set => EnsureStatus().SoulshotCharged = value;
    }

    /// <summary>0 none, 1 spiritshot, 2 blessed spiritshot (Mobius ShotType SPIRITSHOTS / BLESSED_SPIRITSHOTS).</summary>
    public int SpiritshotCharge
    {
        get => Status?.SpiritshotCharge ?? 0;
        set => EnsureStatus().SpiritshotCharge = value;
    }
    /// <summary>NPC remaining Mobius SoulShot stock (template starting count; decremented on fire).</summary>
    public int NpcSoulShotLeft
    {
        get => NpcTemplate?.NpcSoulShotLeft ?? 0;
        set => EnsureNpcTemplate().NpcSoulShotLeft = value;
    }

    /// <summary>NPC SoulShotRate % (template).</summary>
    public int NpcSoulShotChance
    {
        get => NpcTemplate?.NpcSoulShotChance ?? 0;
        set => EnsureNpcTemplate().NpcSoulShotChance = value;
    }

    /// <summary>NPC remaining SpiritShot stock.</summary>
    public int NpcSpiritShotLeft
    {
        get => NpcTemplate?.NpcSpiritShotLeft ?? 0;
        set => EnsureNpcTemplate().NpcSpiritShotLeft = value;
    }

    public int NpcSpiritShotChance
    {
        get => NpcTemplate?.NpcSpiritShotChance ?? 0;
        set => EnsureNpcTemplate().NpcSpiritShotChance = value;
    }

    /// <summary>Mobius weapon <c>_defenceTraits</c> multipliers (sword/bow/…); null = all 1.0.</summary>
    public IReadOnlyDictionary<string, double>? DefenceTraits
    {
        get => NpcTemplate?.DefenceTraits;
        init => EnsureNpcTemplate().DefenceTraits = value;
    }

    /// <summary>Mobius AI attack skills (skillList ∩ skill-combat); null = melee only.</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? AiSkills
    {
        get => NpcTemplate?.AiSkills;
        init => EnsureNpcTemplate().AiSkills = value;
    }

    /// <summary>Mobius AISkillScope.HEAL rows (self/clan heal when wounded).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiHealSkills
    {
        get => NpcTemplate?.AiHealSkills;
        set => EnsureNpcTemplate().AiHealSkills = value;
    }

    /// <summary>Mobius AISkillScope.BUFF rows (self buff when missing).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiBuffSkills
    {
        get => NpcTemplate?.AiBuffSkills;
        set => EnsureNpcTemplate().AiBuffSkills = value;
    }
}
