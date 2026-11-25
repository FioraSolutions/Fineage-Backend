using Fineage.Zone.Npcs;
using Fineage.Zone.Shops;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    NpcCatalog? _npcMaterializeCatalog;

    NpcCatalog NpcMaterializeCatalog => _npcMaterializeCatalog ??= NpcCatalog.LoadDefault();

    void MaterializeSpawn(SpawnNpcDefinition def, string mapId) =>
        MaterializeNpc(def, mapId, def.MaxHp, def.MaxMp);

    /// <summary>
    /// One Mobius-style NPC pawn from a resolved spawn row: fixed spawns, siege spawns, summon rows, and bosses
    /// all land here so combat stats always follow <see cref="SpawnCatalog.FromTemplate"/> / template merge.
    /// </summary>
    void MaterializeNpc(SpawnNpcDefinition def, string? mapId, int curHp, int curMp)
    {
        if (_byId.ContainsKey(def.ObjectId))
            return;

        def = MergeSpawnCombatStats(def);

        var tax = MerchantTowns.For(mapId);
        var entity = new ZoneEntity
        {
            ObjectId = def.ObjectId,
            Kind = ZoneEntityKind.Npc,
            Name = def.Name,
            Title = def.Title,
            NpcType = def.NpcType,
            X = def.X,
            Y = def.Y,
            Z = def.Z,
            Heading = def.Heading,
            NpcId = def.NpcId,
            CreatureRace = NpcMaterializeCatalog.ById.TryGetValue(def.NpcId, out var raceRow) ? raceRow.Race : "",
            Attackable = def.Attackable,
            MaxHp = def.MaxHp,
            CurHp = Math.Clamp(curHp, 1, Math.Max(1, def.MaxHp)),
            MaxMp = def.MaxMp,
            CurMp = Math.Clamp(curMp, 0, Math.Max(0, def.MaxMp)),
            Level = def.Level,
            RewardExp = def.Exp,
            RewardSp = def.Sp,
            PAtk = def.PAtk,
            HpRegen = def.HpRegen,
            MpRegen = def.MpRegen,
            PDef = def.PDef,
            PAtkSpd = def.AttackSpeed,
            BaseAccuracy = def.Accuracy,
            BaseEvasion = def.Evasion,
            BaseCritical = def.Critical,
            RandomDamage = def.RandomDamage,
            Dex = def.Dex,
            ShieldDef = def.ShieldDef,
            ShieldRate = def.ShieldRate,
            MAtk = def.MAtk,
            MDef = def.MDef,
            AttackRange = def.AttackRange,
            AttackType = def.AttackType,
            Rhand = def.Rhand,
            Lhand = def.Lhand,
            CollisionRadius = NpcCollisionRadius(def),
            Aggressive = def.Aggressive,
            AggroRange = def.AggroRange,
            ClanHelpRange = def.ClanHelpRange,
            Clans = def.Clans,
            IgnoreNpcIds = def.IgnoreNpcIds,
            NpcSoulShotLeft = def.SoulShot,
            NpcSoulShotChance = def.SoulShotChance,
            NpcSpiritShotLeft = def.SpiritShot,
            NpcSpiritShotChance = def.SpiritShotChance,
            DefenceTraits = def.DefenceTraits,
            AiSkills = FilterAttackSkills(def.AiSkills, _npcAiSupport.Get(def.NpcId)?.Suicide),
            MinSkillChance = def.MinSkillChance,
            MaxSkillChance = def.MaxSkillChance,
            AiHealSkills = _npcAiSupport.Get(def.NpcId)?.Heal,
            AiBuffSkills = _npcAiSupport.Get(def.NpcId)?.Buff,
            AiDebuffSkills = _npcAiSupport.Get(def.NpcId)?.Debuff,
            AiImmobilizeSkills = _npcAiSupport.Get(def.NpcId)?.Immobilize,
            AiSilenceSkills = _npcAiSupport.Get(def.NpcId)?.Silence,
            AiSuicideSkills = _npcAiSupport.Get(def.NpcId)?.Suicide,
            RunSpeed = def.RunSpeed,
            WalkSpeed = def.WalkSpeed,
            Running = false,
            GlobalAggro = -10,
            HomeSet = true,
            HomeX = def.X,
            HomeY = def.Y,
            HomeZ = def.Z,
            MerchantCastleId = TaxCastleAt(def.X, def.Y, def.Z) is var zoned && zoned > 0 ? zoned : tax.CastleId,
            MerchantTown = MerchantTowns.TownName(mapId),
            MerchantBaseTaxPercent = tax.BaseTaxPercent
        };
        _byId.Add(entity.ObjectId, entity);
        NoteNpcSpawned();
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        AttachWalkingRoute(entity);
    }

    /// <summary>
    /// Hand-built test spawn rows often omit combat stats (PDef=0). Fill from npcs.json when present so melee rolls
    /// fail loud only on genuine missing templates.
    /// </summary>
    SpawnNpcDefinition MergeSpawnCombatStats(SpawnNpcDefinition def)
    {
        if (!NpcMaterializeCatalog.ById.TryGetValue(def.NpcId, out var template))
            return def;

        double pAtk = def.PAtk > 0 ? def.PAtk : template.PAtk;
        double pDef = def.PDef > 0 ? def.PDef : template.PDef;
        double mAtk = def.MAtk > 0 ? def.MAtk : template.MAtk;
        double mDef = def.MDef > 0 ? def.MDef : template.MDef;
        int level = def.Level > 1 ? def.Level : Math.Max(1, template.Level);

        if (pAtk == def.PAtk && pDef == def.PDef && mAtk == def.MAtk && mDef == def.MDef && level == def.Level)
            return def;

        return def with
        {
            PAtk = pAtk,
            PDef = pDef,
            MAtk = mAtk,
            MDef = mDef,
            Level = level
        };
    }

    int NpcCollisionRadius(SpawnNpcDefinition def)
    {
        if (NpcMaterializeCatalog.ById.TryGetValue(def.NpcId, out var template) && template.CollisionRadius > 0)
            return (int)template.CollisionRadius;
        return 0;
    }
}