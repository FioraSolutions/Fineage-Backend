using System.Collections.Frozen;
using System.Text.Json;

namespace Fineage.Zone.Npcs;

/// <summary>
/// One Interlude NPC template transcribed from L2J Mobius <c>stats/npcs</c>.
/// <see cref="Type"/> is the Mobius instance (Merchant, Guard, Teleporter, Folk, …).
/// <see cref="Attackable"/> follows Mobius: missing <c>status attackable</c> means true.
/// <see cref="Aggressive"/> follows Mobius: missing <c>ai isAggressive</c> means true.
/// <see cref="Hp"/>, <see cref="Mp"/>, <see cref="PAtk"/>, <see cref="PDef"/>, <see cref="MAtk"/>, <see cref="MDef"/> and
/// <see cref="AttackSpeed"/> are the folded <c>NPC_STD_CALCULATOR</c> results (stat bonuses, level modifier, int truncation),
/// not the raw XML numbers. <see cref="Accuracy"/> / <see cref="Evasion"/> are unused XML values in Mobius.
/// </summary>
public sealed record NpcTemplate(
    int NpcId,
    string Name,
    string Title,
    string Type,
    int Level,
    float CollisionRadius,
    float CollisionHeight,
    bool Attackable,
    bool Aggressive,
    int Hp,
    int Mp,
    float RunSpeed,
    float WalkSpeed,
    long Exp = 0,
    int Sp = 0,
    double PAtk = 0,
    double PDef = 0,
    int AttackSpeed = 0,
    int AttackRange = 0,
    double Accuracy = 0,
    double Evasion = 0,
    int Critical = 0,
    int RandomDamage = 0,
    int Dex = 30,
    int ShieldDef = 0,
    int ShieldRate = 0,
    double MAtk = 0,
    double MDef = 0,
    int AggroRange = 0,
    int ClanHelpRange = 0,
    IReadOnlyList<string>? Clans = null,
    IReadOnlyList<int>? IgnoreNpcIds = null,
    double HpRegen = 0,
    double MpRegen = 0,
    /// <summary>Mobius <c>SoulShot</c> starting stock (Npc.rechargeShots).</summary>
    int SoulShot = 0,
    /// <summary>Mobius <c>SoulShotRate</c> % chance to fire a soulshot when stock remains.</summary>
    int SoulShotChance = 0,
    int SpiritShot = 0,
    int SpiritShotChance = 0,
    /// <summary>
    /// Folded Mobius <c>CreatureStat._defenceTraits</c> for weapon types (keys <c>sword</c>/<c>bow</c>/…),
    /// resolved from NPC skillList <c>DefenceTrait</c> passives. Missing key = 1.0.
    /// </summary>
    IReadOnlyDictionary<string, double>? DefenceTraits = null,
    /// <summary>Mobius skillList ∩ skill-combat.json attack skills (PhysicalSpecial / DDMagic / …).</summary>
    IReadOnlyList<NpcAiSkill>? AiSkills = null,
    /// <summary>Mobius <c>ai skill minChance</c> (default 7).</summary>
    int MinSkillChance = 7,
    /// <summary>Mobius <c>ai skill maxChance</c> (default 15).</summary>
    int MaxSkillChance = 15,
    /// <summary>Mobius <c>&lt;param name="despawn_time"&gt;</c> on EffectPoint signets, in seconds.</summary>
    int DespawnTimeSeconds = 0,
    /// <summary>Mobius <c>&lt;param name="skill_delay"&gt;</c>: seconds between signet aura pulses.</summary>
    int SkillDelaySeconds = 0,
    /// <summary>Mobius <c>stats/attack type</c> (SWORD, BOW, FIST, …). Empty when the XML omits it.</summary>
    string AttackType = "",
    /// <summary>Mobius <c>equipment rhand</c> item id (0 = none).</summary>
    int Rhand = 0,
    /// <summary>Mobius <c>equipment lhand</c> item id (0 = none).</summary>
    int Lhand = 0,
    /// <summary>Mobius <c>stats race</c> (UNDEAD, HUMANOID, …). Empty when omitted.</summary>
    string Race = "",
    /// <summary>Mobius <c>stats int</c> (default 21). Folded into <see cref="MAtk"/>.</summary>
    int Int = 21,
    /// <summary>Mobius <c>stats men</c> (default 10). Folded into <see cref="MDef"/>.</summary>
    int Men = 10);

/// <summary>One active Mobius NPC AI attack skill (id + level from skillList).</summary>
public readonly record struct NpcAiSkill(int SkillId, int Level);

/// <summary>Loads <c>data/npcs.json</c>.</summary>
public sealed class NpcCatalog
{
    public const string FileName = "npcs.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static readonly object CacheGate = new();
    static string? _cacheKey;
    static NpcCatalog? _cache;

    NpcCatalog(IReadOnlyDictionary<int, NpcTemplate> byId)
    {
        ById = byId;
    }

    public IReadOnlyDictionary<int, NpcTemplate> ById { get; }

    public NpcTemplate Get(int npcId)
    {
        if (!ById.TryGetValue(npcId, out var template))
            throw Fineage.Game.RequiredData.Required.Missing(
                Fineage.Game.RequiredData.RequiredDataKind.Npc, npcId, $"NPC template {npcId} is missing from {FileName}");
        return template;
    }

    public static NpcCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.GetFullPath(Path.Combine(dataDirectory, FileName));
        if (!File.Exists(path))
            throw new FileNotFoundException($"NPC template file not found: {path}", path);

        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        lock (CacheGate)
        {
            if (_cache != null && _cacheKey == key)
                return _cache;
            var loaded = Read(path);
            _cache = loaded;
            _cacheKey = key;
            return loaded;
        }
    }

    public static NpcCatalog LoadDefault()
    {
        var directory = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find data/npcs.json. Pass the data directory to NpcCatalog.Load.");
        return Load(directory);
    }

    public const int DefaultStr = 40;
    public const int DefaultCon = 43;
    public const int DefaultInt = 21;
    public const int DefaultMen = 10;

    /// <summary>
    /// Mobius <c>NpcTemplate</c> keeps P.Atk / M.Atk / P.Def / M.Def as <c>int</c> (StatSet.getInt truncates the XML
    /// decimal) and max HP / MP as <c>float</c>; the NPC standard calculators then fold them with the attribute
    /// bonuses and <c>levelMod = (level + 89) / 100</c>. These helpers are that fold; the catalog stores the
    /// folded value, so every consumer sees what <c>Creature.getPAtk()</c> etc. return.
    /// </summary>
    public static double LevelMod(int level) => (level + 89) / 100d;

    /// <summary><c>FuncPAtkMod</c>: <c>(int)pAtk · STR.bonus · levelMod</c>.</summary>
    public static double FoldPAtk(double xmlPAtk, double strBonus, int level) =>
        (int)Math.Max(0, xmlPAtk) * strBonus * LevelMod(level);

    /// <summary><c>FuncPDefMod</c> for a non-player: <c>(int)pDef · levelMod</c>.</summary>
    public static double FoldPDef(double xmlPDef, int level) => (int)Math.Max(0, xmlPDef) * LevelMod(level);

    /// <summary><c>FuncMAtkMod</c>: <c>(int)mAtk · INT.bonus² · levelMod²</c>.</summary>
    public static double FoldMAtk(double xmlMAtk, double intBonus, int level) =>
        (int)Math.Max(0, xmlMAtk) * intBonus * intBonus * LevelMod(level) * LevelMod(level);

    /// <summary><c>FuncMDefMod</c> for a non-player: <c>(int)mDef · MEN.bonus · levelMod</c>.</summary>
    public static double FoldMDef(double xmlMDef, double menBonus, int level) =>
        (int)Math.Max(0, xmlMDef) * menBonus * LevelMod(level);

    /// <summary><c>FuncPAtkSpeed</c> + <c>getPAtkSpd</c>: <c>round(attackSpeed · DEX.bonus)</c>.</summary>
    public static int FoldAttackSpeed(int xmlAttackSpeed, double dexBonus) =>
        (int)Math.Floor(Math.Max(0, xmlAttackSpeed) * dexBonus + 0.5);

    /// <summary><c>FuncMaxHpMul</c> / <c>FuncMaxMpMul</c>: <c>(int)((float)vital · bonus)</c>.</summary>
    public static int FoldVital(double xmlVital, double bonus) => (int)((float)xmlVital * bonus);

    static NpcFolds FoldRow(NpcDto row, Fineage.Game.StatBonusTable bonuses)
    {
        double str = bonuses.BonusClamped(Fineage.Game.StatSlot.Str, row.Str is > 0 ? row.Str.Value : DefaultStr, out _);
        double con = bonuses.BonusClamped(Fineage.Game.StatSlot.Con, row.Con is > 0 ? row.Con.Value : DefaultCon, out _);
        double intel = bonuses.BonusClamped(Fineage.Game.StatSlot.Int, row.Int is > 0 ? row.Int.Value : DefaultInt, out _);
        double men = bonuses.BonusClamped(Fineage.Game.StatSlot.Men, row.Men is > 0 ? row.Men.Value : DefaultMen, out _);
        double dex = bonuses.BonusClamped(Fineage.Game.StatSlot.Dex, row.Dex > 0 ? row.Dex : 30, out _);
        return new NpcFolds(
            Math.Max(1, FoldVital(row.Hp, con)),
            Math.Max(1, FoldVital(row.Mp, men)),
            FoldPAtk(row.PAtk, str, row.Level),
            FoldPDef(row.PDef, row.Level),
            FoldMAtk(row.MAtk, intel, row.Level),
            FoldMDef(row.MDef, men, row.Level),
            FoldAttackSpeed(row.AttackSpeed, dex));
    }

    readonly record struct NpcFolds(int Hp, int Mp, double PAtk, double PDef, double MAtk, double MDef, int AttackSpeed);

    static NpcCatalog Read(string path)
    {
        FileDto file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{path} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }

        if (file.Npcs == null || file.Npcs.Count == 0)
            throw new InvalidDataException($"{path} has no npcs.");

        // The stat-bonus table is shared with players. A synthetic data root (tests) may omit it; use the default one then.
        string dataRoot = Path.GetDirectoryName(path)!;
        if (!File.Exists(Path.Combine(dataRoot, Fineage.Game.ClassCatalog.StatBonusesFileName)))
            dataRoot = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory) ?? dataRoot;
        var bonuses = Fineage.Game.ClassCatalog.LoadStatBonuses(dataRoot);
        var byId = new Dictionary<int, NpcTemplate>(file.Npcs.Count);
        foreach (var row in file.Npcs)
        {
            if (row.NpcId <= 0)
                throw new InvalidDataException($"{path} has a non-positive npcId.");
            if (byId.ContainsKey(row.NpcId))
                throw new InvalidDataException($"{path} lists npc {row.NpcId} twice.");
            if (string.IsNullOrWhiteSpace(row.Type))
                throw new InvalidDataException($"{path} npc {row.NpcId} needs a type.");
            if (row.Level < 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} has a negative level.");
            if (row.Hp <= 0 || row.Mp <= 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} needs hp and mp.");
            if (row.RunSpeed <= 0 || row.WalkSpeed <= 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} needs run and walk speed.");
            if (row.CollisionRadius <= 0 || row.CollisionHeight <= 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} needs collision radius and height.");
            // Mobius resolves an absent <ai aggroRange> to 0 (NpcTemplate StatSet default),
            // so an explicit 0 is valid non-proactive data. Only a missing JSON field is
            // a transcription gap; refuse it instead of letting the mob silently stay idle.
            if (row.Aggressive && row.AggroRange is null)
                throw new InvalidDataException(
                    $"{path} npc {row.NpcId} is aggressive but has no aggroRange " +
                    "(emit the resolved Mobius value, including 0)");

            var fold = FoldRow(row, bonuses);
            byId.Add(row.NpcId, new NpcTemplate(
                row.NpcId,
                (row.Name ?? "").Trim(),
                (row.Title ?? "").Trim(),
                row.Type.Trim(),
                row.Level,
                row.CollisionRadius,
                row.CollisionHeight,
                row.Attackable,
                row.Aggressive,
                fold.Hp,
                fold.Mp,
                row.RunSpeed,
                row.WalkSpeed,
                Math.Max(0, row.Exp),
                Math.Max(0, row.Sp),
                fold.PAtk,
                fold.PDef,
                fold.AttackSpeed,
                Math.Max(0, row.AttackRange),
                row.Accuracy,
                row.Evasion,
                Math.Max(0, row.Critical),
                Math.Max(0, row.RandomDamage),
                row.Dex > 0 ? row.Dex : 30,
                Math.Max(0, row.ShieldDef),
                Math.Max(0, row.ShieldRate),
                fold.MAtk,
                fold.MDef,
                Math.Max(0, row.AggroRange ?? 0),
                Math.Max(0, row.ClanHelpRange),
                row.Clans is { Count: > 0 } ? row.Clans : null,
                row.IgnoreNpcIds is { Count: > 0 } ? row.IgnoreNpcIds : null,
                Math.Max(0, row.HpRegen),
                Math.Max(0, row.MpRegen),
                Math.Max(0, row.SoulShot),
                Math.Max(0, row.SoulShotChance),
                Math.Max(0, row.SpiritShot),
                Math.Max(0, row.SpiritShotChance),
                row.DefenceTraits is { Count: > 0 } ? row.DefenceTraits : null,
                ParseAiSkills(row.AiSkills),
                row.MinSkillChance > 0 ? row.MinSkillChance : 7,
                row.MaxSkillChance > 0 ? row.MaxSkillChance : 15,
                Math.Max(0, row.DespawnTimeSeconds),
                Math.Max(0, row.SkillDelaySeconds),
                (row.AttackType ?? "").Trim(),
                Math.Max(0, row.Rhand),
                Math.Max(0, row.Lhand),
                (row.Race ?? "").Trim().ToUpperInvariant(),
                row.Int is > 0 ? row.Int.Value : DefaultInt,
                row.Men is > 0 ? row.Men.Value : DefaultMen));
        }

        static IReadOnlyList<NpcAiSkill>? ParseAiSkills(List<AiSkillDto>? rows)
        {
            if (rows is not { Count: > 0 })
                return null;
            var list = new List<NpcAiSkill>(rows.Count);
            foreach (var r in rows)
            {
                if (r.Id > 0 && r.Level > 0)
                    list.Add(new NpcAiSkill(r.Id, r.Level));
            }
            return list.Count > 0 ? list : null;
        }

        return new NpcCatalog(byId.ToFrozenDictionary());
    }

    sealed class FileDto
    {
        public List<NpcDto>? Npcs { get; set; }
    }

    sealed class NpcDto
    {
        public int NpcId { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Type { get; set; }
        public int Level { get; set; }
        public float CollisionRadius { get; set; }
        public float CollisionHeight { get; set; }
        public bool Attackable { get; set; } = true;
        public bool Aggressive { get; set; } = true;
        /// <summary>Mobius <c>stats/vitals</c> hp / mp as transcribed (float precision); folded with CON / MEN at load.</summary>
        public double Hp { get; set; }
        public double Mp { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public long Exp { get; set; }
        public int Sp { get; set; }
        /// <summary>Mobius <c>stats/attack physical</c> (level-scaled base P.Atk).</summary>
        public double PAtk { get; set; }
        /// <summary>Mobius <c>stats/defence physical</c>.</summary>
        public double PDef { get; set; }
        /// <summary>Mobius <c>stats/attack attackSpeed</c> (Pspd).</summary>
        public int AttackSpeed { get; set; }
        /// <summary>Mobius <c>stats/attack range</c>.</summary>
        public int AttackRange { get; set; }
        /// <summary>Mobius <c>attack accuracy</c> base (sqrt(DEX)*6 + level added at runtime).</summary>
        public double Accuracy { get; set; }
        /// <summary>Mobius <c>defence evasion</c> base.</summary>
        public double Evasion { get; set; }
        /// <summary>Mobius <c>attack critical</c> base rate (×10 × DEX bonus).</summary>
        public int Critical { get; set; }
        /// <summary>Mobius <c>attack random</c> damage spread in percent.</summary>
        public int RandomDamage { get; set; }
        public int Dex { get; set; }
        /// <summary>Mobius <c>stats str</c> / <c>con</c> when not 40 / 43.</summary>
        public int? Str { get; set; }
        public int? Con { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("int")] public int? Int { get; set; }
        public int? Men { get; set; }
        /// <summary>Mobius <c>defence shield</c> / <c>shieldRate</c>.</summary>
        public int ShieldDef { get; set; }
        public int ShieldRate { get; set; }
        /// <summary>Mobius <c>attack magical</c> / <c>defence magical</c>.</summary>
        public double MAtk { get; set; }
        public double MDef { get; set; }
        /// <summary>
        /// Mobius <c>ai aggroRange</c>; null means the field was not transcribed (rejected for aggressive rows),
        /// while 0 is Mobius's explicit non-proactive value.
        /// </summary>
        public int? AggroRange { get; set; }
        public int ClanHelpRange { get; set; }
        public List<string>? Clans { get; set; }
        public List<int>? IgnoreNpcIds { get; set; }
        /// <summary>Mobius <c>vitals hpRegen</c>: HP restored per 3 s regen tick.</summary>
        public double HpRegen { get; set; }
        /// <summary>Mobius <c>vitals mpRegen</c>: MP restored per 3 s regen tick.</summary>
        public double MpRegen { get; set; }
        public int SoulShot { get; set; }
        public int SoulShotChance { get; set; }
        public int SpiritShot { get; set; }
        public int SpiritShotChance { get; set; }
        /// <summary>Mobius weapon defenceTraits multipliers (sword/bow/…).</summary>
        public Dictionary<string, double>? DefenceTraits { get; set; }
        public List<AiSkillDto>? AiSkills { get; set; }
        public int MinSkillChance { get; set; }
        public int MaxSkillChance { get; set; }
        /// <summary>Mobius EffectPoint <c>&lt;param name="despawn_time"&gt;</c>, in seconds.</summary>
        public int DespawnTimeSeconds { get; set; }
        /// <summary>Mobius EffectPoint <c>&lt;param name="skill_delay"&gt;</c>, in seconds.</summary>
        public int SkillDelaySeconds { get; set; }
        /// <summary>Mobius <c>stats/attack type</c> (SWORD, BOW, FIST, …).</summary>
        public string? AttackType { get; set; }
        /// <summary>Mobius <c>equipment rhand</c> / <c>lhand</c> item ids.</summary>
        public int Rhand { get; set; }
        public int Lhand { get; set; }
        public string? Race { get; set; }
    }

    sealed class AiSkillDto
    {
        public int Id { get; set; }
        public int Level { get; set; }
    }
}
