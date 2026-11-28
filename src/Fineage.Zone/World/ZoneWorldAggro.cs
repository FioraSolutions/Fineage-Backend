using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius AggroList, aggressive scan, clan help and attack stance:
/// <list type="bullet">
/// <item>Attackable.addDamageHate: hate += damage·100/(level+7) (min 1); the mob attacks its most hated valid attacker.</item>
/// <item>AttackableAI.thinkActive: an aggressive mob (isAggressive, aggroRange &gt; 0) picks a live player inside aggroRange (3D).</item>
/// <item>AttackableAI.thinkAttack: same-clan mobs inside clanHelpRange that are idle join with 1 hate on the target.</item>
/// <item>AttackStanceTaskManager: AutoAttackStart on entering combat, AutoAttackStop after 15 s idle / when a mob drops its target.</item>
/// </list>
/// </summary>
public sealed partial class ZoneWorld
{
    const int AggroScanIntervalMs = 500;
    const int ClanCallIntervalMs = 1000;
    /// <summary>Mobius <c>MAX_ATTACK_TIMEOUT = 1200</c> game ticks (100 ms), two minutes.</summary>
    const int AttackTimeoutMs = 120_000;

    /// <summary>Mobius hate for <paramref name="damage"/> dealt to an NPC of <paramref name="npcLevel"/>.</summary>
    internal static long DamageHate(int damage, int npcLevel) => Math.Max(1, (long)damage * 100 / (Math.Max(1, npcLevel) + 7));

    /// <summary>
    /// Mobius <c>Attackable.addDamageHate</c> bookkeeping of <c>AggroInfo.getDamage()</c>: the kill reward is shared
    /// over every attacker in proportion to what it actually dealt.
    /// </summary>
    static void RecordDamage(ZoneEntity npc, ZoneEntity attacker, int damage)
    {
        if (npc.Kind != ZoneEntityKind.Npc || attacker.Kind != ZoneEntityKind.Player || damage <= 0)
            return;
        npc.DamageBy[attacker.ObjectId] = npc.DamageBy.GetValueOrDefault(attacker.ObjectId) + damage;
    }

    void AddHate(ZoneEntity npc, ZoneEntity attacker, long hate = 1, bool allowNpcAttacker = false)
    {
        if (npc.Kind != ZoneEntityKind.Npc || npc.Summon != null || npc.CurHp <= 0)
            return;
        if (!allowNpcAttacker && attacker.Kind != ZoneEntityKind.Player && attacker.Summon == null)
            return;
        // Mobius FolkStatus: merchants/trainers/folk never hate. Guards/Monsters (Attackable types) do.
        if (!ZoneCombat.TakesNpcCombatDamage(npc.NpcType, npc.Attackable))
            return;
        // Mobius onEvtAttacked: _globalAggro = 0 and the attack timeout restarts (1200 game ticks, 2 min).
        if (npc.GlobalAggro < 0)
            npc.GlobalAggro = 0;
        npc.AttackTimeoutAtMs = NowUnixMs() + AttackTimeoutMs;
        npc.HateList[attacker.ObjectId] = npc.HateList.GetValueOrDefault(attacker.ObjectId) + Math.Max(1, hate);
        int top = MostHated(npc);
        if (top == npc.AggroTargetId || top == 0)
            return;
        SetAggroTarget(npc, top);
        // Mobius: first swing after attack-speed delay, not instantly.
        npc.NextMobAttackMs = NowUnixMs() + ZoneCombat.HitTimeMs(NpcPAtkSpd(npc));
    }

    /// <summary>Mobius <c>Guard.addDamage</c>: nearby guards within 500 add hate 10 on the attacker.</summary>
    void CallGuardHelp(ZoneEntity guard, ZoneEntity attacker)
    {
        if (!IsGuard(guard) || guard.CurHp <= 0)
            return;
        var credit = CreditOf(attacker);
        if (credit.Kind != ZoneEntityKind.Player && credit.Summon == null)
            return;
        const int range = 500;
        const long helpHate = 10;
        foreach (int id in _aoi.GetVisible(guard.ObjectId))
        {
            if (!_byId.TryGetValue(id, out var other) || ReferenceEquals(other, guard) || other.CurHp <= 0 || !IsGuard(other))
                continue;
            if (!ZoneCombat.Within(guard.X, guard.Y, other.X, other.Y, range))
                continue;
            Post(new HateMessage(other.ObjectId, credit.ObjectId, helpHate));
        }
    }

    /// <summary>
    /// Hate list only, no AI wake. Mobius <c>addDamageHate</c> on a skill hit writes aggro without
    /// <c>notifyActionAttacked</c> (which is what actually sets intention ATTACK). Used for skills
    /// that suppress the attack notify (EffectType.HATE, TURN_PASSIVE, Lure/Temptation).
    /// </summary>
    void RecordHate(ZoneEntity npc, ZoneEntity attacker, long hate)
    {
        if (npc.Kind != ZoneEntityKind.Npc || npc.Summon != null || npc.CurHp <= 0 ||
            (attacker.Kind != ZoneEntityKind.Player && attacker.Summon == null))
            return;
        if (!ZoneCombat.TakesNpcCombatDamage(npc.NpcType, npc.Attackable))
            return;
        npc.HateList[attacker.ObjectId] = npc.HateList.GetValueOrDefault(attacker.ObjectId) + Math.Max(1, hate);
    }

    /// <summary>
    /// Mobius <c>AttackableAI.checkAggroCondition</c> / <c>AggroInfo.checkHate</c>: <c>target.isAlikeDead()</c> covers
    /// fake death (<c>Player.isAlikeDead()</c> includes <c>_isFakeDeath</c>), so a playing-dead pawn is never a hate
    /// target and its hate is zeroed — the mob drops it until it stands up and is hit again.
    /// </summary>
    /// <summary>
    /// Template <c>ai aggroRange</c> for the proactive scan. Mobius resolves an absent
    /// <c>&lt;ai aggroRange&gt;</c> to 0, so 0 is non-proactive retail data (raid minions, scripted mobs),
    /// not a missing field. <see cref="NpcCatalog"/> rejects aggressive JSON rows without the field.
    /// </summary>
    internal bool TryGetProactiveAggroRange(ZoneEntity npc, out int aggroRange)
    {
        aggroRange = npc.AggroRange;
        return aggroRange > 0;
    }

    bool ValidHateTarget(ZoneEntity npc, int targetId) =>
        _byId.TryGetValue(targetId, out var t) && !t.FakeDeath &&
        SameInstance(npc.ObjectId, t.ObjectId) && ZoneCombat.WithinAggroForgetRange(npc.X, npc.Y, t.X, t.Y) &&
        IsValidMobAttackTarget(npc, t);

    /// <summary>A connected living player, or a living summon (Mobius <c>isPlayable</c>).</summary>
    static bool IsLivePlayable(ZoneEntity p) =>
        !p.Dead && p.CurHp > 0 &&
        ((p.Kind == ZoneEntityKind.Player && p.Session != null) || (p.Kind == ZoneEntityKind.Npc && p.Summon != null));

    /// <summary>Most hated valid attacker (drops invalid entries); 0 when the list is empty.</summary>
    internal int MostHated(ZoneEntity npc)
    {
        int best = 0;
        long bestHate = long.MinValue;
        List<int>? stale = null;
        foreach (var (id, hate) in npc.HateList)
        {
            if (!ValidHateTarget(npc, id))
            {
                (stale ??= []).Add(id);
                continue;
            }
            if (hate > bestHate)
            {
                best = id;
                bestHate = hate;
            }
        }
        if (stale != null)
            foreach (int id in stale)
                npc.HateList.Remove(id);
        return best;
    }

    /// <summary>
    /// Mobius <c>Attackable.reduceHate(target, amount)</c>: <c>AggroInfo.addHate(amount)</c> against the entry —
    /// a hate-reducing skill (a positive <c>effectPoint</c>, e.g. Trick 11 at the attacker's end of the trade) lowers
    /// the number, and a zero/negative most-hated entry clears the fight and returns the pawn to idle.
    /// </summary>
    void ReduceHate(ZoneEntity npc, ZoneEntity target, long amount)
    {
        if (npc.Kind != ZoneEntityKind.Npc || amount <= 0)
            return;
        if (!npc.HateList.TryGetValue(target.ObjectId, out long hate))
            return;
        long left = hate - amount;
        if (left > 0)
        {
            npc.HateList[target.ObjectId] = left;
            return;
        }
        npc.HateList.Remove(target.ObjectId);
        int next = MostHated(npc);
        SetAggroTarget(npc, next);
        if (next > 0 || npc.ReturningHome)
            return;
        // Mobius `Attackable.reduceHate`: nothing left to hate → `AI_INTENTION_ACTIVE` and gait back to walking —
        // the same shared disengage as a lost target (walk home only past MaxDriftRange).
        GoActiveAfterFight(npc, NowUnixMs());
    }

    /// <summary>Mobius <c>GeoEngine.canSeeTarget(creature, target)</c>: no closed door between, then geo LOS.</summary>
    internal bool CanSeeTarget(ZoneEntity from, ZoneEntity to) => CanSeePoint(from, to.X, to.Y, to.Z);

    bool CanSeePoint(ZoneEntity from, int x, int y, int z)
    {
        if (Doors.FirstBlocking(from.X, from.Y, from.Z, x, y, z, out _, out _, out _) != null)
            return false;
        return _geo.CanSeeTarget(from.X, from.Y, from.Z, x, y, z);
    }

    public const int SysMsgCannotSeeTarget = 181;

    void CannotSeeTarget(ZoneSession session)
    {
        if (_byId.TryGetValue(session.ObjectId, out var player))
        {
            player.Intent.AutoAttackTargetId = 0;
            player.Intent.ClearApproach();
        }
        ShotNotice(session, SysMsgCannotSeeTarget, "Cannot see target.");
    }

    static bool IsGuard(ZoneEntity npc) =>
        string.Equals(npc.NpcType, "Guard", StringComparison.Ordinal);

    /// <summary>Mobius clan help: idle same-clan mobs within clanHelpRange of <paramref name="caller"/> attack <paramref name="target"/>.</summary>
    void CallClanHelp(ZoneEntity caller, ZoneEntity target, long now)
    {
        if (caller.Clans == null || caller.ClanHelpRange <= 0 || now < caller.NextClanCallMs)
            return;
        caller.NextClanCallMs = now + ClanCallIntervalMs;
        foreach (int id in _aoi.GetVisible(caller.ObjectId))
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            if (other.Kind != ZoneEntityKind.Npc || ReferenceEquals(other, caller) || other.CurHp <= 0 || !other.Attackable ||
                other.AggroTargetId > 0 || other.Clans == null || !other.Clans.Any(c => caller.Clans.Contains(c)) ||
                (caller.IgnoreNpcIds?.Contains(other.NpcId) ?? false) ||
                !ZoneCombat.Within(caller.X, caller.Y, other.X, other.Y, caller.ClanHelpRange) ||
                !SameInstance(caller.ObjectId, other.ObjectId))
                continue;
            ZoneLog.Info($"[aggro] clan help npc={other.ObjectId} ({other.Name}) for npc={caller.ObjectId} on player={target.ObjectId}");
            // The helper may be owned by another region: the hate lands at the stage barrier (Mobius addDamageHate 1).
            Post(new HateMessage(other.ObjectId, target.ObjectId, 1));
        }
    }

    /// <summary>Fans Net AutoAttackStart/Stop when an entity's stance changes (players: combat window; NPCs: has a target).</summary>
    void TickAttackStance(long now)
    {
        if (NetBridge == null)
            return;
        foreach (var e in Players())
            NoteStance(e, now);
        foreach (int id in Active(ActiveSets.AiBusy))
        {
            if (_byId.TryGetValue(id, out var npc))
                NoteStance(npc, now);
        }
    }

    void NoteStance(ZoneEntity e, long now)
    {
        bool inStance = e.Kind switch
        {
            ZoneEntityKind.Player => !e.Dead && e.CombatUntilUnixMs > now,
            ZoneEntityKind.Npc => e.CurHp > 0 && e.AggroTargetId > 0,
            _ => false
        };
        if (inStance == e.StanceShown)
            return;
        e.StanceShown = inStance;
        var watchers = NetWatchersOf(e);
        if (watchers.Count > 0)
            NetBridge!.FanoutAttackStance(watchers, e.ObjectId, inStance);
    }
}
