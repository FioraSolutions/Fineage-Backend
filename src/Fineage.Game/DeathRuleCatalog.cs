using System.Text.Json;

namespace Fineage.Game;

/// <summary>
/// Mobius death rules transcribed from the Interlude datapack: the exp-bar share lost on death
/// (<c>PlayerXpPercentLostData.getXpPercent</c>, <c>Player.java:5321</c>) and the chance a death raises Death Penalty
/// (<c>Character.ini DeathPenaltyChance</c>, <c>Config.java:1656</c>).
/// <para>
/// <c>Player.calculateDeathExpPenalty</c> (<c>Player.java:5318-5363</c>) takes the percent for the level, scales it by
/// <c>Stat.REDUCE_EXP_LOST_BY_RAID</c> / <c>_MOB</c> / <c>_PVP</c> and by <c>RateKarmaExpLost</c> when the character
/// has karma, then removes
/// <c>(expForLevel(level + 1) - expForLevel(level)) * percent / 100</c> of the current level's bar.
/// </para>
/// </summary>
public sealed class DeathRuleCatalog
{
    public const string FileName = "xp-percent-lost.json";

    /// <summary>Mobius <c>Character.ini DeathPenaltyChance</c> default (<c>Config.java:1656</c>).</summary>
    public const int DefaultDeathPenaltyChance = 20;

    /// <summary>Mobius <c>Player._deathPenaltyBuffLevel</c> ceiling (<c>increaseDeathPenaltyBuffLevel</c>, <c>:11724</c>).</summary>
    public const int MaxDeathPenaltyLevel = 15;

    /// <summary>Mobius <c>Config.RESPAWN_RESTORE_HP</c> fallback (<c>Config.java:1658</c>).</summary>
    public const double DefaultRespawnRestoreHp = 0.65;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly double[] _percent;

    DeathRuleCatalog(double[] percent, int deathPenaltyChance, double respawnRestoreHp, double respawnRestoreMp)
    {
        _percent = percent;
        DeathPenaltyChance = Math.Clamp(deathPenaltyChance, 0, 100);
        RespawnRestoreHp = Math.Clamp(respawnRestoreHp, 0d, 1d);
        RespawnRestoreMp = Math.Clamp(respawnRestoreMp, 0d, 1d);
    }

    /// <summary>Percent of the current level's exp bar removed on death. Index is the level; rows start at 1.</summary>
    public IReadOnlyList<double> PercentByLevel => _percent;

    /// <summary>Chance in percent that a death raises Death Penalty by one level.</summary>
    public int DeathPenaltyChance { get; }

    /// <summary>
    /// Mobius <c>Config.RESPAWN_RESTORE_HP</c> (<c>Character.ini RespawnRestoreHP</c>, <c>Creature.java:2540</c>): the
    /// fraction of max HP a plain revive restores. The reference deployment runs 65%.
    /// </summary>
    public double RespawnRestoreHp { get; }

    /// <summary>
    /// Mobius <c>Config.RESPAWN_RESTORE_MP</c> (<c>Character.ini RespawnRestoreMP</c>, <c>Creature.java:2544</c>).
    /// The reference deployment restores nothing, so a village revive leaves the mana bar empty.
    /// </summary>
    public double RespawnRestoreMp { get; }

    /// <summary>
    /// Mobius <c>PlayerXpPercentLostData.getXpPercent(level)</c>: the table row for the level, clamped at both ends so a
    /// level outside the datapack (1 below or above) still returns a sane share instead of throwing.
    /// </summary>
    public double PercentForLevel(int level)
    {
        if (_percent.Length == 0)
            return 0d;
        return _percent[Math.Clamp(level, 1, _percent.Length) - 1];
    }

    /// <summary>
    /// Mobius <c>calculateDeathExpPenalty</c>: the exp removed for dying at <paramref name="level"/>.
    /// <paramref name="levelBarExp"/> is the size of the *next* level's bar
    /// (<c>expForLevel(level + 1) - expForLevel(level)</c>), which is what the percent is taken from — not the
    /// character's absolute exp.
    /// </summary>
    public long ExpLost(int level, long levelBarExp, double multiplier = 1d)
    {
        if (levelBarExp <= 0)
            return 0;
        double percent = PercentForLevel(level) * Math.Max(0d, multiplier);
        return (long)Math.Round(levelBarExp * percent / 100d);
    }

    public static DeathRuleCatalog LoadDefault()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", FileName);
        return File.Exists(path) ? Load(path) : Default;
    }

    public static DeathRuleCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    internal static DeathRuleCatalog Parse(Stream stream)
    {
        var file = JsonSerializer.Deserialize<FileShape>(stream, JsonOptions)
                   ?? throw new InvalidDataException($"{FileName}: empty document.");
        var rows = file.PerLevel ?? new Dictionary<string, double>();
        int max = rows.Count == 0 ? 0 : rows.Keys.Select(int.Parse).Max();
        var percent = new double[max];
        foreach (var (key, value) in rows)
            percent[int.Parse(key) - 1] = value;
        return new DeathRuleCatalog(
            percent,
            file.DeathPenaltyChance ?? DefaultDeathPenaltyChance,
            file.RespawnRestoreHp ?? DefaultRespawnRestoreHp,
            file.RespawnRestoreMp ?? 0d);
    }

    /// <summary>
    /// Empty table: a death costs no exp. Used only when the file is missing, so a partial deploy degrades to the old
    /// behaviour instead of refusing to start the zone.
    /// </summary>
    public static DeathRuleCatalog Default { get; } =
        new([], DefaultDeathPenaltyChance, DefaultRespawnRestoreHp, 0d);

    /// <summary>
    /// Test-only constructor hook: a catalog with a forced penalty chance and respawn shares, so a roll can be pinned
    /// without a seeded Random or a hand-written JSON file.
    /// </summary>
    public static DeathRuleCatalog ForTest(int chance, double respawnHp, double respawnMp) =>
        new([10d, 9.875d, 9.75d], chance, respawnHp, respawnMp);

    sealed class FileShape
    {
        public Dictionary<string, double>? PerLevel { get; set; }
        public int? DeathPenaltyChance { get; set; }
        public double? RespawnRestoreHp { get; set; }
        public double? RespawnRestoreMp { get; set; }
    }
}
