using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TrySelectTargetAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TrySelectTarget(session, targetObjectId);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryCancelTargetAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out _))
                return Task.FromResult<string?>("world");

            int previous = session.SelectedTargetId;
            session.SelectedTargetId = 0;
            session.AutoAttackTargetId = 0;
            session.ClearApproach();
            NetBridge?.FanoutTargetUnselected([session.ObjectId], previous);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAttackAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        long approachMs = 0;
        lock (_gate)
        {
            int previous = session.AutoAttackTargetId;
            long now0 = _clock().ToUnixTimeMilliseconds();
            int requested = targetObjectId > 0 ? targetObjectId : session.SelectedTargetId;
            bool alreadySwinging = previous > 0 && previous == requested && session.ApproachTargetId == 0 &&
                session.NextAutoAttackUnixMs > now0;
            // Re-click on the target already being hit: Mobius keeps the running attack, no extra swing.
            error = alreadySwinging ? null : TryAttack(session, targetObjectId, out approachMs);
            if (error == null && !alreadySwinging && approachMs >= 0)
            {
                session.AutoAttackTargetId = session.SelectedTargetId;
                // Approaching: first swing on arrival (Mobius onEvtArrived -> doAttack); else next swing after Pspd.
                session.NextAutoAttackUnixMs = _clock().ToUnixTimeMilliseconds() +
                    (approachMs > 0 ? approachMs : PlayerAttackTimeMs(session.ObjectId));
            }
        }
        return Task.FromResult<string?>(error);
    }

    string? TrySelectTarget(ZoneSession session, int targetObjectId)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";

        // Mobius Action: missing / not visible object → ActionFailed, not a protocol Reject.
        // Returning null here keeps the previous selection and avoids client "[Net] zone reject target".
        if (!_byId.TryGetValue(targetObjectId, out var target))
            return null;

        // Interlude: click own pawn → target self (AOI never lists the observer).
        if (targetObjectId == session.ObjectId)
        {
            if (player.Dead || player.CurHp <= 0)
                return null;

            // Own pawn: the client already draws itself; Mobius sends colour 0 with no HP block and Net has no
            // self-target frame, so nothing is fanned.
            session.SelectedTargetId = targetObjectId;
            return null;
        }

        // Mobius NpcAction.setTarget accepts dead creatures (attack is gated separately).
        // Mobius ItemAction also targets ground drops before pickup — rejecting them with "target"
        // was the reject spam after inventory drops.
        if (target.Kind is not (ZoneEntityKind.Npc or ZoneEntityKind.Player or ZoneEntityKind.GroundItem))
            return null;
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return null;

        session.SelectedTargetId = targetObjectId;
        PublishTargetSelection(session, player, target);
        return null;
    }

    /// <summary>
    /// Mobius <c>Player.setTarget</c>: a click on a creature is answered with the nameplate colour
    /// (<c>MyTargetSelected</c>, colour 0 for anything the player cannot auto-attack) and the target's HP for the
    /// target window (<c>StatusUpdate</c> MAX_HP + CUR_HP). Peers also learn what this player is aiming at
    /// (<c>TargetSelected</c> broadcast), so a nearby client can show the aim marker.
    /// </summary>
    void PublishTargetSelection(ZoneSession session, ZoneEntity player, ZoneEntity target)
    {
        bool creature = target.Kind is ZoneEntityKind.Npc or ZoneEntityKind.Player;
        int colour = creature
            ? InterludeStatus.TargetColor(player.Level, target.Level, IsAutoAttackable(target))
            : 0;

        if (NetBridge == null)
            return;

        int[] self = [player.ObjectId];
        if (creature)
        {
            NetBridge.FanoutStatusUpdate(self, new StatusUpdateMsg
            {
                ObjectId = target.ObjectId,
                AttributeIds = [InterludeStatus.MaxHp, InterludeStatus.CurHp],
                Values = [target.MaxHp, target.CurHp]
            });
            // Mobius PartySpelled on setTarget — TargetStatus icons (debuffs + spoil mark).
            if (target.Abnormals.Count > 0 || target.SpoilerObjectId != 0)
                NetBridge.FanoutAbnormalStatusUpdate(self, AbnormalFor(target));
        }

        // Net has no MyTargetSelected opcode: TargetSelected (0x030F) is its self-side mirror, so it carries the
        // same id + colour. Mobius also broadcasts TargetSelected to peers; that fanout stays out of scope here.
        NetBridge.FanoutTargetSelected(self, target.ObjectId, colour);
    }

    /// <summary>
    /// Mobius <c>Creature.isAutoAttackable(Player)</c> for the entities this zone carries: NPCs follow their
    /// template flag; a player is hostile (coloured nameplate) when PvP-flagged or carrying karma. Everything
    /// else is white (colour 0). Ctrl-attack of a white player is still allowed via <see cref="IsAttackable"/>.
    /// </summary>
    static bool IsAutoAttackable(ZoneEntity target) =>
        target.Kind switch
        {
            ZoneEntityKind.Npc => target.Attackable,
            ZoneEntityKind.Player => target.PvpFlagged || target.Karma > 0,
            _ => false
        };

    /// <summary>
    /// Mobius attack gate for an explicit AttackRequest: another living player is always swingable
    /// (peace zones not imported); any living NPC is swingable because AttackRequest is only sent when
    /// the client intends to force-attack (ctrl / attack hotkey). Accidental merchant clicks stay on
    /// the talk path and never reach here. Attacking a white player raises the attacker's PvP flag.
    /// </summary>
    static bool IsAttackable(ZoneEntity attacker, ZoneEntity target)
    {
        if (target.Dead || target.CurHp <= 0)
            return false;
        return target.Kind switch
        {
            ZoneEntityKind.Npc => true,
            ZoneEntityKind.Player => !ReferenceEquals(attacker, target),
            _ => false
        };
    }

    string? TryAttack(ZoneSession session, int requestedTargetId) =>
        TryAttack(session, requestedTargetId, out _);

    /// <summary>
    /// Interlude RequestAttack: in range -> swing; out of range -> server moveToPawn (run to the target,
    /// MoveToAck to self + watchers) and the swing fires from <see cref="TickAutoAttacks"/> on arrival.
    /// <paramref name="approachMs"/> is the travel time when an approach was started, else 0.
    /// </summary>
    string? TryAttack(ZoneSession session, int requestedTargetId, out long approachMs)
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
            approachMs = ApproachTarget(player, target, PlayerReach(player));
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
            CannotSeeTarget(session);
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
            if (bow != null && EnsureBowArrow(player, bow) == null)
            {
                OutOfArrows(session);
                approachMs = -1;
                return null;
            }
        }
        // Mobius doAttack: a charged weapon fires its soulshot on this swing (hit or miss) and is uncharged.
        bool soulshot = player.SoulshotCharged;
        player.SoulshotCharged = false;
        if (soulshot)
            RechargeAutoShots(player);
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
        FanoutAttack(player, target, attack);
        QueueAttackerDamageLine(player, target, damage);
        if (ps.Weapon == Fineage.Game.WeaponType.Bow)
            ConsumeBowArrow(player);
        long now = _clock().ToUnixTimeMilliseconds();
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        _meleeHits.Add(new PendingMeleeHit(player.ObjectId, targetId, damage, now + PlayerAttackTimeMs(player.ObjectId) / 2));

        return null;
    }

}
