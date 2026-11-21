using Fineage.Game.Quests;

namespace Fineage.Zone.World;

/// <summary>Who raised a quest spawn and when it leaves (Mobius addSpawn despawnDelay + quest timers).</summary>
public sealed class QuestSpawnState
{
    public int OwnerObjectId { get; init; }
    public QuestSummon? Summon { get; init; }
    public long DespawnAtMs { get; set; }
    /// <summary>Saga timers fire even mid-fight; plain quest spawns wait until nobody is fighting them.</summary>
    public bool Strict { get; set; }
    /// <summary>Already on its way out (deleteSpawn / guard): no more hooks, no despawn vars.</summary>
    public bool Leaving { get; set; }
}

public sealed partial class ZoneWorld
{
    /// <summary>Plain <c>spawn_npc</c> monsters and helpers leave after ten idle minutes so quest spawns never pile up.</summary>
    public const int DefaultQuestDespawnSeconds = 600;
    const int QuestDespawnRetryMs = 60_000;

    readonly HashSet<int> _questSpawns = [];

    void TrackQuestSpawn(ZoneEntity entity, ZoneEntity owner, QuestSummon? summon)
    {
        long now = NowUnixMs();
        int seconds = summon is { DespawnSec: > 0 } ? summon.DespawnSec : DefaultQuestDespawnSeconds;
        entity.QuestSpawn = new QuestSpawnState
        {
            OwnerObjectId = owner.ObjectId,
            Summon = summon,
            DespawnAtMs = now + seconds * 1000L,
            Strict = summon is { DespawnSec: > 0 }
        };
        _questSpawns.Add(entity.ObjectId);
    }

    /// <summary>Saga summons: owned, timed, optionally at the player's feet, with a line on arrival.</summary>
    void ApplyQuestSummons(ZoneEntity player, IReadOnlyList<QuestSummon> summons)
    {
        foreach (var summon in summons)
        {
            // One live copy per owner: a re-summon after relog / a stale var never doubles the fight.
            if (summon.Owned && _questSpawns.Any(id => _byId.TryGetValue(id, out var e) && e.NpcId == summon.NpcId &&
                    !e.Dead && e.QuestSpawn is { Leaving: false } q && q.OwnerObjectId == player.ObjectId))
                continue;
            int x = summon.AtPlayer ? player.X : summon.X ?? player.X;
            int y = summon.AtPlayer ? player.Y : summon.Y ?? player.Y;
            int z = summon.AtPlayer ? player.Z : summon.Z ?? player.Z;
            var entity = SpawnQuestNpc(player, summon.NpcId, x, y, z, player.InstanceId, 0, summon);
            if (entity != null && !string.IsNullOrEmpty(summon.Say))
                NpcSay(entity, summon.Say, player.Name);
        }
    }

    /// <summary>Mobius <c>NpcSay</c> (general chat) with <c>PLAYERNAME</c> filled in, heard within 1250.</summary>
    void NpcSay(ZoneEntity speaker, string text, string playerName)
    {
        string line = text.Replace("PLAYERNAME", playerName, StringComparison.Ordinal);
        NpcSaysForTest.Add(line);
        List<int>? netWatchers = null;
        foreach (var entity in Players())
        {
            if (entity.Session != null && HearsLocal(speaker, entity))
                (netWatchers ??= []).Add(entity.ObjectId);
        }

        if (netWatchers != null)
            NetBridge?.FanoutCreatureSay(netWatchers, Line(speaker, Fineage.Protocol.ChatProtocol.All, line));
    }

    internal List<string> NpcSaysForTest { get; } = [];

    ZoneEntity? QuestSpawnOwner(ZoneEntity npc) =>
        npc.QuestSpawn is { } q && _byId.TryGetValue(q.OwnerObjectId, out var owner) && owner.Kind == ZoneEntityKind.Player &&
        owner.Session != null && owner.Inventory != null
            ? owner
            : null;

    void ApplySpawnVars(ZoneEntity npc, IReadOnlyDictionary<string, string>? vars)
    {
        if (vars == null || npc.QuestSpawn?.Summon is not { } summon || QuestSpawnOwner(npc) is not { } owner)
            return;
        if (_quests.ApplySpawnVars(owner.Quests, summon.QuestId, vars))
            SaveQuestProgress(owner.Session!, owner);
    }

    /// <summary>The spawn leaves now: optional line, despawn vars into the owner's row, then gone.</summary>
    void QuestSpawnGone(ZoneEntity npc, string? say, bool applyVars)
    {
        _questSpawns.Remove(npc.ObjectId);
        if (npc.QuestSpawn is { } q)
        {
            q.Leaving = true;
            var owner = QuestSpawnOwner(npc);
            if (!string.IsNullOrEmpty(say))
                NpcSay(npc, say, owner?.Name ?? "");
            if (applyVars)
                ApplySpawnVars(npc, q.Summon?.GoneVars);
        }

        if (_byId.ContainsKey(npc.ObjectId))
            DespawnNpc(npc);
    }

    /// <summary>Mobius <c>deleteSpawn</c> from a step: leave on the next tick, without despawn vars.</summary>
    void LeaveQuietly(ZoneEntity npc)
    {
        if (npc.QuestSpawn is not { } q)
            return;
        q.Leaving = true;
        q.DespawnAtMs = 0;
        q.Strict = true;
    }

    void TickQuestSpawns(long now)
    {
        if (_questSpawns.Count == 0)
            return;
        foreach (int id in _questSpawns.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.QuestSpawn is not { } q || npc.Dead || npc.CurHp <= 0)
            {
                _questSpawns.Remove(id);
                continue;
            }

            if (q.DespawnAtMs > now)
                continue;
            if (!q.Strict && npc.AggroTargetId != 0)
            {
                q.DespawnAtMs = now + QuestDespawnRetryMs;
                continue;
            }

            if (q.Leaving)
                QuestSpawnGone(npc, null, applyVars: false);
            else
                QuestSpawnGone(npc, q.Summon?.GoneSay, applyVars: true);
        }
    }

    /// <summary>Owned spawn died: owner's <c>owned</c> kill steps, then its death vars.</summary>
    void ApplyOwnedKill(ZoneEntity killer, ZoneEntity npc)
    {
        if (npc.QuestSpawn is not { Summon: { Owned: true } summon } q)
            return;
        _questSpawns.Remove(npc.ObjectId);
        if (QuestSpawnOwner(npc) is not { } owner)
            return;
        bool solo = ReferenceEquals(killer, owner) && !_party.InParty(owner.ObjectId);
        if (_quests.HasOwnedSteps(npc.NpcId))
            PublishKill(owner, RecordQuest(owner,
                () => _quests.OnOwned(owner.Quests, owner.Inventory!, npc.NpcId, QuestActorOf(owner), QuestHook.Kill, solo)), npc);
        ApplySpawnVars(npc, summon.DeadVars);
        q.Leaving = true;
    }

    /// <summary>
    /// Hit on an owned spawn. Mobius Archon of Halisha leaves when anyone else, or the owner in a party, hits it;
    /// the owner's hits run <c>owned</c> attack steps (saga mob[2] hit counter).
    /// </summary>
    bool ApplyOwnedAttack(ZoneEntity attacker, ZoneEntity npc)
    {
        if (npc.QuestSpawn is not { Summon: { } summon } q)
            return false;
        if (q.Leaving)
            return true;
        var owner = QuestSpawnOwner(npc);
        bool isOwner = owner != null && ReferenceEquals(attacker, owner);
        bool solo = isOwner && !_party.InParty(owner!.ObjectId);
        if (summon.SoloGuard && !solo)
        {
            if (!string.IsNullOrEmpty(summon.GuardSay))
                NpcSay(npc, summon.GuardSay, attacker.Name);
            ApplySpawnVars(npc, summon.GoneVars);
            LeaveQuietly(npc);
            return true;
        }

        if (summon.Owned && isOwner && _quests.HasOwnedSteps(npc.NpcId))
        {
            var self = owner!;
            var outcome = RecordQuest(self,
                () => _quests.OnOwned(self.Quests, self.Inventory!, npc.NpcId, QuestActorOf(self), QuestHook.Attack, solo));
            PublishKill(self, outcome, npc);
        }

        return false;
    }

    /// <summary>Summons, NPC lines, class change and deleteSpawn requested by a quest step.</summary>
    void ApplyQuestExtras(ZoneSession session, ZoneEntity player, QuestExtras extras, int npcObjectId)
    {
        _byId.TryGetValue(npcObjectId, out var npc);
        if (npc != null)
        {
            foreach (string line in extras.Says)
                NpcSay(npc, line, player.Name);
        }

        if (extras.Summons.Count > 0)
            ApplyQuestSummons(player, extras.Summons);
        if (extras.Despawn && npc?.QuestSpawn != null)
            LeaveQuietly(npc);
        if (extras.ClassChange > 0 && player.ClassId != extras.ClassChange)
        {
            var change = new Fineage.Game.Services.ClassChangeOption("quest", extras.ClassChange, player.ClassId, [], 0, "", "", "", "");
            if (!ApplyClassChange(session, player, change, 0, 0))
                ZoneLog.Info($"quest class-change failed object={player.ObjectId} class={extras.ClassChange}");
        }
    }

    /// <summary>Test hook: run the quest-spawn despawn timers now.</summary>
    internal void TickQuestSpawnsForTest(long? now = null)
    {
        using (_affinity.Step())
            TickQuestSpawns(now ?? NowUnixMs());
    }

    /// <summary>Test hook: kill credit as a dying NPC gives it (owned spawns included).</summary>
    internal void QuestKillEntityForTest(ZoneEntity killer, ZoneEntity npc)
    {
        using (_affinity.Step())
        {
            npc.CurHp = 0;
            ApplyQuestKill(killer, npc);
        }
    }
}
