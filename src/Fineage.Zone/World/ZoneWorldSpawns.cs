using Fineage.Protocol;
using Fineage.Zone.Shops;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly SpawnCatalog _spawnCatalog;
    readonly string _activeMapId;
    readonly bool _includeEnabledMaps;
    readonly Func<DateTimeOffset> _clock;
    readonly Dictionary<int, SpawnSlotState> _spawnSlots = [];

    sealed class SpawnSlotState
    {
        public required SpawnNpcDefinition Definition { get; init; }
        public required string MapId { get; init; }
        public DateTimeOffset? RespawnAt { get; set; }
    }

    void BootstrapSpawns()
    {
        var maps = new List<SpawnMapDefinition>();
        var whitelist = ZoneMapFilter.Maps;
        if (whitelist.Count > 0)
        {
            foreach (string id in whitelist)
            {
                if (!_spawnCatalog.Maps.TryGetValue(id, out var map))
                    throw new InvalidOperationException($"FINEAGE_ZONE_MAPS references unknown spawn map '{id}'.");
                maps.Add(map);
            }
        }
        else if (_includeEnabledMaps)
        {
            foreach (var map in _spawnCatalog.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
            {
                if (!map.Enabled)
                    continue;
                maps.Add(map);
            }
            if (maps.Count == 0)
                throw new InvalidOperationException("No enabled spawn maps in datapack.");
        }
        else
        {
            // Explicit single-map mode (FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0 or tests). Active id defaults to
            // SmokeMapId only when the caller opted out of the full world — never the hosted default.
            var active = _spawnCatalog.GetMap(_activeMapId);
            if (!active.Enabled)
                throw new InvalidOperationException($"Active spawn map '{_activeMapId}' is disabled in datapack.");
            maps.Add(active);
        }

        var bounds = ZoneMapFilter.BoundsList;
        foreach (var map in maps)
        {
            foreach (var def in map.Npcs)
            {
                if (!ZoneMapFilter.Contains(bounds, def.X, def.Y))
                    continue;
                if (_spawnSlots.ContainsKey(def.ObjectId))
                    throw new InvalidOperationException($"Duplicate spawn object id {def.ObjectId}.");
                _spawnSlots.Add(def.ObjectId, new SpawnSlotState { Definition = def, MapId = map.MapId });
                MaterializeSpawn(def, map.MapId);
            }
        }
    }

    void MaterializeSpawn(SpawnNpcDefinition def, string mapId)
    {
        if (_byId.ContainsKey(def.ObjectId))
            return;

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
            Attackable = def.Attackable,
            MaxHp = def.MaxHp,
            CurHp = def.MaxHp,
            MaxMp = def.MaxMp,
            CurMp = def.MaxMp,
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
            // Mobius Spawn location is the returnHome target; captured at spawn, not at first aggro, so a mob that
            // drifted before being attacked still walks back to the real spawn point.
            HomeSet = true,
            HomeX = def.X,
            HomeY = def.Y,
            HomeZ = def.Z,
            MerchantCastleId = tax.CastleId,
            MerchantTown = MerchantTowns.TownName(mapId),
            MerchantBaseTaxPercent = tax.BaseTaxPercent
        };
        _byId.Add(entity.ObjectId, entity);
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
    }

    void ScheduleRespawn(int objectId)
    {
        if (!_spawnSlots.TryGetValue(objectId, out var slot))
            return;

        int delaySeconds = slot.Definition.RespawnDelaySeconds;
        int jitter = slot.Definition.RespawnRandomSeconds;
        if (jitter > 0)
            delaySeconds += _spawnRandom.Next(0, jitter + 1);

        slot.RespawnAt = _clock().AddSeconds(Math.Max(1, delaySeconds));
    }

    void TickSpawns()
    {
        var now = _clock();
        foreach (var slot in _spawnSlots.Values)
        {
            if (slot.RespawnAt == null || now < slot.RespawnAt)
                continue;
            if (_byId.ContainsKey(slot.Definition.ObjectId))
            {
                slot.RespawnAt = null;
                continue;
            }

            MaterializeSpawn(slot.Definition, slot.MapId);
            slot.RespawnAt = null;
            if (_byId.TryGetValue(slot.Definition.ObjectId, out var npc))
                FanoutNewNpc(npc);
        }
    }

    void FanoutNewNpc(ZoneEntity npc)
    {
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (!VisibleTo(player.ObjectId, npc.ObjectId))
                continue;
            if (!player.Knownlist.Add(npc.ObjectId))
                continue;
            // Net clients: the respawn was added to the knownlist here, so the next move refresh sees no diff and
            // the mob stayed invisible until the player left and re-entered view ("mobs appear late"). Push the
            // EntitySpawn now, like Mobius Spawn.doSpawn → broadcast NpcInfo to the region.
            NetBridge?.FanoutKnownlistDiff(player.ObjectId, [npc.ObjectId], []);
        }
    }

    /// <summary>Test hook: despawn an NPC (corpse decay path) so its respawn timer starts.</summary>
    internal void DespawnNpcForTests(int objectId)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(objectId, out var npc))
                DespawnNpc(npc);
        }
    }

    /// <summary>Test hook: advance respawn timers without waiting on the simulation loop.</summary>
    internal void AdvanceSpawnsForTests()
    {
        lock (_gate)
        {
            TickCorpseDecay(_clock().ToUnixTimeMilliseconds());
            TickSpawns();
        }
    }

    /// <summary>
    /// Mobius puts <c>isSuicideAttack</c> skills only in <c>AISkillScope.SUICIDE</c>, not GENERAL/ATTACK.
    /// Fineage <c>aiSkills</c> still list Death Bomb — strip those ids when suicide scope is present.
    /// </summary>
    static IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? FilterAttackSkills(
        IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? attack,
        IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? suicide)
    {
        if (attack is not { Count: > 0 } || suicide is not { Count: > 0 })
            return attack;
        var ban = new HashSet<int>();
        foreach (var s in suicide)
            ban.Add(s.SkillId);
        var kept = attack.Where(a => !ban.Contains(a.SkillId)).ToList();
        return kept.Count > 0 ? kept : null;
    }
}
