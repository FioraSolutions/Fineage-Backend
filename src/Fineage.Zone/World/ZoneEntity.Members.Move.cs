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
    public ZoneEntityMovePayload? Move { get; set; }

    /// <summary>
    /// Sub-unit remainder of the timed move (L2 units). Rounding each tick's step on its own made a pawn
    /// faster than its speed (e.g. 12.6u/tick → 13), so server positions ran ahead of what peers extrapolate.
    /// </summary>
    internal double MoveCarryX, MoveCarryY;

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

    /// <summary>Mobius <c>ZoneId.SWAMP</c>: <see cref="MoveSpeedOf"/> multiplies live run/walk by <see cref="SwampMoveBonus"/>.</summary>
    public bool InSwamp
    {
        get => Move?.InSwamp ?? false;
        set => EnsureMove().InSwamp = value;
    }

    public double SwampMoveBonus
    {
        get => Move?.SwampMoveBonus ?? 1d;
        set => EnsureMove().SwampMoveBonus = value;
    }

    public long DamageZoneNextAtMs
    {
        get => Move?.DamageZoneNextAtMs ?? 0;
        set => EnsureMove().DamageZoneNextAtMs = value;
    }

    public long NoLandingDismountAtMs
    {
        get => Move?.NoLandingDismountAtMs ?? 0;
        set => EnsureMove().NoLandingDismountAtMs = value;
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

    public long AoiRefreshCell
    {
        get => Move?.AoiRefreshCell ?? 0;
        set => EnsureMove().AoiRefreshCell = value;
    }

    public long MoveGeoHeightCell
    {
        get => Move?.MoveGeoHeightCell ?? 0;
        set => EnsureMove().MoveGeoHeightCell = value;
    }

    public bool PathfindPending
    {
        get => Move?.PathfindPending ?? false;
        set => EnsureMove().PathfindPending = value;
    }

    public int PathWantX
    {
        get => Move?.PathWantX ?? 0;
        set => EnsureMove().PathWantX = value;
    }

    public int PathWantY
    {
        get => Move?.PathWantY ?? 0;
        set => EnsureMove().PathWantY = value;
    }

    public int PathWantZ
    {
        get => Move?.PathWantZ ?? 0;
        set => EnsureMove().PathWantZ = value;
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

    public bool AugmentCancelWindow
    {
        get => Combat?.AugmentCancelWindow ?? false;
        set => EnsureCombat().AugmentCancelWindow = value;
    }

    public int AugmentTargetObjectId
    {
        get => Combat?.AugmentTargetObjectId ?? 0;
        set => EnsureCombat().AugmentTargetObjectId = value;
    }

    public int AugmentRefinerObjectId
    {
        get => Combat?.AugmentRefinerObjectId ?? 0;
        set => EnsureCombat().AugmentRefinerObjectId = value;
    }

    public int AugmentGemstoneObjectId
    {
        get => Combat?.AugmentGemstoneObjectId ?? 0;
        set => EnsureCombat().AugmentGemstoneObjectId = value;
    }

    public long AugmentGemstoneCount
    {
        get => Combat?.AugmentGemstoneCount ?? 0;
        set => EnsureCombat().AugmentGemstoneCount = value;
    }
    /// <summary>Ground-drop payload; null unless <see cref="Kind"/> is GroundItem (#353 composition).</summary>
}
