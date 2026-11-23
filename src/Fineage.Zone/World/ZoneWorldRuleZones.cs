using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Geo.ZoneTypes;

namespace Fineage.Zone.World;

/// <summary>
/// Applies the zone-type registry: store, summon, restart, script, town respawn, tax, bookmark,
/// item-drop, derby, effect pulses, residence oust, and boss membership.
/// </summary>
public sealed partial class ZoneWorld
{
    internal ZoneTypeRegistry ZoneRules { get; set; } = ZoneTypeRegistry.Empty;

    readonly Dictionary<int, Dictionary<string, long>> _effectZoneNext = [];
    readonly Dictionary<int, string> _scriptZoneByPlayer = [];
    readonly Dictionary<int, long> _hallOustSince = [];
    readonly HashSet<int> _dangerPlayers = [];
    readonly HashSet<int> _missingEffectSkills = [];
    long _ruleZoneEpochMs;

    void InitRuleZones()
    {
        ZoneRules = ZoneTypeRegistry.SharedDefault;
        _ruleZoneEpochMs = NowUnixMs();
    }

    void ForgetRuleZoneState(int objectId)
    {
        _effectZoneNext.Remove(objectId);
        _scriptZoneByPlayer.Remove(objectId);
        _hallOustSince.Remove(objectId);
        _dangerPlayers.Remove(objectId);
    }

    void UpdateRuleZoneState(ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        var flags = ZoneRules.Query(entity.X, entity.Y, entity.Z, entity.Race);
        string script = flags.ScriptZone ?? "";
        _scriptZoneByPlayer.TryGetValue(entity.ObjectId, out var previous);
        previous ??= "";
        if (!string.Equals(previous, script, StringComparison.Ordinal))
        {
            if (previous.Length > 0)
                ZoneLog.Info($"[zone] script exit obj={entity.ObjectId} name={previous}");
            if (script.Length > 0)
                ZoneLog.Info($"[zone] script enter obj={entity.ObjectId} name={script}");
            if (script.Length == 0)
                _scriptZoneByPlayer.Remove(entity.ObjectId);
            else
                _scriptZoneByPlayer[entity.ObjectId] = script;
        }

        bool danger = flags.InDanger;
        bool was = _dangerPlayers.Contains(entity.ObjectId);
        if (danger == was)
            return;
        if (danger)
            _dangerPlayers.Add(entity.ObjectId);
        else
            _dangerPlayers.Remove(entity.ObjectId);
        FanoutNetEtcStatus(entity);
    }

    internal bool InDangerArea(ZoneEntity player) => _dangerPlayers.Contains(player.ObjectId);

    internal string? BookmarkUseError(ZoneEntity player)
    {
        if (!ZoneRules.Query(player.X, player.Y, player.Z, player.Race).BlocksBookmark)
            return null;
        ZoneLog.Info($"[zone] bookmark refused obj={player.ObjectId} at {player.X},{player.Y},{player.Z}");
        return "bookmark_zone";
    }

    void ApplyNoRestartOnLogin(ZoneEntity entity, DateTimeOffset? lastAccess)
    {
        var flags = ZoneRules.Query(entity.X, entity.Y, entity.Z, entity.Race);
        if (!flags.BlocksRestart)
            return;
        if (lastAccess is not { } access || access == default)
        {
            ZoneLog.Error($"[zone] NoRestartZone login obj={entity.ObjectId} missing LastAccess");
            return;
        }

        long now = NowUnixMs();
        long offlineMs = now - access.ToUnixTimeMilliseconds();
        long uptimeMs = now - _ruleZoneEpochMs;
        if (flags.RestartTimeMs <= 0 || offlineMs <= flags.RestartTimeMs || uptimeMs <= flags.RestartAllowedMs)
            return;
        var point = MapRegionTown(entity);
        ZoneLog.Info(
            $"[zone] NoRestartZone login oust obj={entity.ObjectId} offlineMs={offlineMs} -> {point.X},{point.Y},{point.Z}");
        Relocate(entity, point.X, point.Y, point.Z);
    }

    (int X, int Y, int Z) TownDestination(ZoneEntity player)
    {
        var flags = ZoneRules.Query(player.X, player.Y, player.Z, player.Race);
        if (flags.RespawnRaceMissing)
            ZoneLog.Error($"[zone] RespawnZone '{flags.RespawnZone}' has no point for race {player.Race}");
        else if (!string.IsNullOrEmpty(flags.RespawnPoint))
        {
            if (ResolveRespawnPoint(flags.RespawnPoint, player) is { } point)
                return point;
            ZoneLog.Error(
                $"[zone] respawn point '{flags.RespawnPoint}' missing from map-regions.json (zone {flags.RespawnZone})");
        }

        return MapRegionTown(player);
    }

    (int X, int Y, int Z)? ResolveRespawnPoint(string pointName, ZoneEntity player)
    {
        var region = _mapRegions.ByName(pointName);
        if (region == null)
            return null;
        string? race = ZoneRaces.Name(player.Race);
        if (race != null && region.BannedRace.TryGetValue(race, out var banned) && !string.IsNullOrEmpty(banned))
        {
            var alt = _mapRegions.ByName(banned);
            if (alt == null)
            {
                ZoneLog.Error($"[zone] respawn banned race {race} point '{banned}' missing from map-regions.json");
                return null;
            }

            region = alt;
        }

        var spawn = region.RandomNormalSpawn(Rng(_respawnRandom, player)) ?? region.NormalSpawn();
        return spawn == null ? null : (spawn.X, spawn.Y, spawn.Z);
    }

    (int X, int Y, int Z) MapRegionTown(ZoneEntity player)
    {
        var point = _mapRegions.TownRespawn(player.X, player.Y, Rng(_respawnRandom, player));
        return (point.X, point.Y, point.Z);
    }

    int TaxCastleAt(int x, int y, int z)
    {
        var flags = ZoneRules.Query(x, y, z, race: 0);
        if (flags.TownTaxCastleId > 0)
            return flags.TownTaxCastleId;
        return flags.TaxCastleId;
    }

    void TickRuleZones(long nowUnixMs)
    {
        var pulses = new List<EffectPulse>();
        var effects = ZoneRules.Profile<EffectZoneProfile>();
        var halls = ZoneRules.Profile<ResidenceHallTeleportZoneProfile>();
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind != ZoneEntityKind.Player || entity.Session == null || entity.CurHp <= 0)
                continue;
            TickEffectZones(entity, effects, pulses, nowUnixMs);
            TickHallOust(entity, halls, nowUnixMs);
        }
    }

    void TickEffectZones(ZoneEntity entity, EffectZoneProfile effects, List<EffectPulse> pulses, long nowUnixMs)
    {
        pulses.Clear();
        effects.Collect(entity.X, entity.Y, entity.Z, pulses);
        if (pulses.Count == 0)
        {
            if (_effectZoneNext.TryGetValue(entity.ObjectId, out var stale) && stale.Count > 0)
                DropLeftEffectZones(entity, effects, stale, stillInside: []);
            return;
        }

        if (!_effectZoneNext.TryGetValue(entity.ObjectId, out var due))
        {
            due = [];
            _effectZoneNext[entity.ObjectId] = due;
        }

        var stillInside = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pulse in pulses)
        {
            stillInside.Add(pulse.ZoneName);
            if (!due.ContainsKey(pulse.ZoneName))
                due[pulse.ZoneName] = nowUnixMs + pulse.InitialDelayMs;
            if (nowUnixMs < due[pulse.ZoneName])
                continue;
            due[pulse.ZoneName] = nowUnixMs + pulse.ReuseMs;
            if (pulse.MinLevel > 0 && entity.Level < pulse.MinLevel)
                continue;
            if (pulse.Chance < 100 && Rng(_serviceRandom, entity).Next(100) >= pulse.Chance)
                continue;
            ApplyEffectPulse(entity, pulse, nowUnixMs);
        }

        DropLeftEffectZones(entity, effects, due, stillInside);
    }

    void DropLeftEffectZones(ZoneEntity entity, EffectZoneProfile effects, Dictionary<string, long> due, HashSet<string> stillInside)
    {
        if (due.Count == 0)
            return;
        var left = new List<string>();
        foreach (var name in due.Keys)
        {
            if (!stillInside.Contains(name))
                left.Add(name);
        }

        foreach (var name in left)
        {
            due.Remove(name);
            var pulse = effects.Zones.FirstOrDefault(p => p.ZoneName == name);
            if (pulse is { RemoveOnExit: true })
                RemoveEffectPulse(entity, pulse);
        }

        if (due.Count == 0)
            _effectZoneNext.Remove(entity.ObjectId);
    }

    void ApplyEffectPulse(ZoneEntity entity, EffectPulse pulse, long nowUnixMs)
    {
        if (entity.Dead)
            return;
        foreach (var skill in pulse.Skills)
        {
            var template = _game.Skills.Find(skill.SkillId);
            if (template == null)
            {
                if (_missingEffectSkills.Add(skill.SkillId))
                    ZoneLog.Error(
                        $"[zone] EffectZone '{pulse.ZoneName}' skillId={skill.SkillId} missing from {SkillCatalog.FileName} — not applied");
                continue;
            }

            if (!template.TryLevelData(skill.Level, out var levelData))
            {
                ZoneLog.Error($"[zone] EffectZone '{pulse.ZoneName}' skillId={skill.SkillId} missing level {skill.Level} — not applied");
                continue;
            }

            if (entity.Abnormals.Any(a => a.Id == skill.SkillId))
                continue;
            ApplyAbnormalFromSkill(template, levelData, entity, nowUnixMs, attacker: entity, force: pulse.BypassConditions || !template.IsDebuff);
        }
    }

    void RemoveEffectPulse(ZoneEntity entity, EffectPulse pulse)
    {
        var ids = pulse.Skills.Select(s => s.SkillId).ToHashSet();
        if (entity.Abnormals.RemoveAll(a => ids.Contains(a.Id)) > 0)
            FanoutAbnormal(entity);
    }

    void TickHallOust(ZoneEntity entity, ResidenceHallTeleportZoneProfile halls, long nowUnixMs)
    {
        if (!halls.TrySpot(entity.X, entity.Y, entity.Z, out var spot) || !spot.Hall)
        {
            _hallOustSince.Remove(entity.ObjectId);
            return;
        }

        var hall = _chSiege.Board.Find(spot.ResidenceId);
        if (hall is not { InProgress: true })
        {
            _hallOustSince.Remove(entity.ObjectId);
            return;
        }

        if (!_hallOustSince.TryGetValue(entity.ObjectId, out long since))
        {
            _hallOustSince[entity.ObjectId] = nowUnixMs;
            return;
        }

        if (nowUnixMs - since < 30_000)
            return;
        if (spot.Spawns.Length == 0)
        {
            ZoneLog.Error($"[zone] ResidenceHallTeleportZone '{spot.ZoneName}' missing spawn — oust not applied");
            return;
        }

        var dest = spot.Spawns[Rng(_serviceRandom, entity).Next(spot.Spawns.Length)];
        _hallOustSince.Remove(entity.ObjectId);
        ZoneLog.Info($"[zone] residence hall oust obj={entity.ObjectId} hall={spot.ResidenceId} -> {dest.X},{dest.Y},{dest.Z}");
        Relocate(entity, dest.X, dest.Y, dest.Z);
    }

    internal void TickRuleZonesForTest(long nowUnixMs) => TickRuleZones(nowUnixMs);
}
