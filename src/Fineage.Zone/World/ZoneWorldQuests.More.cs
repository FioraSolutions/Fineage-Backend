using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Rewards;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Applies quest exp/sp and reports whether the character gained a level (Mobius <c>addLevel</c>).</summary>
    bool ApplyProgress(ZoneEntity player, QuestOutcome outcome) => ApplyExpSp(player, outcome.Exp, outcome.Sp);

    /// <summary>
    /// Mobius quest <c>addSpawn(npcId, npc, randomOffset, …)</c> + <c>addAttackDesire</c>:
    /// materialize near the dialog NPC and put hate on the player. No respawn slot — death removes them.
    /// </summary>
    void ApplyQuestSpawns(ZoneEntity player, ZoneEntity anchor, IReadOnlyList<int> npcIds)
    {
        var templates = NpcCatalog.LoadDefault();
        foreach (int npcId in npcIds)
        {
            if (!templates.ById.TryGetValue(npcId, out var template))
            {
                ZoneLog.Info($"quest spawn skip unknown npcId={npcId}");
                continue;
            }

            int objectId = AllocateQuestNpcObjectId();
            // Mobius randomOffset around the talking NPC (~40–80 units).
            double angle = _spawnRandom.NextDouble() * Math.PI * 2;
            int dist = 40 + _spawnRandom.Next(0, 41);
            int x = anchor.X + (int)(Math.Cos(angle) * dist);
            int y = anchor.Y + (int)(Math.Sin(angle) * dist);
            int z = anchor.Z;
            if (_geo.HasGeo(x, y))
                z = _geo.GetHeight(x, y, z);

            var entity = new ZoneEntity
            {
                ObjectId = objectId,
                Kind = ZoneEntityKind.Npc,
                InstanceId = anchor.InstanceId,
                Name = template.Name,
                Title = template.Title,
                NpcType = template.Type,
                X = x,
                Y = y,
                Z = z,
                Heading = anchor.Heading,
                NpcId = template.NpcId,
                Attackable = true,
                MaxHp = template.Hp,
                CurHp = template.Hp,
                MaxMp = template.Mp,
                CurMp = template.Mp,
                Level = template.Level,
                RewardExp = template.Exp,
                RewardSp = template.Sp,
                PAtk = template.PAtk,
                HpRegen = template.HpRegen,
                MpRegen = template.MpRegen,
                PDef = template.PDef,
                PAtkSpd = template.AttackSpeed,
                BaseAccuracy = template.Accuracy,
                BaseEvasion = template.Evasion,
                BaseCritical = template.Critical,
                RandomDamage = template.RandomDamage,
                Dex = template.Dex,
                ShieldDef = template.ShieldDef,
                ShieldRate = template.ShieldRate,
                MAtk = template.MAtk,
                MDef = template.MDef,
                AttackRange = template.AttackRange,
                Aggressive = true,
                AggroRange = Math.Max(template.AggroRange, 1000),
                ClanHelpRange = template.ClanHelpRange,
                Clans = template.Clans,
                IgnoreNpcIds = template.IgnoreNpcIds,
                NpcSoulShotLeft = template.SoulShot,
                NpcSoulShotChance = template.SoulShotChance,
                NpcSpiritShotLeft = template.SpiritShot,
                NpcSpiritShotChance = template.SpiritShotChance,
                DefenceTraits = template.DefenceTraits,
                AiSkills = FilterAttackSkills(template.AiSkills, _npcAiSupport.Get(template.NpcId)?.Suicide),
                MinSkillChance = template.MinSkillChance,
                MaxSkillChance = template.MaxSkillChance,
                AiHealSkills = _npcAiSupport.Get(template.NpcId)?.Heal,
                AiBuffSkills = _npcAiSupport.Get(template.NpcId)?.Buff,
                AiDebuffSkills = _npcAiSupport.Get(template.NpcId)?.Debuff,
                AiImmobilizeSkills = _npcAiSupport.Get(template.NpcId)?.Immobilize,
                AiSilenceSkills = _npcAiSupport.Get(template.NpcId)?.Silence,
                AiSuicideSkills = _npcAiSupport.Get(template.NpcId)?.Suicide,
                RunSpeed = template.RunSpeed,
                WalkSpeed = template.WalkSpeed,
                HomeSet = true,
                HomeX = x,
                HomeY = y,
                HomeZ = z
            };
            _byId.Add(objectId, entity);
            _aoi.Enter(objectId, x, y);
            AddHate(entity, player, hate: 100);
            FanoutNewNpc(entity);
        }
    }

    int AllocateQuestNpcObjectId()
    {
        for (int id = NpcObjectIdAllocator.ExclusiveEnd - 1; id >= NpcObjectIdAllocator.First; id--)
        {
            if (_byId.ContainsKey(id) || _spawnSlots.ContainsKey(id))
                continue;
            return id;
        }

        throw new InvalidOperationException("NPC object id range is exhausted for quest spawns.");
    }

    /// <summary>Test hook: place a talk NPC and run <c>spawn_npc</c> outcomes near it.</summary>
    internal int QuestSpawnNearForTest(ZoneEntity player, int anchorNpcId, int x, int y, int z, IReadOnlyList<int> spawnNpcIds)
    {
        lock (_gate)
        {
            int anchorId = AllocateQuestNpcObjectId();
            var anchor = new ZoneEntity
            {
                ObjectId = anchorId,
                Kind = ZoneEntityKind.Npc,
                NpcId = anchorNpcId,
                Name = "QuestAnchor",
                X = x,
                Y = y,
                Z = z,
                Attackable = false,
                MaxHp = 100,
                CurHp = 100
            };
            _byId.Add(anchorId, anchor);
            _aoi.Enter(anchorId, x, y);
            ApplyQuestSpawns(player, anchor, spawnNpcIds);
            return anchorId;
        }
    }

    /// <summary>Test hook: count live NPCs with the given template id.</summary>
    internal int CountNpcTemplateForTest(int npcId)
    {
        lock (_gate)
            return _byId.Values.Count(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId && e.CurHp > 0);
    }

    /// <summary>Test hook: first live NPC with the given template id.</summary>
    internal ZoneEntity? FindNpcTemplateForTest(int npcId)
    {
        lock (_gate)
            return _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId && e.CurHp > 0);
    }

    /// <summary>
    /// Mobius <c>PlayerStat.addExpAndSp</c> → <c>addExp</c> → <c>addLevel</c>: rolls the exp counter, and when it crosses
    /// a row also raises level, refreshes max HP/MP and refills them. Returns true on a level gain so the caller can run
    /// <see cref="ApplyLevelUp"/> (rewardSkills, penalties, packets).
    /// </summary>
    bool ApplyExpSp(ZoneEntity player, long exp, int sp)
    {
        if (exp == 0 && sp == 0)
            return false;

        var table = _game.Classes?.Experience;
        if (table == null)
        {
            player.Exp = exp > long.MaxValue - player.Exp ? long.MaxValue : player.Exp + exp;
            player.Sp = sp > int.MaxValue - player.Sp ? int.MaxValue : player.Sp + sp;
            return false;
        }

        int previous = player.Level;
        var progress = table.Add(player.Exp, player.Level, player.Sp, exp, sp);
        player.Exp = progress.Exp;
        player.Sp = progress.Sp;
        if (progress.Level == previous || _game.Classes == null || !_game.Classes.TryGet(player.ClassId, out var template) || template == null)
        {
            player.Level = progress.Level;
            return progress.Level != previous;
        }

        player.Level = progress.Level;
        player.MaxHp = template.ModifiedHp(progress.Level);
        player.MaxMp = template.ModifiedMp(progress.Level);
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        return true;
    }

}
