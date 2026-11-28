namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryAdminStatus(ZoneSession session, ZoneEntity gm, string verb, string args)
    {
        if (verb.Equals("admin_online", StringComparison.OrdinalIgnoreCase))
            return AdminOnline(session, gm);
        if (verb.Equals("admin_invul", StringComparison.OrdinalIgnoreCase))
            return AdminToggleInvul(session, gm, gm);
        if (verb.Equals("admin_setinvul", StringComparison.OrdinalIgnoreCase))
        {
            int targetId = gm.Session?.SelectedTargetId ?? 0;
            if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || target.Kind != ZoneEntityKind.Player)
            {
                FanGmSystem(session, "Incorrect target.");
                return "bad_request";
            }

            return AdminToggleInvul(session, gm, target);
        }

        if (verb.Equals("admin_hide", StringComparison.OrdinalIgnoreCase))
            return AdminHide(session, gm, args);
        if (verb.Equals("admin_vis", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_visible", StringComparison.OrdinalIgnoreCase))
            return AdminSetInvisible(session, gm, hide: false);
        if (verb.Equals("admin_invis", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_invisible", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_setinvis", StringComparison.OrdinalIgnoreCase))
            return AdminSetInvisible(session, gm, hide: true);

        return null;
    }

    string? AdminOnline(ZoneSession session, ZoneEntity gm)
    {
        int total = 0;
        int combat = 0;
        long now = NowUnixMs();
        foreach (var p in Players())
        {
            if (p.Session?.InWorld != true)
                continue;
            total++;
            if (p.CombatUntilUnixMs > now)
                combat++;
        }

        FanGmSystem(session, "Online Player Report");
        FanGmSystem(session, $"Total count: {total}");
        FanGmSystem(session, $"Total online: {total}");
        FanGmSystem(session, $"In combat: {combat}");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "online", "", $"total={total}");
        return null;
    }

    string? AdminToggleInvul(ZoneSession session, ZoneEntity gm, ZoneEntity target)
    {
        target.Invulnerable = !target.Invulnerable;
        string text = target.Invulnerable
            ? $"{target.Name} is now invulnerable."
            : $"{target.Name} is now mortal.";
        FanGmSystem(session, text);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "invul", target.Name,
            target.Invulnerable ? "on" : "off");
        return null;
    }

    string? AdminHide(ZoneSession session, ZoneEntity gm, string args)
    {
        string param = args.Trim().ToLowerInvariant();
        if (param is not ("on" or "off"))
        {
            FanGmSystem(session, "//hide [on|off]");
            return "bad_request";
        }

        bool hide = param == "on";
        if (gm.Invisible == hide)
        {
            FanGmSystem(session, hide ? "Currently, you cannot be seen." : "Currently, you can be seen.");
            return null;
        }

        return AdminSetInvisible(session, gm, hide);
    }

    string? AdminSetInvisible(ZoneSession session, ZoneEntity gm, bool hide)
    {
        if (gm.Invisible == hide)
        {
            FanGmSystem(session, hide ? "Now, you cannot be seen." : "Now, you can be seen.");
            return null;
        }

        gm.Invisible = hide;
        if (hide)
            DespawnInvisibleFromNonGms(gm);
        else
            AppearAfterInvisible(gm);

        FanGmSystem(session, hide ? "Now, you cannot be seen." : "Now, you can be seen.");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, hide ? "invis" : "vis", gm.Name, hide ? "on" : "off");
        return null;
    }

    void DespawnInvisibleFromNonGms(ZoneEntity gm)
    {
        foreach (var peer in Players())
        {
            if (peer.Session == null || peer.ObjectId == gm.ObjectId)
                continue;
            if (ZoneAdmin.IsAdminCharacter(peer.Name))
                continue;
            if (!peer.Knownlist.Remove(gm.ObjectId))
                continue;
            UntrackWatcher(peer.ObjectId, gm.ObjectId);
            NetBridge?.FanoutKnownlistDiff(peer.ObjectId, [], [gm.ObjectId]);
        }
    }

    void AppearAfterInvisible(ZoneEntity gm)
    {
        var visible = new HashSet<int>();
        CollectVisible(gm, visible);
        var addedPeers = new List<int>();
        foreach (int id in visible)
        {
            if (!_byId.TryGetValue(id, out var other) || other.Session == null)
                continue;
            if (other.Knownlist.Contains(gm.ObjectId))
                continue;
            other.Knownlist.Add(gm.ObjectId);
            TrackWatcher(other.ObjectId, gm.ObjectId);
            if (!gm.Knownlist.Contains(id))
            {
                gm.Knownlist.Add(id);
                TrackWatcher(gm.ObjectId, id);
            }

            NetBridge?.FanoutKnownlistDiff(other.ObjectId, [gm.ObjectId], []);
            addedPeers.Add(id);
        }

        if (addedPeers.Count > 0)
            NetBridge?.FanoutKnownlistDiff(gm.ObjectId, addedPeers, []);
    }
}
