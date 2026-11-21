using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

/// <summary>
/// Riding after Mobius <c>RequestActionUse</c> 38 and <c>Ride</c>: mount a summoned mountable pet (strider),
/// dismount, peace / siege gates, unsummon-on-mount with pet state stored on the collar.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly Lazy<MountRules> _mountRules = new(MountRules.LoadDefault);

    internal MountRules MountRules => _mountRules.Value;

    public const int SysMsgCannotFishWhileMount = 592;

    /// <summary>Mobius RequestActionUse 38.</summary>
    string? TryRideAction(ZoneSession session, ZoneEntity player)
    {
        if (player.Mount != null)
        {
            Dismount(player);
            return null;
        }
        if (PetEntityOf(player) is not { Summon.Pet: { } pet } summon)
            return null;
        if (!pet.Template.Mountable)
            return MountProtocol.Blocked;
        return TryMountFromPet(session, player, summon, pet);
    }

    string? TryMountFromPet(ZoneSession session, ZoneEntity player, ZoneEntity summon, PetState pet)
    {
        long now = NowUnixMs();
        if (player.Dead)
        {
            SelfLine(session, MountProtocol.SysMsgStriderDeadRider, "A strider cannot be ridden while dead.");
            return MountProtocol.Blocked;
        }
        if (summon.Dead || summon.CurHp <= 0)
        {
            SelfLine(session, MountProtocol.SysMsgDeadStrider, "A dead strider cannot be ridden.");
            return MountProtocol.Blocked;
        }
        if (pet.Fed <= 0)
            return MountProtocol.Blocked;
        if (summon.CombatUntilUnixMs > now)
        {
            SelfLine(session, MountProtocol.SysMsgStriderInBattle, "A strider in battle cannot be ridden.");
            return MountProtocol.Blocked;
        }
        if (player.CombatUntilUnixMs > now)
        {
            SelfLine(session, MountProtocol.SysMsgRiderInBattle, "A strider cannot be ridden while in battle.");
            return MountProtocol.Blocked;
        }
        if (player.Sitting)
        {
            SelfLine(session, MountProtocol.SysMsgMountWhileStanding, "A strider can be ridden only when standing.");
            return MountProtocol.Blocked;
        }
        if (player.Fishing)
        {
            SelfLine(session, SysMsgCannotFishWhileMount, "You can't mount, dismount, break and drop items while fishing.");
            return MountProtocol.Blocked;
        }
        if (Peace.Contains(player.X, player.Y, player.Z))
            return MountProtocol.Blocked;
        if (ActiveSiegeCastle(player.X, player.Y, player.Z) > 0)
            return MountProtocol.Blocked;
        if (!DisarmWeaponsForMount(player))
            return MountProtocol.Blocked;

        StorePet(player, summon);
        int collarId = pet.ControlObjectId;
        UnsummonPet(player, summon, 0);

        _summonTemplates ??= NpcCatalog.LoadDefault();
        if (!_summonTemplates.ById.TryGetValue(pet.Template.NpcId, out var npc))
            throw new InvalidOperationException($"mount npc missing from npc catalog: pet npc id {pet.Template.NpcId}");

        ApplyMount(player, pet.Template.MountKind, pet.Template.NpcId, collarId, wyvernRent: false, npc.RunSpeed, npc.WalkSpeed);
        return null;
    }

    /// <summary>Wyvern manager bypass (castle lord): mount wyvern display npc from <see cref="MountRules"/>.</summary>
    internal void MountWyvernFromManager(ZoneEntity player, ZoneEntity striderSummon, PetState pet)
    {
        if (player.Session is not { } session)
            return;
        StorePet(player, striderSummon);
        int collarId = pet.ControlObjectId;
        UnsummonPet(player, striderSummon, 0);
        var rules = MountRules;
        _summonTemplates ??= NpcCatalog.LoadDefault();
        if (!_summonTemplates.ById.TryGetValue(rules.WyvernDisplayNpcId, out var npc))
            throw new InvalidOperationException($"wyvern display npc missing from npc catalog: id {rules.WyvernDisplayNpcId}");
        ApplyMount(player, PetMountKind.Wyvern, rules.WyvernDisplayNpcId, collarId, wyvernRent: true, npc.RunSpeed, npc.WalkSpeed);
    }

    void ApplyMount(ZoneEntity player, PetMountKind kind, int npcId, int controlObjectId, bool wyvernRent, float run, float walk)
    {
        player.Mount = new MountState
        {
            Kind = kind,
            NpcId = npcId,
            ControlObjectId = controlObjectId,
            WyvernRent = wyvernRent,
            RunSpeed = run,
            WalkSpeed = walk
        };
        player.SetLiveRunSpeed(run);
        player.SetLiveWalkSpeed(walk);
        FanoutRide(player, MountProtocol.RideMount, kind, npcId);
        RefreshRunSpeedAndFanout(player);
        SendMountActionState(player);
    }

    void Dismount(ZoneEntity player)
    {
        if (player.Mount == null)
            return;
        bool wyvernRent = player.Mount.WyvernRent;
        player.Mount = null;
        RefreshRunSpeed(player);
        FanoutRide(player, MountProtocol.RideDismount, PetMountKind.None, 0);
        RefreshRunSpeedAndFanout(player);
        if (wyvernRent)
            RevokeTemporaryWyvernSkill(player);
        SendMountActionState(player);
    }

    void FanoutRide(ZoneEntity player, int action, PetMountKind kind, int npcId)
    {
        if (NetBridge == null)
            return;
        var watchers = KnownNetPlayersOf(player);
        if (watchers.Count == 0)
            return;
        NetBridge.FanoutRide(watchers, new RideNetMsg
        {
            ObjectId = player.ObjectId,
            Action = action,
            MountKind = (byte)kind,
            RideNpcId = action == MountProtocol.RideMount ? npcId + MountProtocol.RideNpcOffset : 0
        });
    }

    bool DisarmWeaponsForMount(ZoneEntity player)
    {
        if (player.Inventory is not { } bag)
            return true;
        bool any = false;
        foreach (var item in bag.Items.Where(i => i.IsEquipped).ToArray())
        {
            if (item.Template.WeaponType == WeaponType.None)
                continue;
            bag.Unequip(item);
            any = true;
        }
        if (!any || player.Session is not { } session)
            return true;
        SelfUserInfo(player);
        SaveInventory(session, bag);
        return true;
    }

    internal bool BlocksAttackWhileMounted(ZoneEntity player) =>
        player.Mount?.Kind == PetMountKind.Wyvern;

    /// <summary>Drop wyvern rent on teleport/death/logout.</summary>
    void ClearMount(ZoneEntity player, bool dismountBroadcast = true)
    {
        if (player.Mount == null)
            return;
        bool wyvernRent = player.Mount.WyvernRent;
        player.Mount = null;
        RefreshRunSpeed(player);
        if (dismountBroadcast)
            FanoutRide(player, MountProtocol.RideDismount, PetMountKind.None, 0);
        RefreshRunSpeedAndFanout(player);
        if (wyvernRent)
            RevokeTemporaryWyvernSkill(player);
        SendMountActionState(player);
    }
}