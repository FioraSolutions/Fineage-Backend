namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static bool IsAdminCombatVerb(string verb) =>
        verb.Equals("admin_heal", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_kill", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_kill_monster", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_res", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_res_monster", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_delete", StringComparison.OrdinalIgnoreCase);

    string? TryAdminCombat(ZoneSession session, ZoneEntity gm, string verb, string args)
    {
        if (verb.Equals("admin_heal", StringComparison.OrdinalIgnoreCase))
            return AdminHeal(session, gm, args);
        if (verb.Equals("admin_kill", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_kill_monster", StringComparison.OrdinalIgnoreCase))
            return AdminKill(session, gm, args, monstersOnly: verb.Equals("admin_kill_monster", StringComparison.OrdinalIgnoreCase));
        if (verb.Equals("admin_res", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("admin_res_monster", StringComparison.OrdinalIgnoreCase))
            return AdminRes(session, gm, args, monstersOnly: verb.Equals("admin_res_monster", StringComparison.OrdinalIgnoreCase));
        if (verb.Equals("admin_delete", StringComparison.OrdinalIgnoreCase))
            return AdminDelete(session, gm, args);
        return null;
    }

    string? AdminHeal(ZoneSession session, ZoneEntity gm, string args)
    {
        string trimmed = args.Trim();
        if (trimmed.Length > 0 && int.TryParse(trimmed, out int radius) && radius > 0)
        {
            int healed = 0;
            foreach (var e in _byId.Values)
            {
                if (e.Kind is not (ZoneEntityKind.Player or ZoneEntityKind.Npc))
                    continue;
                if (!AdminWithinRadius(gm, e, radius))
                    continue;
                HealCreature(e);
                healed++;
            }

            _punishments.RecordGmAction(gm.Name, gm.ObjectId, "heal", $"radius={radius}", $"count={healed}");
            FanGmSystem(session, $"Healed within {radius} unit radius ({healed}).");
            return null;
        }

        ZoneEntity? target = ResolveAdminCreatureTarget(gm, trimmed, allowSelf: true);
        if (target == null)
        {
            FanGmSystem(session, "Incorrect target/radius specified.");
            return "bad_request";
        }

        HealCreature(target);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "heal", target.Name, $"oid={target.ObjectId}");
        FanGmSystem(session, $"Healed {target.Name}.");
        return null;
    }

    void HealCreature(ZoneEntity target)
    {
        target.CurHp = target.MaxHp;
        target.CurMp = target.MaxMp;
        if (target.Kind == ZoneEntityKind.Player)
            target.CurCp = target.MaxCp;
        FanoutVitals(target, target.Kind == ZoneEntityKind.Player ? PlayerVitalsStatus(target) : StatusFor(target));
        if (target.Session != null)
            SaveVitals(target.Session, target);
    }

    string? AdminKill(ZoneSession session, ZoneEntity gm, string args, bool monstersOnly)
    {
        string trimmed = args.Trim();
        if (trimmed.Length > 0 && int.TryParse(trimmed, out int radius) && radius > 0
            && FindOnlinePlayer(trimmed) == null)
        {
            int killed = 0;
            foreach (var e in _byId.Values.ToArray())
            {
                if (ReferenceEquals(e, gm))
                    continue;
                if (!AdminWithinRadius(gm, e, radius))
                    continue;
                if (monstersOnly)
                {
                    if (e.Kind != ZoneEntityKind.Npc || e.Dead)
                        continue;
                    ForceKillNpc(e, gm);
                    killed++;
                }
                else if (e.Kind == ZoneEntityKind.Player && !e.Dead)
                {
                    ApplyPlayerDeath(e, gm.ObjectId);
                    killed++;
                }
                else if (e.Kind == ZoneEntityKind.Npc && !e.Dead)
                {
                    ForceKillNpc(e, gm);
                    killed++;
                }
            }

            _punishments.RecordGmAction(gm.Name, gm.ObjectId, monstersOnly ? "kill_monster" : "kill",
                $"radius={radius}", $"count={killed}");
            FanGmSystem(session, $"Killed all characters within a {radius} unit radius ({killed}).");
            return null;
        }

        ZoneEntity? target = ResolveAdminCreatureTarget(gm, trimmed, allowSelf: false);
        if (target == null || (monstersOnly && target.Kind == ZoneEntityKind.Player))
        {
            FanGmSystem(session, "Invalid target.");
            return "bad_request";
        }

        if (target.Kind == ZoneEntityKind.Player)
        {
            if (target.Dead)
                return null;
            // Admin kill bypasses Invulnerable (Mobius reduceCurrentHp after stopping invul effects).
            target.Invulnerable = false;
            ApplyPlayerDeath(target, gm.ObjectId);
        }
        else if (target.Kind == ZoneEntityKind.Npc)
        {
            if (target.Dead)
                return null;
            ForceKillNpc(target, gm);
        }
        else
        {
            FanGmSystem(session, "Invalid target.");
            return "bad_request";
        }

        _punishments.RecordGmAction(gm.Name, gm.ObjectId, monstersOnly ? "kill_monster" : "kill",
            target.Name, $"oid={target.ObjectId}");
        FanGmSystem(session, $"Killed {target.Name}.");
        return null;
    }

    void ForceKillNpc(ZoneEntity npc, ZoneEntity killer) => RemoveDeadNpc(npc, killer);

    string? AdminRes(ZoneSession session, ZoneEntity gm, string args, bool monstersOnly)
    {
        string trimmed = args.Trim();
        if (trimmed.Length > 0 && int.TryParse(trimmed, out int radius) && radius > 0
            && FindOnlinePlayer(trimmed) == null)
        {
            int count = 0;
            foreach (var e in _byId.Values)
            {
                if (!AdminWithinRadius(gm, e, radius))
                    continue;
                if (monstersOnly)
                {
                    if (e.Kind != ZoneEntityKind.Npc || !e.Dead)
                        continue;
                    // Interlude corpses are despawned; radius res of monsters is a no-op once decayed.
                    continue;
                }

                if (e.Kind != ZoneEntityKind.Player || !e.Dead)
                    continue;
                AdminRevivePlayer(e);
                count++;
            }

            _punishments.RecordGmAction(gm.Name, gm.ObjectId, monstersOnly ? "res_monster" : "res",
                $"radius={radius}", $"count={count}");
            FanGmSystem(session,
                monstersOnly
                    ? $"Resurrected all non-players within a {radius} unit radius."
                    : $"Resurrected all players within a {radius} unit radius ({count}).");
            return null;
        }

        ZoneEntity? target = ResolveAdminCreatureTarget(gm, trimmed, allowSelf: true);
        if (target == null)
        {
            FanGmSystem(session, "Enter a valid player name or radius.");
            return "bad_request";
        }

        if (monstersOnly)
        {
            if (target.Kind == ZoneEntityKind.Player || !target.Dead)
            {
                FanGmSystem(session, "Incorrect target.");
                return "bad_request";
            }

            FanGmSystem(session, "Monster corpses cannot be resurrected after decay.");
            return "bad_request";
        }

        if (target.Kind != ZoneEntityKind.Player)
        {
            FanGmSystem(session, "Incorrect target.");
            return "bad_request";
        }

        if (!target.Dead)
            return null;

        AdminRevivePlayer(target);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "res", target.Name, $"oid={target.ObjectId}");
        FanGmSystem(session, $"Resurrected {target.Name}.");
        return null;
    }

    void AdminRevivePlayer(ZoneEntity player)
    {
        ClearReviveOffer(player);
        // Mobius admin_res: doRevive + restoreExp(100).
        player.Dead = false;
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        player.CurCp = player.MaxCp;
        player.Running = true;
        RestoreExpFromDeath(player, 100.0);
        FanoutRevive(player);
        if (player.Session != null)
        {
            SelfUserInfo(player);
            FanoutVitals(player, PlayerVitalsStatus(player));
            SaveVitals(player.Session, player);
        }
    }

    string? AdminDelete(ZoneSession session, ZoneEntity gm, string args)
    {
        string trimmed = args.Trim();
        if (trimmed.Length > 0 && int.TryParse(trimmed, out int radius) && radius > 0)
        {
            int deleted = 0;
            foreach (var e in _byId.Values.ToArray())
            {
                if (e.Kind != ZoneEntityKind.Npc)
                    continue;
                if (!AdminWithinRadius(gm, e, radius))
                    continue;
                DespawnNpc(e);
                deleted++;
            }

            _punishments.RecordGmAction(gm.Name, gm.ObjectId, "delete", $"radius={radius}", $"count={deleted}");
            FanGmSystem(session, $"Deleted {deleted} NPC(s) within {radius}.");
            return null;
        }

        int targetId = gm.Session?.SelectedTargetId ?? 0;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || target.Kind != ZoneEntityKind.Npc)
        {
            FanGmSystem(session, "Incorrect target.");
            return "bad_request";
        }

        string name = target.Name;
        int oid = target.ObjectId;
        DespawnNpc(target);
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "delete", name, $"oid={oid}");
        FanGmSystem(session, $"Deleted {name}.");
        return null;
    }

    ZoneEntity? ResolveAdminCreatureTarget(ZoneEntity gm, string nameOrEmpty, bool allowSelf)
    {
        if (!string.IsNullOrWhiteSpace(nameOrEmpty))
        {
            var named = FindOnlinePlayer(nameOrEmpty.Trim());
            if (named != null)
                return named;
            return null;
        }

        int targetId = gm.Session?.SelectedTargetId ?? 0;
        if (targetId > 0 && _byId.TryGetValue(targetId, out var selected)
            && selected.Kind is ZoneEntityKind.Player or ZoneEntityKind.Npc)
            return selected;

        return allowSelf ? gm : null;
    }

    static bool AdminWithinRadius(ZoneEntity origin, ZoneEntity other, int radius)
    {
        long dx = origin.X - other.X;
        long dy = origin.Y - other.Y;
        return dx * dx + dy * dy <= (long)radius * radius;
    }
}
