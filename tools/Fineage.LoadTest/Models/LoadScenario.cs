using Fineage.Accounts;
using Fineage.LoadTest.Consistency;

namespace Fineage.LoadTest.Models;

public sealed class LoadScenario
{
    public string Name { get; set; } = "unnamed";
    public int BotCount { get; set; } = 50;
    public int RampUpSeconds { get; set; } = 30;
    public int DurationSeconds { get; set; } = 120;
    public string SpawnLayout { get; set; } = "town";
    public int SpawnX { get; set; } = 46045;
    public int SpawnY { get; set; } = 41251;
    public int SpawnZ { get; set; } = -3440;
    public string AccountPrefix { get; set; } = "loadbot";
    public string Password { get; set; } = "loadtest-pass-v1";
    public string LoginHost { get; set; } = "127.0.0.1";
    public int LoginPort { get; set; } = 9106;
    public int ZonePort { get; set; } = 9777;
    public int ClassId { get; set; } = 0;
    public int Race { get; set; } = HumanClasses.RaceHuman;
    /// <summary>Mobius skill id for load combat skill actions. 0 = <see cref="ResolveCombatSkillId"/>.</summary>
    public int CombatSkillId { get; set; }
    /// <summary>When true, the run fails if no UseSkill→MagicSkillUse oracle samples were collected.</summary>
    public bool RequireSkillLatencySamples { get; set; }
    public Dictionary<string, double> ActionWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["move"] = 0.30,
        ["attack"] = 0.18,
        ["skill"] = 0.10,
        ["say"] = 0.08,
        ["sit"] = 0.08,
        ["social"] = 0.06,
        ["pickup"] = 0.08,
        ["trade"] = 0.04,
        ["party"] = 0.04,
        ["pvp"] = 0.05,
        ["relog"] = 0.02
    };

    /// <summary>Default share of the fleet that travels town to town through gatekeepers.</summary>
    public const double DefaultGatekeeperTravelShare = 0.2;

    /// <summary>
    /// Share of bots (0..1) that only travel between towns via gatekeeper teleports instead of
    /// running <see cref="ActionWeights"/>. Null uses <see cref="DefaultGatekeeperTravelShare"/>; 0 turns travel off.
    /// </summary>
    public double? GatekeeperTravelShare { get; set; }

    /// <summary>Pause after a landed teleport before the bot talks to the next gatekeeper.</summary>
    public int GatekeeperTripCooldownMs { get; set; } = 3000;

    /// <summary>Datapack directory with teleports.json and spawns/ (see <c>GatekeeperDirectory.ResolveDataDir</c>).</summary>
    public string? DataDir { get; set; }

    /// <summary>
    /// Place the bot at <see cref="SpawnX"/>,<see cref="SpawnY"/>,<see cref="SpawnZ"/> right after EnterWorld.
    /// <para>
    /// EnterWorld carries no coordinates (Mobius <c>EnterWorld</c> resumes the persisted character position), so a
    /// scenario's spawn point only decides where the bot believes it is, never where the server puts it. A scenario
    /// that must start at a specific place — a gatekeeper on an upper floor, a hub — turns this on; the bot then sends
    /// the load-test <c>TeleportToLocation</c>, which the zone accepts only when
    /// <c>FINEAGE_ZONE_LOADTEST_TELEPORT=1</c>.
    /// </para>
    /// </summary>
    public bool TeleportToSpawnOnEnter { get; set; }

    public double ResolvedGatekeeperTravelShare
    {
        get
        {
            double share = GatekeeperTravelShare ?? DefaultGatekeeperTravelShare;
            if (share is < 0 or > 1 || double.IsNaN(share))
                throw new InvalidOperationException($"gatekeeperTravelShare must be within 0..1, got {share}");
            return share;
        }
    }

    /// <summary>
    /// Bot <paramref name="botId"/> travels when the running share count steps up at its index, so
    /// exactly round(share × botCount) bots travel, spread evenly over the fleet and the ramp.
    /// </summary>
    /// <remarks>
    /// Heavy-play combat bots (party or train slots) never travel; in <c>aoe-combined</c> the share applies to
    /// the economy background bots only, counted from the first of them.
    /// </remarks>
    public bool IsGatekeeperTraveler(int botId)
    {
        double share = ResolvedGatekeeperTravelShare;
        int index = botId;
        if (HeavyPlay is { IsHeavyPlay: true } hp)
        {
            int combat = hp.CombatBotCount;
            if (botId < combat)
                return false;
            index = botId - combat;
        }

        return Math.Floor((index + 1) * share + 1e-9) > Math.Floor(index * share + 1e-9);
    }

    /// <summary>AOI cell size used by the consistency oracle (match FINEAGE_ZONE_AOI_CELL).</summary>
    public int AoiCellSize { get; set; } = 3000;

    /// <summary>
    /// How many bots may <c>ConnectAndEnterAsync</c> at once. 0 uses 16.
    /// <c>FINEAGE_LOADTEST_ENTER_CONCURRENCY</c> overrides this.
    /// </summary>
    public int EnterConcurrency { get; set; } = 16;

    /// <summary>Optional sibling YAML under scenarios/; merges action weights and layout defaults.</summary>
    public string? Extends { get; set; }

    /// <summary>Opt-in bounded per-issue frame trace written next to the report (see <see cref="ConsistencyTraceOptions"/>).</summary>
    public ConsistencyTraceOptions ConsistencyTrace { get; set; } = new();

    /// <summary>Full-economy hammer (#955): P2P trade, shops, stores, manufacture, manor, warehouse.</summary>
    public EconomyHammerConfig? Economy { get; set; }

    /// <summary>Structured AoE / party fan-out scenarios (#951).</summary>
    public HeavyPlayConfig? HeavyPlay { get; set; }

    public int ResolveCombatSkillId()
    {
        if (CombatSkillId > 0)
            return CombatSkillId;
        if (HeavyPlay?.SkillRotation is { Length: > 0 })
            return HeavyPlay.SkillRotation[0];
        return ClassId switch
        {
            HumanClasses.Mystic or 25 or 38 => 1177,
            48 => 36,
            HumanClasses.Fighter or 18 or 31 or 44 or 49 or 53 => 3,
            _ => 1177
        };
    }

    public (int classId, int race) ResolveClassAndRace(int botId)
    {
        if (Economy is { Enabled: true } eco)
        {
            int mod = Math.Abs(botId) % eco.ManufacturerModulo;
            if (mod == eco.ManufacturerRemainder)
                return (eco.ManufacturerClassId, eco.ManufacturerRace);
        }

        return (ClassId, Race);
    }
}