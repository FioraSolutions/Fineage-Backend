using Fineage.Game.Services;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius AdminRide pet ids.</summary>
    const int AdminRideWyvernNpcId = 12621;
    const int AdminRideStriderNpcId = 12526;

    static bool IsAdminRideVerb(string verb) =>
        verb.Equals("admin_ride_wyvern", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_ride_strider", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_unride_wyvern", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_unride_strider", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_unride", StringComparison.OrdinalIgnoreCase);

    string? TryAdminRide(ZoneSession session, ZoneEntity gm, string verb, string args)
    {
        ZoneEntity rider = ResolveAdminRideTarget(gm);
        if (verb.StartsWith("admin_ride", StringComparison.OrdinalIgnoreCase))
            return AdminRide(session, gm, rider, wyvern: verb.Contains("wyvern", StringComparison.OrdinalIgnoreCase));
        return AdminUnride(session, gm, rider);
    }

    ZoneEntity ResolveAdminRideTarget(ZoneEntity gm)
    {
        int targetId = gm.Session?.SelectedTargetId ?? 0;
        if (targetId > 0
            && targetId != gm.ObjectId
            && _byId.TryGetValue(targetId, out var target)
            && target.Kind == ZoneEntityKind.Player)
            return target;
        return gm;
    }

    string? AdminRide(ZoneSession session, ZoneEntity gm, ZoneEntity rider, bool wyvern)
    {
        if (rider.IsMounted || rider.Summon != null)
        {
            FanGmSystem(session, "Target already have a summon.");
            return "bad_request";
        }

        int npcId = wyvern ? AdminRideWyvernNpcId : AdminRideStriderNpcId;
        var kind = wyvern ? PetMountKind.Wyvern : PetMountKind.Strider;
        _summonTemplates ??= Fineage.Zone.Npcs.NpcCatalog.LoadDefault();
        if (!_summonTemplates.ById.TryGetValue(npcId, out var template))
        {
            // Fall back to configured wyvern display npc when the retail ride id is absent from npcs.json.
            if (wyvern)
            {
                npcId = MountRules.WyvernDisplayNpcId;
                template = _summonTemplates.Get(npcId);
            }
            else
            {
                ZoneLog.Error($"admin ride missing npc template id={npcId}");
                FanGmSystem(session, $"Ride NPC template {npcId} is missing from npcs.json.");
                return "bad_request";
            }
        }

        ApplyMount(rider, kind, npcId, controlObjectId: 0, wyvernRent: true, template.RunSpeed, template.WalkSpeed);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, wyvern ? "ride_wyvern" : "ride_strider",
            rider.Name, $"npc={npcId}");
        FanGmSystem(session, $"{rider.Name} is now riding.");
        return null;
    }

    string? AdminUnride(ZoneSession session, ZoneEntity gm, ZoneEntity rider)
    {
        if (!rider.IsMounted)
        {
            FanGmSystem(session, "Target is not mounted.");
            return null;
        }

        Dismount(rider);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "unride", rider.Name);
        FanGmSystem(session, $"{rider.Name} dismounted.");
        return null;
    }
}
