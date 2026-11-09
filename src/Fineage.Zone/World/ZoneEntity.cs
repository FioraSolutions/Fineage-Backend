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
