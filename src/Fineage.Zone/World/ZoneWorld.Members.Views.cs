using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Game.Shops;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    bool IsLive(ZoneSession session) =>
        session.InWorld &&
        session.ObjectId != 0 &&
        _byId.TryGetValue(session.ObjectId, out var entity) &&
        ReferenceEquals(entity.Session, session);

    List<ZoneEntity> OrderedVisible(int objectId)
    {
        var visible = new List<ZoneEntity>();
        if (!_byId.TryGetValue(objectId, out var self))
            return visible;
        foreach (int id in _aoi.GetVisible(objectId))
        {
            if (_byId.TryGetValue(id, out var entity) && entity.InstanceId == self.InstanceId)
                visible.Add(entity);
        }

        visible.Sort(static (a, b) =>
        {
            int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.ObjectId.CompareTo(b.ObjectId);
        });
        return visible;
    }

    List<ZoneEntity> Players()
    {
        var players = new List<ZoneEntity>();
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true })
                players.Add(entity);
        }

        return players;
    }

    static List<int> SortedExcept(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (!other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    static List<int> SortedShared(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    InventoryMessages.UserInfoVitals BuildUserInfoVitals(ZoneEntity entity)
    {
        int level = Math.Max(1, entity.Level);
        int maxCp = 0;
        int str = 0, dex = 0, con = 0, intel = 0, wit = 0, men = 0;
        int pAtk = 0, mAtk = 0, pDef = 0, mDef = 0, pAtkSpd = 0, mAtkSpd = 0, accuracy = 0, crit = 0, evasion = 0;
        if (PlayerClasses.TryGet(entity.ClassId, out var template) && template != null)
        {
            try { maxCp = template.ModifiedCp(level); }
            catch (ArgumentOutOfRangeException) { maxCp = template.MaxCp; }
            str = template.Str + entity.HennaStr;
            dex = template.Dex + entity.HennaDex;
            con = template.Con + entity.HennaCon;
            intel = template.Int + entity.HennaInt;
            wit = template.Wit + entity.HennaWit;
            men = template.Men + entity.HennaMen;
            // Same pipeline combat uses (Mobius stat funcs), so the client status window matches the server.
            var stats = PlayerStats(entity);
            pAtk = stats.PAtk;
            mAtk = stats.MAtk;
            pDef = stats.PDef;
            mDef = stats.MDef;
            pAtkSpd = stats.PAtkSpd;
            mAtkSpd = stats.MAtkSpd;
            accuracy = stats.Accuracy;
            crit = stats.CritRate;
            evasion = stats.Evasion;
        }

        return new InventoryMessages.UserInfoVitals(
            level,
            entity.CurHp,
            entity.MaxHp,
            entity.CurMp,
            entity.MaxMp,
            maxCp,
            maxCp,
            entity.Exp,
            entity.Sp,
            str, dex, con, intel, wit, men,
            pAtk, mAtk, pDef, mDef, pAtkSpd, mAtkSpd,
            accuracy, crit, evasion);
    }

    static NpcInfoMsg NpcInfo(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        NpcId = entity.NpcId,
        Name = entity.Name,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Heading = entity.Heading,
        Attackable = entity.Attackable,
        RunSpeed = entity.RunSpeed,
        WalkSpeed = entity.WalkSpeed,
        Title = entity.Title,
        NpcType = entity.NpcType
    };

    /// <summary>
    /// Moves a live player to <paramref name="level"/> and runs the same reward-skills refresh a level up does, so a
    /// test can assert the book and the SkillList fanout without grinding exp.
    /// </summary>
    internal bool TrySetPlayerLevelForTests(ZoneSession session, int level)
    {
        ZoneEntity player;
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var entity) || entity.Inventory == null)
                return false;
            entity.Level = Math.Max(1, level);
            RefreshKnownSkills(entity);
            player = entity;
        }

        // The client only learns the new book from a SkillList, exactly as a level up sends it.
        NetBridge?.FanoutSkillList([session.ObjectId],
            Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        return true;
    }

    internal NpcInfoMsg NpcInfoByTemplateForTests(int npcId)
    {
        lock (_gate)
        {
            ZoneEntity? found = null;
            foreach (var entity in _byId.Values)
            {
                if (entity.Kind != ZoneEntityKind.Npc || entity.NpcId != npcId)
                    continue;
                if (found == null || entity.ObjectId < found.ObjectId)
                    found = entity;
            }

            if (found == null)
                throw new KeyNotFoundException($"NPC template {npcId} is not spawned.");
            return NpcInfo(found);
        }
    }

    internal static SpawnItemMsg SpawnItem(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        ItemId = entity.GroundItemId,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Stackable = entity.GroundStackable,
        Count = entity.GroundCount
    };
}
