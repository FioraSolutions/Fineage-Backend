using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TrySelectTargetAsync(ZoneSession session, SelectTargetMsg select, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TrySelectTarget(session, select.TargetObjectId, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryCancelTargetAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out _))
                return "world";

            int previous = session.SelectedTargetId;
            session.SelectedTargetId = 0;
            session.AutoAttackTargetId = 0;
            session.ClearApproach();
            toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg { ObjectId = 0, ColorHint = 0 }));
            NetBridge?.FanoutTargetUnselected([session.ObjectId], new Fineage.Net.Contract.TargetUnselectedNetMsg
            {
                TargetObjectId = previous
            });
        }

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryAttackAsync(ZoneSession session, AttackRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        long approachMs = 0;
        lock (_gate)
        {
            int previous = session.AutoAttackTargetId;
            long now0 = _clock().ToUnixTimeMilliseconds();
            int requested = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
            bool alreadySwinging = previous > 0 && previous == requested && session.ApproachTargetId == 0 &&
                session.NextAutoAttackUnixMs > now0;
            // Re-click on the target already being hit: Mobius keeps the running attack, no extra swing.
            error = alreadySwinging ? null : TryAttack(session, request.TargetObjectId, outbound, out approachMs);
            if (error == null && !alreadySwinging && approachMs >= 0)
            {
                session.AutoAttackTargetId = session.SelectedTargetId;
                // Approaching: first swing on arrival (Mobius onEvtArrived -> doAttack); else next swing after Pspd.
                session.NextAutoAttackUnixMs = _clock().ToUnixTimeMilliseconds() +
                    (approachMs > 0 ? approachMs : PlayerAttackTimeMs(session.ObjectId));
            }
        }
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TrySelectTarget(ZoneSession session, int targetObjectId, List<Pending> toSelf)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (!_byId.TryGetValue(targetObjectId, out var target))
            return "target";

        // Interlude: click own pawn → target self (AOI never lists the observer).
        if (targetObjectId == session.ObjectId)
        {
            if (player.Dead || player.CurHp <= 0)
                return "target";

            session.SelectedTargetId = targetObjectId;
            toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
            {
                ObjectId = targetObjectId,
                ColorHint = 0
            }));
            return null;
        }

        // Mobius setTarget accepts any live Creature: friendly NPCs (guides, merchants) select for talk UI,
        // Attackable is attack-only, and other players select so their nameplate and target window work.
        if (target.Kind is not (ZoneEntityKind.Npc or ZoneEntityKind.Player) || target.CurHp <= 0)
            return "target";
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return "target";

        session.SelectedTargetId = targetObjectId;
        PublishTargetSelection(session, player, target, toSelf);
        return null;
    }

    /// <summary>
    /// Mobius <c>Player.setTarget</c>: a click on a creature is answered with the nameplate colour
    /// (<c>MyTargetSelected</c>, colour 0 for anything the player cannot auto-attack) and the target's HP for the
    /// target window (<c>StatusUpdate</c> MAX_HP + CUR_HP). Peers also learn what this player is aiming at
    /// (<c>TargetSelected</c> broadcast), so a nearby client can show the aim marker.
    /// </summary>
    void PublishTargetSelection(ZoneSession session, ZoneEntity player, ZoneEntity target, List<Pending> toSelf)
    {
        int colour = InterludeStatus.TargetColor(player.Level, target.Level, IsAutoAttackable(target));
        toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
        {
            ObjectId = target.ObjectId,
            ColorHint = colour
        }));
        toSelf.Add(Pack(session, MsgType.StatusUpdate, new StatusUpdateMsg
        {
            ObjectId = target.ObjectId,
            AttributeIds = [InterludeStatus.MaxHp, InterludeStatus.CurHp],
            Values = [target.MaxHp, target.CurHp]
        }));

        if (NetBridge == null)
            return;

        int[] self = [player.ObjectId];
        NetBridge.FanoutStatusUpdate(self, new Fineage.Net.Contract.StatusUpdateNetMsg
        {
            EntityValue = (ulong)(uint)target.ObjectId,
            EntityGeneration = 1,
            AttributeIds = [InterludeStatus.MaxHp, InterludeStatus.CurHp],
            Values = [target.MaxHp, target.CurHp]
        });
        // Net has no MyTargetSelected opcode: TargetSelected (0x030F) is its self-side mirror, so it carries the
        // same id + colour. Mobius also broadcasts TargetSelected to peers; that fanout stays out of scope here.
        NetBridge.FanoutTargetSelected(self, new Fineage.Net.Contract.TargetSelectedNetMsg
        {
            TargetObjectId = target.ObjectId,
            ColorHint = colour
        });
    }

    /// <summary>
    /// Mobius <c>Creature.isAutoAttackable(Player)</c> for the entities this zone carries: NPCs follow their
    /// template flag; a player is only auto-attackable when hostile (PvP-flagged or carrying karma). Everything
    /// else is not, so merchants, guides and friendly players all come back white (colour 0).
    /// </summary>
    static bool IsAutoAttackable(ZoneEntity target) =>
        target.Kind switch
        {
            ZoneEntityKind.Npc => target.Attackable,
            ZoneEntityKind.Player => target.PvpFlagged || target.PkKills > 0,
            _ => false
        };

    /// <summary>
    /// Mobius <c>Creature.isAttackable</c> for a player's swing. NPCs follow the template flag; another player is
    /// attackable only while PvP-flagged or carrying karma (Mobius <c>isAutoAttackable(Player)</c>), and never the
    /// attacker itself. This is the gate <c>D4</c> was missing: it used to reject every non-NPC outright.
    /// </summary>
    static bool IsAttackable(ZoneEntity attacker, ZoneEntity target)
    {
        if (target.Dead || target.CurHp <= 0)
            return false;
        return target.Kind switch
        {
            ZoneEntityKind.Npc => target.Attackable,
            ZoneEntityKind.Player => !ReferenceEquals(attacker, target) && IsAutoAttackable(target),
            _ => false
        };
    }

    string? TryAttack(ZoneSession session, int requestedTargetId, List<Pending> outbound) =>
        TryAttack(session, requestedTargetId, outbound, out _);

    /// <summary>
    /// Interlude RequestAttack: in range -> swing; out of range -> server moveToPawn (run to the target,
    /// MoveToAck to self + watchers) and the swing fires from <see cref="TickAutoAttacks"/> on arrival.
    /// <paramref name="approachMs"/> is the travel time when an approach was started, else 0.
    /// </summary>
    string? TryAttack(ZoneSession session, int requestedTargetId, List<Pending> outbound, out long approachMs)
    {
        approachMs = 0;
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (player.CastingUntilMs > _clock().ToUnixTimeMilliseconds())
            return null; // Mobius: auto-attack waits while casting

        int targetId = requestedTargetId > 0 ? requestedTargetId : session.SelectedTargetId;
        if (targetId <= 0)
            return "attack";
        if (requestedTargetId > 0)
            session.SelectedTargetId = targetId;

        if (!_byId.TryGetValue(targetId, out var target))
            return "attack";
        if (!IsAttackable(player, target))
            return "attack";
        if (!VisibleTo(player.ObjectId, targetId))
            return "attack";
        AdvanceMove(player, _clock().ToUnixTimeMilliseconds());
        if (!ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player)))
        {
            approachMs = ApproachTarget(player, target, PlayerReach(player), outbound);
            return approachMs < 0 ? "attack" : null;
        }
        if (player.MoveActive)
        {
            // Mobius onEvtArrived: in reach -> stop the run and swing from here (no slide past the target).
            StopTimedMove(player);
            SyncSession(player, session);
        }
        session.ClearApproach();
        // Mobius Creature.doAttack: no LOS → SysMsg 181 "Cannot see target.", intention ACTIVE, ActionFailed.
        if (!CanSeeTarget(player, target))
        {
            CannotSeeTarget(session, outbound);
            approachMs = -1;
            return null;
        }

        var ps = PlayerStats(player);
        // Mobius doAttack: a bow fires only with an arrow of the matching grade equipped; the arrow is spent on the
        // shot itself (hit or miss) — Creature.canUseRangeWeapon -> checkAndEquipArrows, doAttackHitByBow ->
        // reduceArrowCount.
        if (ps.Weapon == Fineage.Game.WeaponType.Bow)
        {
            var bow = player.Inventory?.GetEquipped(PaperdollSlot.RHand)?.Template;
            if (bow != null && EnsureBowArrow(player, bow, outbound) == null)
            {
                OutOfArrows(session, outbound);
                approachMs = -1;
                return null;
            }
        }
        // Mobius doAttack: a charged weapon fires its soulshot on this swing (hit or miss) and is uncharged.
        bool soulshot = player.SoulshotCharged;
        player.SoulshotCharged = false;
        if (soulshot)
            RechargeAutoShots(player, outbound);
        // Mobius doAttack turns the attacker to its target; the target's own facing decides back/side/front.
        player.Heading = ZoneCombat.HeadingFrom(player.X, player.Y, target.X, target.Y);
        var (damage, flags) = RollPlayerMelee(player, ps, target, soulshot);

        var attack = new AttackMsg
        {
            AttackerObjectId = player.ObjectId,
            TargetObjectId = targetId,
            Damage = damage,
            Flags = flags,
            AttackerX = player.X,
            AttackerY = player.Y,
            AttackerZ = player.Z,
            TargetX = target.X,
            TargetY = target.Y,
            TargetZ = target.Z
        };

        // Swing now; HP drops at hitTime (Mobius onHitTimer).
        FanoutAttack(player, target, attack, outbound);
        QueueAttackerDamageLine(player, target, damage);
        if (ps.Weapon == Fineage.Game.WeaponType.Bow)
            ConsumeBowArrow(player, outbound);
        long now = _clock().ToUnixTimeMilliseconds();
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        _meleeHits.Add(new PendingMeleeHit(player.ObjectId, targetId, damage, now + PlayerAttackTimeMs(player.ObjectId) / 2));

        return null;
    }

    // Physical stats. Players: class template values (same as UserInfo); NPCs: Mobius npcs.json.
    // TODO(combat parity): STR/level/weapon/armor/buff stat pipeline.
    int PlayerPAtkSpd(ZoneEntity player) => PlayerStats(player).PAtkSpd;

    long PlayerAttackTimeMs(int objectId) =>
        _byId.TryGetValue(objectId, out var p) ? ZoneCombat.AttackTimeMs(PlayerPAtkSpd(p)) : ZoneCombat.AutoAttackIntervalMs;

    double PlayerPAtk(ZoneEntity player) => PlayerStats(player).PAtk;

    double PlayerPDef(ZoneEntity player) => PlayerStats(player).PDef;

    int PlayerReach(ZoneEntity player) => ZoneCombat.PlayerAttackReach(PlayerStats(player).AttackRange);

    /// <summary>
    /// One physical swing from a player. A monster target rolls against its template P.Def / evasion / shield; a player
    /// target rolls against the same player stat pipeline an incoming mob swing uses (<see cref="PlayerStats"/>), so a
    /// duel is the same math as PvE with the target's own defence. Mobius applies the same
    /// <c>calcPhysicalDam</c>, only the stats behind it change.
    /// </summary>
    (int Damage, byte Flags) RollPlayerMelee(ZoneEntity player, PlayerCombatStats ps, ZoneEntity target, bool soulshot)
    {
        double pDef;
        double evasion;
        double shieldRate;
        int shieldDef;
        if (target.Kind == ZoneEntityKind.Npc)
        {
            pDef = NpcPDef(target);
            evasion = NpcEvasion(target);
            shieldRate = NpcShieldRate(target);
            shieldDef = target.ShieldDef;
        }
        else
        {
            var ts = PlayerStats(target);
            pDef = Math.Max(1d, ts.PDef);
            evasion = ts.Evasion;
            shieldRate = ts.ShieldRate;
            shieldDef = ts.ShieldDef;
        }

        return ZoneCombat.RollMelee(_combatRandom, ps.PAtk, pDef,
            ps.Accuracy, evasion, ps.CritRate, ps.RandomDamage,
            soulshot, (int)ps.WeaponGrade, shieldRate, shieldDef, ps.Weapon == Fineage.Game.WeaponType.Bow,
            ZoneCombat.HitConditionBonus(player.X, player.Y, player.Z, target.X, target.Y, target.Z, target.Heading));
    }

    double NpcPDef(ZoneEntity npc) =>
        Math.Max(1d, StatMods(npc).Apply(CombatStat.PDef, npc.PDef > 0 ? npc.PDef : Math.Max(1, npc.Level) * 40d));

    int NpcPAtkSpd(ZoneEntity npc) =>
        Math.Max(1, StatMods(npc).Apply(CombatStat.PAtkSpd, npc.PAtkSpd > 0 ? npc.PAtkSpd : ZoneCombat.DefaultNpcPAtkSpd));

    (int Damage, byte Flags) NpcMeleeRoll(ZoneEntity npc, ZoneEntity player)
    {
        if (npc.PAtk <= 0)
            return (ZoneCombat.MobMeleeDamage(npc.Level), 0);
        var ps = PlayerStats(player);
        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, player.X, player.Y);
        return ZoneCombat.RollMelee(_combatRandom, StatMods(npc).Apply(CombatStat.PAtk, npc.PAtk), ps.PDef, NpcAccuracy(npc), ps.Evasion,
            NpcCritPermille(npc), npc.RandomDamage, false, 0, ps.ShieldRate, ps.ShieldDef, false,
            ZoneCombat.HitConditionBonus(npc.X, npc.Y, npc.Z, player.X, player.Y, player.Z, player.Heading));
    }

    Task? _combatLoop;

    /// <summary>One live combat-loop tick (movement, hits, player auto-attack / approach, casts, mobs, corpses).</summary>
    internal async Task RunPlayerCombatTickAsync(CancellationToken ct)
    {
        var outbound = new List<Pending>();
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            TickPlayerMovement(now, outbound);
            TickNpcMovement(now, outbound);
            TickPendingHits(now, outbound);
            TickMeleeHits(now, outbound);
            TickAutoAttacks(outbound);
            TickPendingCasts(now, outbound);
            TickQueuedMoves(now, outbound);
            TickPendingPickups(now, outbound);
            TickMobAttacks(now, outbound);
            TickCorpseDecay(now, outbound);
            TickGroundExpiry(now, outbound);
        }
        if (outbound.Count > 0)
            await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    async Task CombatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ZoneCombat.AutoAttackTickMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RunPlayerCombatTickAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void TickAutoAttacks(List<Pending> outbound)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.AutoAttackTargetId <= 0)
                continue;
            AdvanceMove(player, now); // Net timed approach: measure reach from where the pawn really is now
            int targetId = session.AutoAttackTargetId;
            if (player.Dead || !_byId.TryGetValue(targetId, out var target) || target.CurHp <= 0 || session.SelectedTargetId != targetId)
            {
                session.AutoAttackTargetId = 0;
                session.ClearApproach();
                continue;
            }
            if (!VisibleTo(player.ObjectId, targetId))
            {
                session.AutoAttackTargetId = 0;
                session.ClearApproach();
                continue;
            }

            // Mid-run and the pawn moved: re-send MoveToPawn toward its new spot (Mobius follow).
            if (session.ApproachTargetId == targetId && now < session.ApproachArriveUnixMs)
            {
                if (!ZoneCombat.Within(target.X, target.Y, session.ApproachTargetX, session.ApproachTargetY, ZoneCombat.ApproachRepathDistance))
                {
                    long re = ApproachTarget(player, target, PlayerReach(player), outbound);
                    if (re < 0)
                    {
                        session.AutoAttackTargetId = 0;
                        session.ClearApproach();
                        SendActionFailed(player, outbound);
                        continue;
                    }
                    session.NextAutoAttackUnixMs = now + re;
                }
                continue;
            }

            if (now < session.NextAutoAttackUnixMs)
                continue;
            if (!ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player)))
            {
                // Target moved away: Mobius re-issues moveToPawn and keeps the attack intention.
                long ms = ApproachTarget(player, target, PlayerReach(player), outbound);
                if (ms < 0)
                {
                    session.AutoAttackTargetId = 0;
                    session.ClearApproach();
                    SendActionFailed(player, outbound);
                    continue;
                }
                session.NextAutoAttackUnixMs = now + ms;
                continue;
            }
            session.ClearApproach();
            session.NextAutoAttackUnixMs = now + PlayerAttackTimeMs(player.ObjectId);
            if (TryAttack(session, targetId, outbound) != null)
                session.AutoAttackTargetId = 0;
        }
    }

    static StatusUpdateMsg StatusFor(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
        Values = [entity.CurHp, entity.MaxHp]
    };

    readonly List<PendingMeleeHit> _meleeHits = new();

    void QueueAttackerDamageLine(ZoneEntity attacker, ZoneEntity target, int damage)
    {
        // Mobius YOU_HIT_S1_FOR_S2_DAMAGE goes to the attacker only, never to watchers.
        if (damage <= 0 || attacker.Session == null || NetBridge == null)
            return;

        NetBridge.FanoutSystemMessage(
            [attacker.ObjectId],
            CombatLines.ToNet(CombatLines.HitTarget(target.Name, damage)));
    }

    void FanoutAttack(ZoneEntity attacker, ZoneEntity target, AttackMsg attack, List<Pending> outbound)
    {
        byte[] attackBody = MessagePackSerializer.Serialize(attack);
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, attacker) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(attacker.ObjectId);
            if (!cares)
                continue;
            outbound.Add(new Pending(watcher.Session, MsgType.Attack, attackBody));
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutAttackState(netWatchers, new Fineage.Net.Contract.AttackStateMsg
            {
                AttackerValue = (ulong)(uint)attack.AttackerObjectId,
                AttackerGeneration = 1,
                TargetValue = (ulong)(uint)attack.TargetObjectId,
                TargetGeneration = 1,
                Damage = attack.Damage,
                Flags = attack.Flags,
                AttackerX = attack.AttackerX,
                AttackerY = attack.AttackerY,
                AttackerZ = attack.AttackerZ,
                TargetX = attack.TargetX,
                TargetY = attack.TargetY,
                TargetZ = attack.TargetZ
            });
        }
    }

    void TickMeleeHits(long now, List<Pending> outbound)
    {
        if (_meleeHits.Count == 0)
            return;
        var due = _meleeHits.Where(h => h.DueUnixMs <= now).ToList();
        if (due.Count == 0)
            return;
        _meleeHits.RemoveAll(h => h.DueUnixMs <= now);
        foreach (var hit in due)
            ResolveMeleeHit(hit, now, outbound);
    }

    void ResolveMeleeHit(PendingMeleeHit hit, long now, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(hit.AttackerId, out var attacker) || attacker.CurHp <= 0 || attacker.Dead)
            return;
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.CurHp <= 0 || target.Dead)
            return;
        // Mobius onHitTimer: target ran out of reach during the swing -> no damage.
        int reach = (attacker.Kind == ZoneEntityKind.Npc ? ZoneCombat.MobAttackReach(attacker.AttackRange) : PlayerReach(attacker)) + ZoneCombat.HitRangeTolerance;
        if (!ZoneCombat.Within(attacker.X, attacker.Y, target.X, target.Y, reach))
            return;

        if (hit.Damage <= 0)
        {
            // Miss (Attack carried HitFlagMiss): no HP change / StatusUpdate, but the fight and hate still start.
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.Kind == ZoneEntityKind.Npc)
                AddHate(target, attacker);
            return;
        }

        target.CurHp = Math.Max(0, target.CurHp - hit.Damage);

        if (target.Kind == ZoneEntityKind.Npc)
        {
            FanoutVitals(target, StatusFor(target), outbound);
            RecordDamage(target, attacker, hit.Damage);
            AddHate(target, attacker, DamageHate(hit.Damage, target.Level));
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.CurHp <= 0)
            {
                RemoveDeadNpc(target, outbound, killer: attacker);
                GrantKillReward(attacker, target, outbound);
            }
            return;
        }

        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target), outbound);
        if (target.CurHp > 0)
            MaybeBreakCast(target, hit.Damage, outbound);
        if (target.CurHp <= 0)
        {
            attacker.HateList.Remove(target.ObjectId);
            attacker.AggroTargetId = MostHated(attacker);
            if (target.Session != null)
                target.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, attacker.ObjectId, outbound);
        }
    }

    void FanoutCombat(ZoneEntity attacker, ZoneEntity target, AttackMsg attack, StatusUpdateMsg status, List<Pending> outbound)
    {
        byte[] attackBody = MessagePackSerializer.Serialize(attack);
        byte[] statusBody = MessagePackSerializer.Serialize(status);

        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, attacker) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(attacker.ObjectId);
            if (!cares)
                continue;

            outbound.Add(new Pending(watcher.Session, MsgType.Attack, attackBody));
            outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, statusBody));
        }
    }

    void RemoveDeadNpc(ZoneEntity npc, List<Pending> outbound, ZoneEntity? killer = null)
    {
        // Kill quest steps are not applied here. IQuestKillHook is the later combat entry.
        int objectId = npc.ObjectId;
        int npcId = npc.NpcId;
        int x = npc.X;
        int y = npc.Y;
        int z = npc.Z;

        var watchers = new List<ZoneEntity>();
        foreach (var player in Players())
        {
            if (player.Knownlist.Contains(objectId))
                watchers.Add(player);
        }

        // Mobius Creature.doDie -> broadcastPacket(new Die(this)): client plays the death animation.
        npc.AggroTargetId = 0;
        npc.Chasing = false;
        byte[] dieBody = MessagePackSerializer.Serialize(new DieMsg { ObjectId = objectId });
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            outbound.Add(new Pending(watcher.Session, MsgType.Die, dieBody));
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
            if (watcher.Session.SelectedTargetId == objectId)
                watcher.Session.AutoAttackTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDie(netWatchers, new Fineage.Net.Contract.EntityDieMsg
            {
                EntityValue = (ulong)(uint)objectId,
                EntityGeneration = 1,
                KillerObjectId = killer?.ObjectId ?? 0
            });
        }

        npc.SweepItems.Clear();
        if (npc.SpoilerObjectId != 0 && _dropCatalog.TryGet(npcId, out var definition))
        {
            foreach (var stack in DropRoller.RollCorpse(definition, _dropRandom, _dropRates))
                npc.SweepItems.Add(stack);
        }

        SpawnDeathDrops(npcId, x, y, z, killer, watchers, outbound);
        bool spoiled = npc.SpoilerObjectId != 0;
        npc.CorpseAwaitingSweep = npc.SweepItems.Count > 0;
        if (spoiled)
            ZoneLog.Info($"corpse object={objectId} spoiler={npc.SpoilerObjectId} sweep={npc.SweepItems.Count}");
        // Mobius DecayTaskManager: DefaultCorpseTime, + SpoiledCorpseExtendTime when spoiled; unswept spoil decays with the corpse.
        npc.DecayAtMs = _clock().ToUnixTimeMilliseconds() + _spoilRules.CorpseMs(spoiled);
    }

    void DespawnNpc(ZoneEntity npc, List<Pending> outbound)
    {
        int objectId = npc.ObjectId;

        _aoi.Leave(objectId);
        _byId.Remove(objectId);
        npc.SpoilerObjectId = 0;
        npc.SweepItems.Clear();
        npc.CorpseAwaitingSweep = false;
        npc.DecayAtMs = 0;
        npc.Chasing = false;
        npc.AggroTargetId = 0;
        npc.LastAiTickMs = 0;
        ScheduleRespawn(objectId);

        byte[] deleteBody = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = objectId });
        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                outbound.Add(new Pending(player.Session, MsgType.DeleteObject, deleteBody));
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
            if (player.Session.SelectedTargetId == objectId)
                player.Session.SelectedTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, new Fineage.Net.Contract.EntityDespawnMsg
            {
                EntityValue = (ulong)(uint)objectId,
                EntityGeneration = 1
            });
        }
    }

    bool VisibleTo(int observerId, int otherId)
    {
        if (!SameInstance(observerId, otherId))
            return false;
        foreach (int id in _aoi.GetVisible(observerId))
        {
            if (id == otherId)
                return true;
        }

        return false;
    }
}
