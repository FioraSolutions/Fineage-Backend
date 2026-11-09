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
