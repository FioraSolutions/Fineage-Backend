using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Manor;

namespace Fineage.Zone.World;

/// <summary>
/// Sowing and harvesting after the Mobius <c>Seed</c> / <c>Harvester</c> item handlers and the <c>Sow</c> /
/// <c>Harvesting</c> effects: a seed item picks the target and casts Sowing (2097); at hit time the seed is used up and the
/// sow roll decides whether the monster carries the crop; after the kill only the sower's Harvester (2098) takes it.
/// </summary>
public sealed partial class ZoneWorld
{
    static bool IsMonsterType(string npcType) =>
        npcType is "Monster" or "RiftInvader" or "SepulcherMonster" or "FestivalMonster" or "FeedableBeast" or "Chest"
            or "RaidBoss" or "GrandBoss";

    bool SameOrPartyIds(ZoneEntity player, out List<int> ids)
    {
        ids = [player.ObjectId];
        if (_party.Of(player.ObjectId) is { } party)
        {
            ids = [.. party.MemberIds];
            return true;
        }

        return false;
    }

    /// <summary>Mobius <c>Seed.onItemUse</c>: target checks, the castle territory rule, then the Sowing cast.</summary>
    string? TryUseSeed(ZoneSession session, ZoneEntity player, Fineage.Game.ItemInstance item, ManorSeed seed)
    {
        if (_manor.Board.Disabled)
            return ManorProtocol.Disabled;
        int targetId = session.SelectedTargetId;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || target.Kind != ZoneEntityKind.Npc ||
            !VisibleTo(player.ObjectId, targetId))
        {
            Notice(session, ManorProtocol.InvalidTarget, "Invalid target.");
            return ManorProtocol.Target;
        }

        if (!target.Attackable || !IsMonsterType(target.NpcType) || target.NpcType is "RaidBoss" or "GrandBoss" or "Chest")
        {
            Notice(session, ManorProtocol.TargetNotSeedable, "The target is unavailable for seeding.");
            return ManorProtocol.Target;
        }

        if (target.Dead || target.CurHp <= 0)
        {
            Notice(session, ManorProtocol.InvalidTarget, "Invalid target.");
            return ManorProtocol.Target;
        }

        if (target.Seeded)
            return ManorProtocol.Target; // Mobius ActionFailed

        // Mobius MapRegionData.getAreaCastle: the seed belongs to the castle whose territory the sower stands in.
        int areaCastle = _mapRegions.At(player.X, player.Y)?.Castle ?? 0;
        if (seed.CastleId != areaCastle)
        {
            Notice(session, ManorProtocol.SeedNotHere, "This seed may not be sown here.");
            return ManorProtocol.Castle;
        }

        // Mobius Attackable.setSeeded(seed, player): remember the seed and the sower; Sowing makes it stick.
        target.SeedId = seed.SeedId;
        target.SeederObjectId = player.ObjectId;
        return CastManorSkill(session, player, target, ManorProtocol.SowingSkillId, item.ItemId,
            (t, _) => ApplySow(session, player, t));
    }

    /// <summary>Mobius <c>Harvester.onItemUse</c>: a dead monster as the target, then the Harvesting cast.</summary>
    string? TryUseHarvester(ZoneSession session, ZoneEntity player)
    {
        if (_manor.Board.Disabled)
            return ManorProtocol.Disabled;
        int targetId = session.SelectedTargetId;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || target.Kind != ZoneEntityKind.Npc ||
            !IsMonsterType(target.NpcType) || !(target.Dead || target.CurHp <= 0))
        {
            Notice(session, ManorProtocol.InvalidTarget, "Invalid target.");
            return ManorProtocol.Target;
        }

        return CastManorSkill(session, player, target, ManorProtocol.HarvestingSkillId, ManorProtocol.HarvesterItemId,
            (t, _) => ApplyHarvest(session, player, t), allowDeadTarget: true);
    }

    /// <summary>Item skill cast (Mobius <c>player.useMagic</c> from an item handler): MP, reuse, MagicSkillUse fan-out, hit timer.</summary>
    string? CastManorSkill(ZoneSession session, ZoneEntity player, ZoneEntity target, int skillId, int itemId,
        DeferredEffect effect, bool allowDeadTarget = false)
    {
        long now = NowUnixMs();
        var template = _game.Skills.Find(skillId);
        if (template == null || !template.TryLevelData(1, out var levelData))
            return SkillProtocol.Unknown;
        if (player.Dead)
            return SkillProtocol.Dead;
        if (IsCasting(player, now) || IsActionBlocked(player))
            return SkillProtocol.Reuse;
        if (player.SkillReuseUntilMs.TryGetValue(skillId, out long until) && until > now)
            return SkillProtocol.Reuse;
        if (player.CurMp < levelData.MpConsume)
            return SkillProtocol.Mp;
        AdvanceMove(player, now);
        if (!InSkillCastRange(player, target, template.CastRange))
            return SkillProtocol.Range;

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, 1);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[skillId] = now + reuseMs;
        var cast = BuildCast(player, target, skillId, 1, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        ScheduleEffect(player, target, template, 1, now, hitMs, effect, allowDeadTarget);
        ZoneLog.Info($"[manor] cast skill={skillId} item={itemId} caster={player.ObjectId} target={target.ObjectId}");
        return null;
    }

    /// <summary>
    /// Mobius <c>Sow.onStart</c>: a live sowable monster still carrying this sower's seed loses nothing when it cannot be
    /// sown; otherwise one seed is used, the roll decides, and the party (or the sower alone) hears 889 / 890.
    /// </summary>
    void ApplySow(ZoneSession session, ZoneEntity player, ZoneEntity target)
    {
        if (target.Dead || target.CurHp <= 0 || !_manor.Board.Data.CanBeSown(target.NpcId) || target.Seeded ||
            target.SeederObjectId != player.ObjectId)
            return;
        var seed = _manor.Board.Data.Seed(target.SeedId);
        var inventory = player.Inventory;
        if (seed == null || inventory == null)
            return;
        var changes = new List<Fineage.Protocol.InventoryUpdateEntryMsg>();
        if (!Take(inventory, seed.SeedId, 1, changes))
            return;
        FanInventory(session, Collapse(changes));
        SaveInventory(session, inventory);

        int chance = ManorRules.SowChance(seed.Alternative, seed.Level, player.Level, target.Level);
        bool ok = ManorRules.Roll(_manorRandom, chance);
        SameOrPartyIds(player, out var ids);
        if (ok)
        {
            NetBridge?.FanoutPlaySound([player.ObjectId], 0, "ItemSound.quest_itemget", 0, 0, 0, 0, 0);
            target.Seeded = true;
            target.HarvestItemId = seed.CropId;
            target.HarvestCount = ManorRules.HarvestCount(target.Level, seed.Level, _manor.Board.Settings.Rate);
            ClanLine(ids, ManorProtocol.SowSuccess, "The seed was successfully sown.");
        }
        else
        {
            ClanLine(ids, ManorProtocol.SowFailed, "The seed was not sown.");
        }

        ZoneLog.Info($"[manor] sow seed={seed.SeedId} target={target.ObjectId} chance={chance} ok={ok}");
    }

    /// <summary>
    /// Mobius <c>Harvesting.onStart</c>: only the sower (891); a sown corpse rolls the level-gap chance (892 on a miss) and
    /// hands over the crop once (30 / 29, the party hears 1138 / 1137); an unsown corpse answers 893.
    /// </summary>
    void ApplyHarvest(ZoneSession session, ZoneEntity player, ZoneEntity target)
    {
        if (!(target.Dead || target.CurHp <= 0))
            return;
        if (target.SeederObjectId != player.ObjectId)
        {
            Notice(session, ManorProtocol.NoHarvestRights, "You are not authorized to harvest.");
            return;
        }

        if (!target.Seeded)
        {
            Notice(session, ManorProtocol.HarvestNotSown, "The harvest failed because the seed was not sown.");
            return;
        }

        if (!ManorRules.Roll(_manorRandom, ManorRules.HarvestChance(player.Level, target.Level)))
        {
            Notice(session, ManorProtocol.HarvestFailed, "The harvest has failed.");
            return;
        }

        int itemId = target.HarvestItemId, count = target.HarvestCount;
        target.HarvestItemId = 0;
        target.HarvestCount = 0;
        if (itemId <= 0 || count <= 0 || player.Inventory is not { } inventory)
            return;
        GiveItem(session, inventory, itemId, count);
        string name = _game.Items.TryGet(itemId, out var t) ? t.Name : itemId.ToString();
        if (count == 1)
            SelfLine(session, ManorProtocol.ObtainedOne, $"You have obtained {name}.", name);
        else
            SelfLine(session, ManorProtocol.ObtainedMany, $"You have obtained {count} {name}.", name, count.ToString());
        if (SameOrPartyIds(player, out var ids))
        {
            var others = ids.Where(id => id != player.ObjectId).ToArray();
            if (count == 1)
                ClanLine(others, ManorProtocol.PartyHarvestedOne, $"{player.Name} harvested {name}.", player.Name, name);
            else
                ClanLine(others, ManorProtocol.PartyHarvestedMany, $"{player.Name} harvested {count} {name}.", player.Name,
                    count.ToString(), name);
        }

        ZoneLog.Info($"[manor] harvest target={target.ObjectId} by={player.ObjectId} item={itemId} x{count}");
    }
}

public sealed partial class ZoneWorld
{
    /// <summary>Test hook: spawn a template monster at a point (no respawn bookkeeping beyond the normal spawn).</summary>
    internal ZoneEntity SpawnMonsterForTests(int npcId, int x, int y, int z)
    {
        lock (_gate)
        {
            var template = Fineage.Zone.Npcs.NpcCatalog.LoadDefault().ById[npcId];
            int objectId = AllocateQuestNpcObjectId();
            MaterializeSpawn(Fineage.Zone.Spawns.SpawnCatalog.FromTemplate(objectId, template, x, y, z, 0, attackable: true), _activeMapId);
            var npc = _byId[objectId];
            npc.HomeSet = false;
            FanoutNewNpc(npc);
            return npc;
        }
    }
}
