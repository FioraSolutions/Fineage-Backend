using Fineage.Game.Services;
using Fineage.Net.Contract;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void SendMountActionState(ZoneEntity player) => SendPlayerActionState(player);

    internal bool TryBuildPlayerActionStateNet(ZoneEntity player, out PlayerActionStateNetMsg msg)
    {
        msg = BuildPlayerActionState(player);
        return true;
    }

    void SendPlayerActionState(ZoneEntity player)
    {
        if (NetBridge == null || player.Session == null)
            return;
        var msg = BuildPlayerActionState(player);
        NetBridge.FanoutPlayerActionState([player.ObjectId], msg);
    }

    internal PlayerActionStateNetMsg BuildPlayerActionState(ZoneEntity player)
    {
        long now = NowUnixMs();
        var summon = PetEntityOf(player);
        var pet = summon?.Summon?.Pet;
        bool petOut = summon != null && !summon.Dead && summon.CurHp > 0;
        bool mountable = pet?.Template.Mountable == true;
        bool mounted = player.Mount != null;
        bool canRide = petOut && mountable && pet!.Fed > 0 && !mounted && !player.Dead && !player.Sitting &&
                       player.CombatUntilUnixMs <= now && (summon!.CombatUntilUnixMs <= now) &&
                       !player.Fishing && !Peace.Contains(player.X, player.Y, player.Z) &&
                       ActiveSiegeCastle(player.X, player.Y, player.Z) == 0;
        return new PlayerActionStateNetMsg
        {
            PetOut = petOut,
            MountablePet = mountable && petOut,
            Mounted = mounted,
            CanRide = canRide,
            MountKind = (byte)(player.Mount?.Kind ?? PetMountKind.None),
            PetControlObjectId = pet?.ControlObjectId ?? 0
        };
    }
}