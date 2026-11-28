namespace Fineage.Zone.Combat;

/// <summary>Mobius <c>Creature.onHitTimer</c>: melee damage lands at hitTime (~half the swing), not on swing start.</summary>
/// <remarks><see cref="Flags"/>: the swing's hit flags (miss / crit / shield) for the victim's system messages.</remarks>
/// <remarks><see cref="RechargeShots"/>: Mobius onHitTimer(rechargeShots) — the swing fired a soulshot, so auto-shot
/// recharges (and broadcasts the weapon glow) at hit time, not at swing start.</remarks>
/// <remarks><see cref="AttackerLine"/>: Mobius onHitTimer -> sendDamageMessage — the attacker's own
/// "You hit / missed / critical" lines go out at hit time with the HP change, not when the swing starts.</remarks>
/// <remarks><see cref="SwingUnixMs"/>: when the swing (Attack packet) went out, for the combat trace.</remarks>
public sealed record PendingMeleeHit(int AttackerId, int TargetId, int Damage, long DueUnixMs, byte Flags = 0, bool RechargeShots = false,
    bool AttackerLine = false, long SwingUnixMs = 0);
