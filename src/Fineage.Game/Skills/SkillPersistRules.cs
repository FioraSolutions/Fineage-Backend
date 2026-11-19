using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game.Skills;

/// <summary>
/// Mobius <c>Player.storeEffect</c> filters: toggles, dances/songs (<c>isMagic == 3</c>),
/// herbs (<c>abnormalInstant</c> / <c>isRecoveryHerb</c> / <c>herbs.json</c>), and
/// <c>LIFE_FORCE_OTHERS</c> heals are not restored on login. Reuse timestamps are stored separately.
/// </summary>
public static class SkillPersistRules
{
    public const string FlagsFileName = "skill-persist-flags.json";
    public const string LifeForceOthers = "LIFE_FORCE_OTHERS";

    static readonly Lazy<Flags> Loaded = new(Load);

    public static bool IsToggle(SkillDefinition? skill) =>
        skill != null && string.Equals(skill.OperateType, "T", StringComparison.OrdinalIgnoreCase);

    public static bool IsDance(int skillId) => Loaded.Value.Dances.Contains(skillId);

    public static bool IsHerbEffect(int skillId) =>
        Loaded.Value.AbnormalInstant.Contains(skillId) || Loaded.Value.RecoveryHerbs.Contains(skillId);

    /// <summary>Whether this abnormal may be written as restore_type 0 and re-applied on enter.</summary>
    public static bool CanStoreEffect(SkillDefinition? skill, int skillId, bool isHerbSkill = false)
    {
        if (isHerbSkill || IsHerbEffect(skillId))
            return false;
        if (IsDance(skillId))
            return false;
        if (IsToggle(skill))
            return false;
        if (skill != null &&
            string.Equals(skill.AbnormalType, LifeForceOthers, StringComparison.OrdinalIgnoreCase))
            return false;
        return skillId > 0;
    }

    static Flags Load()
    {
        var path = TeleportCatalog.FindFile(FlagsFileName);
        if (path == null)
            return new Flags([], [], []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new Flags(ReadIds(root, "dances"), ReadIds(root, "abnormalInstant"), ReadIds(root, "recoveryHerbs"));
    }

    static HashSet<int> ReadIds(JsonElement root, string name)
    {
        var set = new HashSet<int>();
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return set;
        foreach (var n in arr.EnumerateArray())
        {
            if (n.TryGetInt32(out int id) && id > 0)
                set.Add(id);
        }

        return set;
    }

    sealed record Flags(HashSet<int> Dances, HashSet<int> AbnormalInstant, HashSet<int> RecoveryHerbs);
}
