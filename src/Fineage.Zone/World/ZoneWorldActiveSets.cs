using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius-style scheduling registries (ThinkTaskManager / MovementTaskManager analogues).
/// Hot ticks iterate these sets only — never the full <c>_byId</c> table on an idle world.
/// Mutations stay under <c>_gate</c> with the entity fields they mirror.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Entities with <see cref="ZoneEntity.MoveActive"/> (players + NPCs).</summary>
    readonly HashSet<int> _moving = [];
    /// <summary>NPCs with aggro target and/or returning home (Mobius AI task membership).</summary>
    readonly HashSet<int> _aiBusy = [];
    /// <summary>Entities with a queued <see cref="ZoneEntity.PendingHit"/>.</summary>
    readonly HashSet<int> _pendingHit = [];
    /// <summary>NPC corpses with <see cref="ZoneEntity.DecayAtMs"/> &gt; 0.</summary>
    readonly HashSet<int> _decaying = [];
    /// <summary>Entities that still need HP/MP regen this sim tick.</summary>
    readonly HashSet<int> _needRegen = [];
    /// <summary>Entities with at least one abnormal (buff/debuff) to expire.</summary>
    readonly HashSet<int> _withAbnormals = [];

    /// <summary>Test/diagnostics: sizes of the Mobius-style active registries.</summary>
    internal (int Moving, int AiBusy, int PendingHit, int Decaying, int NeedRegen, int WithAbnormals) ActiveSetCounts =>
        (_moving.Count, _aiBusy.Count, _pendingHit.Count, _decaying.Count, _needRegen.Count, _withAbnormals.Count);

    internal void SetMoveActive(ZoneEntity entity, bool active)
    {
        entity.MoveActive = active;
        if (active)
            _moving.Add(entity.ObjectId);
        else
            _moving.Remove(entity.ObjectId);
    }

    internal void SetReturningHome(ZoneEntity npc, bool returning)
    {
        npc.ReturningHome = returning;
        SyncAiBusy(npc);
    }

    internal void SetAggroTarget(ZoneEntity npc, int targetId)
    {
        npc.AggroTargetId = targetId;
        SyncAiBusy(npc);
    }

    void SyncAiBusy(ZoneEntity npc)
    {
        if (npc.Kind != ZoneEntityKind.Npc)
            return;
        if (npc.AggroTargetId > 0 || npc.ReturningHome)
            _aiBusy.Add(npc.ObjectId);
        else
            _aiBusy.Remove(npc.ObjectId);
    }

    internal void SetPendingHit(ZoneEntity entity, PendingSkillHit? hit)
    {
        entity.PendingHit = hit;
        if (hit != null)
            _pendingHit.Add(entity.ObjectId);
        else
            _pendingHit.Remove(entity.ObjectId);
    }

    internal void SetDecayAtMs(ZoneEntity npc, long decayAtMs)
    {
        npc.DecayAtMs = decayAtMs;
        if (decayAtMs > 0)
            _decaying.Add(npc.ObjectId);
        else
            _decaying.Remove(npc.ObjectId);
    }

    internal void SyncNeedRegen(ZoneEntity entity)
    {
        bool need = !entity.Dead
            && entity.CurHp > 0
            && (entity.CurHp < entity.MaxHp || entity.CurMp < entity.MaxMp);
        if (need)
            _needRegen.Add(entity.ObjectId);
        else
            _needRegen.Remove(entity.ObjectId);
    }

    void ForgetActiveSets(int objectId)
    {
        _moving.Remove(objectId);
        _aiBusy.Remove(objectId);
        _pendingHit.Remove(objectId);
        _decaying.Remove(objectId);
        _needRegen.Remove(objectId);
        _withAbnormals.Remove(objectId);
    }
}
