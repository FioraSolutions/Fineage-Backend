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
    /// <summary>
    /// All live entities on the session knownlist (players + NPCs + ground items).
    /// </summary>
    internal List<ZoneEntity> SnapshotKnownEntities(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];

            var list = new List<ZoneEntity>(self.Knownlist.Count);
            foreach (int id in self.Knownlist)
            {
                if (!_byId.TryGetValue(id, out var other) || other.InstanceId != self.InstanceId)
                    continue;
                if (other.Kind == ZoneEntityKind.Player && other.Session is not { InWorld: true })
                    continue;
                list.Add(other);
            }

            list.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            return list;
        }
    }

    /// <summary>Object ids on the session's knownlist (any entity kind). For move enter/leave diffs.</summary>
    internal HashSet<int> SnapshotKnownIds(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];
            return new HashSet<int>(self.Knownlist);
        }
    }

    bool TryGetLivePlayerUnlocked(ZoneSession session, out ZoneEntity entity)
    {
        if (session.ObjectId != 0 &&
            _byId.TryGetValue(session.ObjectId, out entity!) &&
            entity.Kind == ZoneEntityKind.Player &&
            ReferenceEquals(entity.Session, session) &&
            session.InWorld)
            return true;
        entity = null!;
        return false;
    }

    /// <summary>Live player by object id (for Net knownlist enter diffs).</summary>
    internal bool TryGetLivePlayerById(int objectId, out ZoneEntity entity)
    {
        lock (_gate)
        {
            if (objectId != 0 &&
                _byId.TryGetValue(objectId, out entity!) &&
                entity.Kind == ZoneEntityKind.Player &&
                entity.Session is { InWorld: true })
                return true;
            entity = null!;
            return false;
        }
    }

    /// <summary>Any world entity by id (player / NPC) for Net knownlist diffs.</summary>
    internal bool TryGetEntityById(int objectId, out ZoneEntity entity)
    {
        lock (_gate)
        {
            if (objectId != 0 && _byId.TryGetValue(objectId, out entity!))
                return true;
            entity = null!;
            return false;
        }
    }

    /// <summary>
    /// Test hook: kill <paramref name="player"/> as if <paramref name="killer"/> had landed the final blow, so the
    /// exp loss and Death Penalty roll run through the production path.
    /// </summary>
    internal void KillPlayerByNpcForTest(ZoneEntity player, ZoneEntity killer)
    {
        lock (_gate)
            ApplyPlayerDeath(player, killer.ObjectId);
    }

    /// <summary>Test hook: rebuild the book after a test mutates <see cref="ZoneEntity.LearnedSkills"/>.</summary>
    internal void RefreshKnownSkillsForTest(ZoneEntity player)
    {
        lock (_gate)
            RefreshKnownSkills(player);
    }

    /// <summary>Test hook: run Mobius <c>checkPlayerSkills</c> without going through a death.</summary>
    internal void DecreaseSkillsOnDelevelForTest(ZoneEntity player)
    {
        lock (_gate)
            DecreaseSkillsOnDelevel(player);
    }
}
