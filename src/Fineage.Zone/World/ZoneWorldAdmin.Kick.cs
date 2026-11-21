namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? AdminKick(ZoneSession gmSession, ZoneEntity gm, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "bad_request";

        string targetName = args.Trim();
        var target = FindOnlinePlayer(targetName);
        if (target == null)
        {
            ZoneLog.Info($"admin kick miss object={gm.ObjectId} target={targetName}");
            FanGmSystem(gmSession, "That player is not online.");
            return "bad_request";
        }

        if (target.ObjectId == gm.ObjectId)
        {
            FanGmSystem(gmSession, "You cannot kick yourself.");
            return "bad_request";
        }

        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "kick", target.Name);
        DisconnectOnlinePlayer(
          target.ObjectId,
          $"admin kick object={target.ObjectId} by={gm.Name} gm={gm.ObjectId}");
        FanGmSystem(gmSession, $"Kicked {target.Name}.");
        ZoneLog.Info($"admin kick ok gm={gm.ObjectId} target={target.ObjectId}");
        return null;
    }
}
