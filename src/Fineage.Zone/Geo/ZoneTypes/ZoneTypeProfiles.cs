using Fineage.Zone;

namespace Fineage.Zone.Geo.ZoneTypes;

/// <summary>
/// One Mobius zone type. The registry calls <see cref="Accept"/> for each imported row of <see cref="TypeName"/>
/// and <see cref="Contribute"/> when a point is tested. Subclasses hold that type's parameters; the registry
/// does not branch on the type name.
/// </summary>
public abstract class ZoneTypeProfile
{
    public abstract string TypeName { get; }

    public abstract void Accept(
        ZoneShape shape,
        IReadOnlyDictionary<string, string> stats,
        IReadOnlyList<ZoneRacePoint> races,
        IReadOnlyList<ZoneSpawn> spawns);

    public virtual void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
    }

    public int Count { get; protected set; }

    protected static bool IsDisabled(IReadOnlyDictionary<string, string> stats) =>
        stats.TryGetValue("default_enabled", out var value)
        && value.Equals("false", StringComparison.OrdinalIgnoreCase);

    protected static bool TryInt(IReadOnlyDictionary<string, string> stats, string key, out int value)
    {
        value = 0;
        return stats.TryGetValue(key, out var text) && int.TryParse(text, out value);
    }

    protected static void RejectUnknownStats(string type, string name, IReadOnlyDictionary<string, string> stats, params string[] known)
    {
        foreach (var key in stats.Keys)
        {
            if (Array.IndexOf(known, key) < 0)
                ZoneLog.Error($"[zone] {type} '{name}' has unhandled stat '{key}'");
        }
    }

    protected static ZoneShape? First(IReadOnlyList<ZoneShape> zones, int x, int y, int z)
    {
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Contains(x, y, z))
                return zones[i];
        }

        return null;
    }
}

/// <summary>A type whose polygons already live in a dedicated catalog (water, peace, swamp, …).</summary>
public abstract class CatalogBackedZoneProfile : ZoneTypeProfile
{
    public override void Accept(
        ZoneShape shape,
        IReadOnlyDictionary<string, string> stats,
        IReadOnlyList<ZoneRacePoint> races,
        IReadOnlyList<ZoneSpawn> spawns)
    {
        ZoneLog.Error(
            $"[zone] {TypeName} '{shape.Name}' belongs in its catalog file, not zone-types.json — row not applied");
    }
}

public sealed class WaterZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "WaterZone";
}

public sealed class PeaceZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "PeaceZone";
}

public sealed class ArenaZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "ArenaZone";
}

public sealed class SiegeZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "SiegeZone";
}

public sealed class SwampZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "SwampZone";
}

public sealed class DamageZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "DamageZone";
}

public sealed class NoLandingZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "NoLandingZone";
}

public sealed class MotherTreeZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "MotherTreeZone";
}

public sealed class FishingZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "FishingZone";
}

public sealed class OlympiadStadiumZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "OlympiadStadiumZone";
}

public sealed class JailZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "JailZone";
}

public sealed class HqZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "HqZone";
}

public sealed class FlagPvPZoneProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "FlagPvPZone";
}

public sealed class NpcSpawnTerritoryProfile : CatalogBackedZoneProfile
{
    public override string TypeName => "NpcSpawnTerritory";
}

public sealed class NoStoreZoneProfile : ZoneTypeProfile
{
    readonly List<ZoneShape> _zones = [];
    public override string TypeName => "NoStoreZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats);
        if (IsDisabled(stats))
            return;
        _zones.Add(shape);
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        if (First(_zones, x, y, z) != null)
            flags.BlocksStore = true;
    }
}

public sealed class NoSummonFriendZoneProfile : ZoneTypeProfile
{
    readonly List<ZoneShape> _zones = [];
    public override string TypeName => "NoSummonFriendZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats);
        if (IsDisabled(stats))
            return;
        _zones.Add(shape);
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        if (First(_zones, x, y, z) != null)
            flags.BlocksSummon = true;
    }
}

public sealed class NoRestartZoneProfile : ZoneTypeProfile
{
    readonly List<NoRestartRow> _zones = [];
    public override string TypeName => "NoRestartZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "restartTime", "restartAllowedTime", "instanceId", "default_enabled");
        if (IsDisabled(stats))
            return;
        if (!TryInt(stats, "restartTime", out int restartSec))
            ZoneLog.Error($"[zone] NoRestartZone '{shape.Name}' missing restartTime");
        TryInt(stats, "restartAllowedTime", out int allowedSec);
        _zones.Add(new NoRestartRow(shape, Math.Max(0, restartSec) * 1000, Math.Max(0, allowedSec) * 1000));
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (!_zones[i].Shape.Contains(x, y, z))
                continue;
            flags.BlocksRestart = true;
            flags.RestartTimeMs = _zones[i].RestartTimeMs;
            flags.RestartAllowedMs = _zones[i].RestartAllowedMs;
            return;
        }
    }

    readonly record struct NoRestartRow(ZoneShape Shape, int RestartTimeMs, int RestartAllowedMs);
}

public sealed class EffectSkill
{
    public int SkillId { get; init; }
    public int Level { get; init; }
}

public sealed class EffectPulse
{
    public string ZoneName { get; init; } = "";
    public ZoneShape Shape { get; init; } = null!;
    public int Chance { get; init; } = 100;
    public int InitialDelayMs { get; init; }
    public int ReuseMs { get; init; } = 30000;
    public bool BypassConditions { get; init; }
    public bool RemoveOnExit { get; init; }
    public bool ShowDanger { get; init; } = true;
    public int MinLevel { get; init; }
    public EffectSkill[] Skills { get; init; } = [];
}

public sealed class EffectZoneProfile : ZoneTypeProfile
{
    readonly List<EffectPulse> _zones = [];
    public override string TypeName => "EffectZone";
    public IReadOnlyList<EffectPulse> Zones => _zones;

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats,
            "chance", "initialDelay", "reuse", "bypassSkillConditions", "maxDynamicSkillCount",
            "showDangerIcon", "skillIdLvl", "removeEffectsOnExit", "affectedLvlMin", "default_enabled",
            "targetClass", "instanceId");
        if (IsDisabled(stats))
            return;
        if (stats.TryGetValue("targetClass", out var target)
            && !target.Equals("Playable", StringComparison.OrdinalIgnoreCase)
            && !target.Equals("Creature", StringComparison.OrdinalIgnoreCase))
            ZoneLog.Error($"[zone] EffectZone '{shape.Name}' targetClass '{target}' is not applied");

        var skills = new List<EffectSkill>();
        if (!stats.TryGetValue("skillIdLvl", out var raw) || string.IsNullOrWhiteSpace(raw))
            ZoneLog.Error($"[zone] EffectZone '{shape.Name}' missing skillIdLvl — effects not applied");
        else
        {
            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = token.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int id) || !int.TryParse(parts[1], out int level) || id <= 0 || level <= 0)
                {
                    ZoneLog.Error($"[zone] EffectZone '{shape.Name}' skillIdLvl '{token}' is not applied");
                    continue;
                }

                skills.Add(new EffectSkill { SkillId = id, Level = level });
            }
        }

        TryInt(stats, "chance", out int chance);
        if (chance <= 0)
            chance = 100;
        TryInt(stats, "initialDelay", out int delay);
        TryInt(stats, "reuse", out int reuse);
        if (reuse <= 0)
            reuse = 30000;
        TryInt(stats, "affectedLvlMin", out int minLevel);
        _zones.Add(new EffectPulse
        {
            ZoneName = shape.Name,
            Shape = shape,
            Chance = chance,
            InitialDelayMs = Math.Max(0, delay),
            ReuseMs = reuse,
            BypassConditions = stats.TryGetValue("bypassSkillConditions", out var bypass)
                && bypass.Equals("true", StringComparison.OrdinalIgnoreCase),
            RemoveOnExit = stats.TryGetValue("removeEffectsOnExit", out var remove)
                && remove.Equals("true", StringComparison.OrdinalIgnoreCase),
            ShowDanger = !stats.TryGetValue("showDangerIcon", out var icon)
                || !icon.Equals("false", StringComparison.OrdinalIgnoreCase),
            MinLevel = Math.Max(0, minLevel),
            Skills = skills.ToArray()
        });
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (_zones[i].ShowDanger && _zones[i].Shape.Contains(x, y, z))
            {
                flags.InDanger = true;
                return;
            }
        }
    }

    public void Collect(int x, int y, int z, List<EffectPulse> into)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (_zones[i].Shape.Contains(x, y, z))
                into.Add(_zones[i]);
        }
    }
}

public sealed class ScriptZoneProfile : ZoneTypeProfile
{
    readonly List<ZoneShape> _zones = [];
    public override string TypeName => "ScriptZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "default_enabled");
        if (string.IsNullOrWhiteSpace(shape.Name))
        {
            ZoneLog.Error("[zone] ScriptZone missing name — not applied");
            return;
        }

        if (IsDisabled(stats))
            return;
        _zones.Add(shape);
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        var hit = First(_zones, x, y, z);
        if (hit != null)
            flags.ScriptZone = hit.Name;
    }
}

public sealed class TownZoneProfile : ZoneTypeProfile
{
    readonly List<TownRow> _zones = [];
    public override string TypeName => "TownZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "townId", "taxById", "default_enabled");
        if (IsDisabled(stats))
            return;
        if (!TryInt(stats, "townId", out int townId) || townId <= 0)
        {
            ZoneLog.Error($"[zone] TownZone '{shape.Name}' missing townId — not applied");
            return;
        }

        if (!TryInt(stats, "taxById", out int taxBy))
            ZoneLog.Error($"[zone] TownZone '{shape.Name}' missing taxById");
        _zones.Add(new TownRow(shape, townId, Math.Max(0, taxBy)));
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (!_zones[i].Shape.Contains(x, y, z))
                continue;
            flags.TownId = _zones[i].TownId;
            flags.TownTaxCastleId = _zones[i].TaxCastleId;
            return;
        }
    }

    readonly record struct TownRow(ZoneShape Shape, int TownId, int TaxCastleId);
}

public sealed class RespawnZoneProfile : ZoneTypeProfile
{
    readonly List<RespawnRow> _zones = [];
    public override string TypeName => "RespawnZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "default_enabled");
        if (IsDisabled(stats))
            return;
        if (races.Count == 0)
        {
            ZoneLog.Error($"[zone] RespawnZone '{shape.Name}' missing race points — not applied");
            return;
        }

        var byRace = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in races)
        {
            if (string.IsNullOrWhiteSpace(race.Race) || string.IsNullOrWhiteSpace(race.Point))
            {
                ZoneLog.Error($"[zone] RespawnZone '{shape.Name}' has an empty race point — not applied");
                continue;
            }

            byRace[race.Race] = race.Point;
        }

        if (byRace.Count == 0)
            return;
        _zones.Add(new RespawnRow(shape, byRace));
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        string? raceName = ZoneRaces.Name(race);
        for (int i = 0; i < _zones.Count; i++)
        {
            if (!_zones[i].Shape.Contains(x, y, z))
                continue;
            flags.RespawnZone = _zones[i].Shape.Name;
            if (raceName == null)
            {
                flags.RespawnRaceMissing = true;
                return;
            }

            if (_zones[i].ByRace.TryGetValue(raceName, out var point))
                flags.RespawnPoint = point;
            else
                flags.RespawnRaceMissing = true;
            return;
        }
    }

    sealed class RespawnRow(ZoneShape shape, Dictionary<string, string> byRace)
    {
        public ZoneShape Shape { get; } = shape;
        public Dictionary<string, string> ByRace { get; } = byRace;
    }
}

public static class ZoneRaces
{
    public static string? Name(int race) => race switch
    {
        0 => "HUMAN",
        1 => "ELF",
        2 => "DARK_ELF",
        3 => "ORC",
        4 => "DWARF",
        _ => null
    };
}

public sealed class ConditionZoneProfile : ZoneTypeProfile
{
    readonly List<ConditionRow> _zones = [];
    public override string TypeName => "ConditionZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "NoBookmark", "NoItemDrop", "default_enabled");
        if (IsDisabled(stats))
            return;
        bool bookmark = stats.TryGetValue("NoBookmark", out var b) && b.Equals("true", StringComparison.OrdinalIgnoreCase);
        bool noDrop = stats.TryGetValue("NoItemDrop", out var d) && d.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!bookmark && !noDrop)
        {
            ZoneLog.Error($"[zone] ConditionZone '{shape.Name}' missing NoBookmark and NoItemDrop — not applied");
            return;
        }

        _zones.Add(new ConditionRow(shape, bookmark, noDrop));
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (!_zones[i].Shape.Contains(x, y, z))
                continue;
            if (_zones[i].NoBookmark)
                flags.BlocksBookmark = true;
            if (_zones[i].NoItemDrop)
                flags.BlocksItemDrop = true;
        }
    }

    readonly record struct ConditionRow(ZoneShape Shape, bool NoBookmark, bool NoItemDrop);
}

public sealed class DerbyTrackZoneProfile : ZoneTypeProfile
{
    readonly List<ZoneShape> _zones = [];
    public override string TypeName => "DerbyTrackZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "default_enabled");
        if (IsDisabled(stats))
            return;
        _zones.Add(shape);
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        if (First(_zones, x, y, z) != null)
            flags.InDerby = true;
    }
}

public sealed class ResidenceSpot
{
    public string ZoneName { get; init; } = "";
    public int ResidenceId { get; init; }
    public bool Hall { get; init; }
    public ZoneSpawn[] Spawns { get; init; } = [];
}

public abstract class ResidenceTeleportProfile : ZoneTypeProfile
{
    readonly List<ResidenceRow> _zones = [];
    protected abstract bool Hall { get; }

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "residenceId", "residenceZoneId", "default_enabled");
        if (IsDisabled(stats))
            return;
        if (!TryInt(stats, "residenceId", out int residenceId) || residenceId <= 0)
        {
            ZoneLog.Error($"[zone] {TypeName} '{shape.Name}' missing residenceId — not applied");
            return;
        }

        if (spawns.Count == 0)
            ZoneLog.Error($"[zone] {TypeName} '{shape.Name}' missing spawn — oust has no destination");
        _zones.Add(new ResidenceRow(shape, residenceId, spawns.ToArray()));
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        if (FirstOf(x, y, z) != null)
            flags.BlocksSummon = true;
    }

    public bool TrySpot(int x, int y, int z, out ResidenceSpot spot)
    {
        var row = FirstOf(x, y, z);
        if (row == null)
        {
            spot = null!;
            return false;
        }

        spot = new ResidenceSpot
        {
            ZoneName = row.Shape.Name,
            ResidenceId = row.ResidenceId,
            Hall = Hall,
            Spawns = row.Spawns
        };
        return true;
    }

    ResidenceRow? FirstOf(int x, int y, int z)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (_zones[i].Shape.Contains(x, y, z))
                return _zones[i];
        }

        return null;
    }

    sealed class ResidenceRow(ZoneShape shape, int residenceId, ZoneSpawn[] spawns)
    {
        public ZoneShape Shape { get; } = shape;
        public int ResidenceId { get; } = residenceId;
        public ZoneSpawn[] Spawns { get; } = spawns;
    }
}

public sealed class ResidenceTeleportZoneProfile : ResidenceTeleportProfile
{
    public override string TypeName => "ResidenceTeleportZone";
    protected override bool Hall => false;
}

public sealed class ResidenceHallTeleportZoneProfile : ResidenceTeleportProfile
{
    public override string TypeName => "ResidenceHallTeleportZone";
    protected override bool Hall => true;
}

public sealed class TaxZoneProfile : ZoneTypeProfile
{
    readonly List<TaxRow> _zones = [];
    public override string TypeName => "TaxZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "domainId", "default_enabled");
        if (IsDisabled(stats))
            return;
        if (!TryInt(stats, "domainId", out int domain) || domain <= 0)
        {
            ZoneLog.Error($"[zone] TaxZone '{shape.Name}' missing domainId — not applied");
            return;
        }

        _zones.Add(new TaxRow(shape, domain));
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (!_zones[i].Shape.Contains(x, y, z))
                continue;
            flags.TaxCastleId = _zones[i].CastleId;
            return;
        }
    }

    readonly record struct TaxRow(ZoneShape Shape, int CastleId);
}

public sealed class BossZoneProfile : ZoneTypeProfile
{
    readonly List<ZoneShape> _zones = [];
    public override string TypeName => "BossZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "default_enabled", "InvadeTime", "oustX", "oustY", "oustZ");
        if (IsDisabled(stats))
            return;
        if (!TryInt(stats, "oustX", out _) || !TryInt(stats, "oustY", out _) || !TryInt(stats, "oustZ", out _))
            ZoneLog.Error($"[zone] BossZone '{shape.Name}' missing oustX/oustY/oustZ");
        _zones.Add(shape);
        Count++;
    }

    public override void Contribute(int x, int y, int z, int race, ZoneFlags flags)
    {
        if (First(_zones, x, y, z) != null)
            flags.InBoss = true;
    }
}

public sealed class NoPvPZoneProfile : ZoneTypeProfile
{
    readonly List<ZoneShape> _zones = [];
    public override string TypeName => "NoPvPZone";
    public IReadOnlyList<ZoneShape> Zones => _zones;

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "default_enabled");
        if (IsDisabled(stats))
            return;
        _zones.Add(shape);
        Count++;
    }
}

public sealed class CastleZoneProfile : ZoneTypeProfile
{
    public override string TypeName => "CastleZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "castleId", "default_enabled");
        if (!TryInt(stats, "castleId", out int id) || id <= 0)
        {
            ZoneLog.Error($"[zone] CastleZone '{shape.Name}' missing castleId — not applied");
            return;
        }

        if (spawns.Count == 0)
            ZoneLog.Error($"[zone] CastleZone '{shape.Name}' missing spawn");
        Count++;
    }
}

public sealed class ClanHallZoneProfile : ZoneTypeProfile
{
    public override string TypeName => "ClanHallZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "clanHallId", "default_enabled");
        if (!TryInt(stats, "clanHallId", out int id) || id <= 0)
        {
            ZoneLog.Error($"[zone] ClanHallZone '{shape.Name}' missing clanHallId — not applied");
            return;
        }

        Count++;
    }
}

public sealed class SiegableHallZoneProfile : ZoneTypeProfile
{
    public override string TypeName => "SiegableHallZone";

    public override void Accept(ZoneShape shape, IReadOnlyDictionary<string, string> stats, IReadOnlyList<ZoneRacePoint> races, IReadOnlyList<ZoneSpawn> spawns)
    {
        RejectUnknownStats(TypeName, shape.Name, stats, "clanHallId", "default_enabled");
        if (!TryInt(stats, "clanHallId", out int id) || id <= 0)
        {
            ZoneLog.Error($"[zone] SiegableHallZone '{shape.Name}' missing clanHallId — not applied");
            return;
        }

        Count++;
    }
}
