using Fineage.Accounts;

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
        ["teleport"] = 0.03,
        ["pvp"] = 0.05,
        ["relog"] = 0.02
    };

    /// <summary>AOI cell size used by the consistency oracle (match FINEAGE_ZONE_AOI_CELL).</summary>
    public int AoiCellSize { get; set; } = 3000;

    /// <summary>
    /// How many bots may <c>ConnectAndEnterAsync</c> at once. 0 uses 16.
    /// <c>FINEAGE_LOADTEST_ENTER_CONCURRENCY</c> overrides this.
    /// </summary>
    public int EnterConcurrency { get; set; } = 16;

    /// <summary>Instant offensive skill known at level 1 for the scenario class (Mobius autoGet).</summary>
    public int ResolveCombatSkillId()
    {
        if (CombatSkillId > 0)
            return CombatSkillId;
        return ClassId switch
        {
            HumanClasses.Mystic or 25 or 38 => 1177, // Wind Strike
            HumanClasses.Fighter or 18 or 31 or 44 or 49 or 53 => 3, // Power Strike (level ≥ 5)
            _ => 1177
        };
    }

    /// <summary>Optional sibling YAML under scenarios/; merges action weights and layout defaults.</summary>
    public string? Extends { get; set; }

    /// <summary>Full-economy hammer (#955): P2P trade, shops, stores, manufacture, manor, warehouse.</summary>
    public EconomyHammerConfig? Economy { get; set; }

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