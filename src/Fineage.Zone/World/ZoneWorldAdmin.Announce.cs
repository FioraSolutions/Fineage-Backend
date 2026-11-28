namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryAdminAnnounce(ZoneSession session, ZoneEntity gm, string verb, string args)
    {
        string text = args.Trim();
        if (text.Length == 0)
        {
            FanGmSystem(session, "Syntax: //announce <text to announce here>");
            return "bad_request";
        }

        bool critical = verb.Equals("admin_announce_crit", StringComparison.OrdinalIgnoreCase);
        // Mobius Config.GM_ANNOUNCER_NAME default true in many packs — append GM name for auditability.
        string line = $"{text} [{gm.Name}]";
        AnnounceToAll(line, critical);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, critical ? "announce_crit" : "announce", "", text);
        return null;
    }
}
