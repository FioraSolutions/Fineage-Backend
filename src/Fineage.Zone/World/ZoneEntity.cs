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

sealed class ZoneEntity
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

    /// <summary>True while an abnormal with combat stat funcs is active (UserInfo refresh on change).</summary>
    public bool HasStatAbnormal
    {
        get => Skills?.HasStatAbnormal ?? false;
        set => EnsureSkills().HasStatAbnormal = value;
    }
    /// <summary>Shared move payload; null until first move access (#353 composition).</summary>
    public ZoneEntityMovePayload? Move { get; set; }

    /// <summary>
    /// Base run speed (Mobius <c>CreatureTemplate.baseRunSpd</c>). Buffs and debuffs change the live
    /// <see cref="RunSpeed"/> through the <c>runSpd</c> (<c>Stat.MOVE_SPEED</c>) functions; the base is only rewritten
    /// by a template / class change.
    /// </summary>
    public float BaseRunSpeed
    {
        get => Move?.BaseRunSpeed ?? 0;
        set => EnsureMove().BaseRunSpeed = value;
    }

    /// <summary>
    /// Live run speed sent on the wire. Kept denormalised on the entity: spawn / MoveState / UserInfo / ChangeMoveType
    /// all carry it, and <see cref="RefreshRunSpeed"/> recomputes it when buffs, passives or the class change.
    /// </summary>
    public float RunSpeed
    {
        get => Move?.LiveRunSpeed ?? BaseRunSpeed;
        set
        {
            EnsureMove().BaseRunSpeed = value;
            EnsureMove().LiveRunSpeed = value;
        }
    }

    /// <summary>Sets the live (buffed) run speed without touching the base template value.</summary>
    internal void SetLiveRunSpeed(float speed) => EnsureMove().LiveRunSpeed = speed;

    /// <summary>
    /// Base walk speed (Mobius <c>CreatureTemplate.baseWalkSpd</c>). <c>Stat.MOVE_SPEED("runSpd")</c> is the one speed
    /// stat in Mobius and <c>CreatureStat.getWalkSpeed</c> folds it onto this base, so buffs and debuffs move walk too.
    /// </summary>
    public float BaseWalkSpeed
    {
        get => Move?.BaseWalkSpeed ?? 0;
        set => EnsureMove().BaseWalkSpeed = value;
    }

    /// <summary>
    /// Live walk speed sent on the wire; mirrors <see cref="RunSpeed"/> and is recomputed with it.
    /// </summary>
    public float WalkSpeed
    {
        get => Move?.LiveWalkSpeed ?? BaseWalkSpeed;
        set
        {
            EnsureMove().BaseWalkSpeed = value;
            EnsureMove().LiveWalkSpeed = value;
        }
    }

    /// <summary>Sets the live (buffed) walk speed without touching the base template value.</summary>
    internal void SetLiveWalkSpeed(float speed) => EnsureMove().LiveWalkSpeed = speed;

    /// <summary>
    /// Mobius <c>baseMoveSpd/fastSwim</c> (<c>CreatureTemplate.java:147</c>, falls back to <c>baseRunSpd</c>).
    /// 0 = not in the template → use the run base, exactly like Mobius.
    /// </summary>
    public float BaseSwimRunSpeed
    {
        get => Move?.BaseSwimRunSpeed ?? 0;
        set => EnsureMove().BaseSwimRunSpeed = value;
    }

    /// <summary>
    /// Mobius <c>baseMoveSpd/slowSwim</c> (<c>CreatureTemplate.java:148</c>, falls back to <c>baseWalkSpd</c>).
    /// </summary>
    public float BaseSwimWalkSpeed
    {
        get => Move?.BaseSwimWalkSpeed ?? 0;
        set => EnsureMove().BaseSwimWalkSpeed = value;
    }

    /// <summary>Live swim run speed (Mobius <c>getSwimRunSpeed()</c>); recomputed with the land speeds.</summary>
    public float SwimRunSpeed
    {
        get => Move?.LiveSwimRunSpeed ?? (BaseSwimRunSpeed > 0 ? BaseSwimRunSpeed : BaseRunSpeed);
        set
        {
            EnsureMove().BaseSwimRunSpeed = value;
            EnsureMove().LiveSwimRunSpeed = value;
        }
    }

    /// <summary>Live swim walk speed (Mobius <c>getSwimWalkSpeed()</c>).</summary>
    public float SwimWalkSpeed
    {
        get => Move?.LiveSwimWalkSpeed ?? (BaseSwimWalkSpeed > 0 ? BaseSwimWalkSpeed : BaseWalkSpeed);
        set
        {
            EnsureMove().BaseSwimWalkSpeed = value;
            EnsureMove().LiveSwimWalkSpeed = value;
        }
    }

    /// <summary>Sets the live swim speeds without touching the base template values.</summary>
    internal void SetLiveSwimSpeed(float run, float walk)
    {
        EnsureMove().LiveSwimRunSpeed = run;
        EnsureMove().LiveSwimWalkSpeed = walk;
    }

    public bool Running
    {
        get => Move?.Running ?? true;
        set => EnsureMove().Running = value;
    }

    /// <summary>Mobius isInsideZone(WATER): last water state seen by the mover (swim speed while true).</summary>
    public bool InWater
    {
        get => Move?.InWater ?? false;
        set => EnsureMove().InWater = value;
    }

    /// <summary>Mobius MoveData: server-side move in progress (Net path). Position advances by speed each tick.</summary>
    public bool MoveActive
    {
        get => Move?.MoveActive ?? false;
        set => EnsureMove().MoveActive = value;
    }

    public int MoveDestX
    {
        get => Move?.MoveDestX ?? 0;
        set => EnsureMove().MoveDestX = value;
    }

    public int MoveDestY
    {
        get => Move?.MoveDestY ?? 0;
        set => EnsureMove().MoveDestY = value;
    }

    public int MoveDestZ
    {
        get => Move?.MoveDestZ ?? 0;
        set => EnsureMove().MoveDestZ = value;
    }

    public long MoveLastTickMs
    {
        get => Move?.MoveLastTickMs ?? 0;
        set => EnsureMove().MoveLastTickMs = value;
    }

    /// <summary>Mobius MoveData.geoPath: remaining geo-path waypoints after the current segment.</summary>
    public Queue<(int X, int Y, int Z)> MovePath => EnsureMove().MovePath;

    /// <summary>Segment switched to the next waypoint inside AdvanceMove; tick broadcasts MoveToLocation for it.</summary>
    public bool MoveSegmentDirty
    {
        get => Move?.MoveSegmentDirty ?? false;
        set => EnsureMove().MoveSegmentDirty = value;
    }

    /// <summary>Position of the last in-move knownlist refresh (Mobius region/knownlist revalidate while running).</summary>
    public int AoiRefreshX
    {
        get => Move?.AoiRefreshX ?? 0;
        set => EnsureMove().AoiRefreshX = value;
    }

    public int AoiRefreshY
    {
        get => Move?.AoiRefreshY ?? 0;
        set => EnsureMove().AoiRefreshY = value;
    }

    ZoneEntityMovePayload EnsureMove() => Move ??= new ZoneEntityMovePayload();

    public ZoneSession? Session { get; init; }
    public PlayerInventory? Inventory { get; init; }
    /// <summary>
    /// Player is dead. Equip and unequip are rejected. Combat sets this; spawn leaves it false.
    /// CurHp is not the signal: a living player can sit at low HP while regen heals them.
    /// </summary>
    public bool Dead
    {
        get => Vitals?.Dead ?? false;
        set => EnsureVitals().Dead = value;
    }
    /// <summary>Scroll object id selected for the enchant window. 0 when the window is closed.</summary>
    public int ActiveEnchantObjectId
    {
        get => Combat?.ActiveEnchantObjectId ?? 0;
        set => EnsureCombat().ActiveEnchantObjectId = value;
    }
    /// <summary>Ground-drop payload; null unless <see cref="Kind"/> is GroundItem (#353 composition).</summary>
    public ZoneGroundItemPayload? Ground { get; set; }

    public int GroundItemId
    {
        get => Ground?.ItemId ?? 0;
        set => EnsureGround().ItemId = value;
    }

    public int GroundCount
    {
        get => Ground?.Count ?? 1;
        set => EnsureGround().Count = value;
    }

    public bool GroundStackable
    {
        get => Ground?.Stackable ?? false;
        set => EnsureGround().Stackable = value;
    }

    ZoneGroundItemPayload EnsureGround() => Ground ??= new ZoneGroundItemPayload();
    /// <summary>Mobius DropProtection owner (killer); 0 = anyone may pick up.</summary>
    public int GroundOwnerId
    {
        get => Ground?.OwnerId ?? 0;
        set => EnsureGround().OwnerId = value;
    }

    /// <summary>Unix ms until only <see cref="GroundOwnerId"/> (or their party) may pick up.</summary>
    public long GroundProtectUntilMs
    {
        get => Ground?.ProtectUntilMs ?? 0;
        set => EnsureGround().ProtectUntilMs = value;
    }

    /// <summary>Mobius ItemsAutoDestroy: unix ms when the ground item is removed; 0 = never.</summary>
    public long GroundExpireAtMs
    {
        get => Ground?.ExpireAtMs ?? 0;
        set => EnsureGround().ExpireAtMs = value;
    }
    /// <summary>NPC spoil/sweep payload; null until first spoil access (#353 composition).</summary>
    public ZoneNpcSpoilPayload? Spoil { get; set; }

    /// <summary>Mobius spoil: who activated spoil on this attackable (0 = none).</summary>
    public int SpoilerObjectId
    {
        get => Spoil?.SpoilerObjectId ?? 0;
        set => EnsureSpoil().SpoilerObjectId = value;
    }

    /// <summary>Pre-rolled corpse drops after death when spoiled; cleared by sweeper.</summary>
    public List<Drops.DropStack> SweepItems => EnsureSpoil().SweepItems;

    /// <summary>Dead NPC kept in world until sweeper takes <see cref="SweepItems"/>.</summary>
    public bool CorpseAwaitingSweep
    {
        get => Spoil?.CorpseAwaitingSweep ?? false;
        set => EnsureSpoil().CorpseAwaitingSweep = value;
    }

    ZoneNpcSpoilPayload EnsureSpoil() => Spoil ??= new ZoneNpcSpoilPayload();
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
