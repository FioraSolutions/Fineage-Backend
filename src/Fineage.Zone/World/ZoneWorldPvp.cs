using Fineage.Accounts;

namespace Fineage.Zone.World;

/// <summary>
/// Interlude PvP flag + karma/PK, matched against L2J Mobius CT_0 Interlude: <c>Player.updatePvPStatus</c>,
/// <c>Player.onKillUpdatePvPKarma</c>, <c>Player.increasePkKillsAndKarma</c>, <c>Playable.checkIfPvP</c>,
/// <c>Formulas.calculateKarmaGain</c> / <c>calculateKarmaLost</c>, <c>PlayerStat.addExp</c> and
/// <c>PvpFlagTaskManager</c> (purple, blinking for the last 20 s). Flag lasts <see cref="PvpNormalTimeMs"/> vs white / <see cref="PvpVsPvpTimeMs"/> vs already-flagged.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>pvp.properties <c>PvPVsNormalTime</c> default (ms).</summary>
    public const int PvpNormalTimeMs = 120_000;

    /// <summary>pvp.properties <c>PvPVsPvPTime</c> default (ms).</summary>
    public const int PvpVsPvpTimeMs = 60_000;

    /// <summary>Mobius PvPFlagTask: <c>updatePvPFlag(2)</c> once the flag has less than this left.</summary>
    public const int PvpBlinkMs = 20_000;

    /// <summary>
    /// Mobius <c>Formulas.calculateKarmaLost</c> divisor. <c>KarmaLossData</c> (karmaLoss.xml) defaults every level
    /// to 1.0 and the datapack file is not imported, so the per-level modifier is 1.
    /// </summary>
    public const int KarmaLostDivisor = 30;

    /// <summary>Wire value of the nameplate flag: 0 white, 1 purple, 2 purple blinking.</summary>
    internal static byte PvpFlagWire(ZoneEntity e) =>
        !e.PvpFlagged ? (byte)0 : e.PvpFlagBlink ? (byte)2 : (byte)1;

    /// <summary>Arena or a running siege (Mobius <c>ZoneId.PVP</c>): no flag, no karma, no PvP/PK counters.</summary>
    internal bool InPvpArea(ZoneEntity e) =>
        Pvp.Contains(e.X, e.Y, e.Z) || InActiveSiegePvp(e.X, e.Y, e.Z);

    /// <summary>Arena only (PVP zone that is not a siege): no exp loss and no item drop on a player kill.</summary>
    bool InArenaOnly(ZoneEntity e) =>
        Pvp.Contains(e.X, e.Y, e.Z) && !InActiveSiegePvp(e.X, e.Y, e.Z);

    /// <summary>
    /// L2J <c>Player.updatePvPStatus(Creature target)</c>: any aggressive act (swing, offensive cast — hit or miss)
    /// on a karma-0 player flags the attacker, unless both stand in a PVP zone. Hitting an already-flagged player
    /// flags you too (shorter <see cref="PvpVsPvpTimeMs"/>); hitting a karma player never flags.
    /// </summary>
    void UpdatePvPStatus(ZoneEntity attacker, ZoneEntity target, long now)
    {
        // Mobius updatePvPStatus: fighters of the same duel never flag each other.
        if (SameDuel(attacker, target))
            return;
        if (attacker.Kind != ZoneEntityKind.Player || target.Kind != ZoneEntityKind.Player)
            return;
        if (ReferenceEquals(attacker, target) || target.Karma > 0)
            return;
        if (InPvpArea(attacker) && InPvpArea(target))
            return;

        RaisePvpFlag(attacker, now, CheckIfPvP(attacker, target) ? PvpVsPvpTimeMs : PvpNormalTimeMs);
    }

    /// <summary>
    /// Mobius <c>setPvpFlagLasts(now + time)</c> + <c>startPvPFlag</c>: raise or refresh the purple name. The end is
    /// overwritten (not extended), and a refresh past the blink window turns blinking off again.
    /// </summary>
    void RaisePvpFlag(ZoneEntity attacker, long now, int durationMs)
    {
        if (attacker.Kind != ZoneEntityKind.Player)
            return;

        attacker.PvpFlagUntilMs = now + Math.Max(0, durationMs);
        bool changed = false;
        if (!attacker.PvpFlagged)
        {
            attacker.PvpFlagged = true;
            changed = true;
            ZoneLog.Info($"[pvp] flag on object={attacker.ObjectId} until={attacker.PvpFlagUntilMs}");
        }
        if (attacker.PvpFlagBlink && attacker.PvpFlagUntilMs - now > PvpBlinkMs)
        {
            attacker.PvpFlagBlink = false;
            changed = true;
        }
        if (changed)
            BroadcastPvpAppearance(attacker);
    }

    /// <summary>L2J <c>checkIfPvP</c>: both players karma-free and the target already flagged.</summary>
    static bool CheckIfPvP(ZoneEntity attacker, ZoneEntity target) =>
        attacker.Karma == 0 && target.Karma == 0 && target.PvpFlagged;

    /// <summary>
    /// L2J <c>onKillUpdatePvPKarma</c> (no duel / clan war): kills inside a PVP zone count nothing; a flagged victim
    /// is a PvP kill; a karma victim gives nothing (<c>AwardPKKillPVPPoint=false</c>); a white victim is a PK.
    /// <paramref name="victimWasFlagged"/> is the flag the victim carried when it died (death clears it).
    /// </summary>
    void OnKillUpdatePvPKarma(ZoneEntity killer, ZoneEntity victim, bool victimWasFlagged)
    {
        if (killer.Kind != ZoneEntityKind.Player || victim.Kind != ZoneEntityKind.Player)
            return;
        if (ReferenceEquals(killer, victim))
            return;
        if (InPvpArea(killer) || InPvpArea(victim))
            return;

        // Mobius: a kill between clans at mutual war (no academy members) is a PvP kill, never a PK.
        if (AtMutualWar(killer, victim))
        {
            killer.PvpKills++;
            PersistKarmaPk(killer);
            ApplyWarKillReputation(killer, victim);
            ZoneLog.Info($"[pvp] war-kill killer={killer.ObjectId} victim={victim.ObjectId} pvp={killer.PvpKills}");
            BroadcastPvpAppearance(killer);
            return;
        }

        if (victimWasFlagged && killer.Karma == 0 && victim.Karma == 0)
        {
            killer.PvpKills++;
            PersistKarmaPk(killer);
            ZoneLog.Info($"[pvp] pvp-kill killer={killer.ObjectId} victim={victim.ObjectId} pvp={killer.PvpKills}");
            BroadcastPvpAppearance(killer);
            return;
        }

        if (victim.Karma > 0 || victimWasFlagged)
            return;

        int gain = CalculateKarmaGain(killer.PkKills, isSummon: false);
        killer.Karma = killer.Karma > int.MaxValue - gain ? int.MaxValue : killer.Karma + gain;
        killer.PkKills++;
        PersistKarmaPk(killer);
        ZoneLog.Info(
            $"[pvp] pk killer={killer.ObjectId} victim={victim.ObjectId} karma={killer.Karma} pk={killer.PkKills}");
        BroadcastPvpAppearance(killer);
    }

    /// <summary>
    /// Mobius CT_0 <c>Formulas.calculateKarmaGain</c> (retail table; <paramref name="pkCount"/> is the count before
    /// this kill): 720 for the first PK, +360 per PK up to 99, slower to 179, then a flat 43200.
    /// </summary>
    internal static int CalculateKarmaGain(int pkCount, bool isSummon)
    {
        if (isSummon)
        {
            int summon = (int)((((pkCount * 0.375) + 1) * 60) * 4) - 150;
            return Math.Min(10800, summon);
        }
        if (pkCount < 99)
            return (int)((((pkCount * 0.5) + 1) * 60) * 12);
        if (pkCount < 180)
            return (int)((((pkCount * 0.125) + 37.75) * 60) * 12);
        return 43200;
    }

    /// <summary>
    /// Mobius CT_0 <c>Formulas.calculateKarmaLost</c> for gained exp: <c>exp / RateKarmaLost / levelMod / 30</c>
    /// (RateKarmaLost = -1 → RateXp), capped at the karma held.
    /// </summary>
    internal static int CalculateKarmaLost(int karma, long exp, double rateXp)
    {
        double rate = rateXp > 0 ? rateXp : 1d;
        double lost = Math.Abs(exp) / rate / KarmaLostDivisor;
        return (int)Math.Clamp(Math.Floor(lost), 0, Math.Max(0, karma));
    }

    /// <summary>
    /// Mobius <c>Player.doDie</c> karma tempfix: a karma player that dies keeps 3/4 of it (under 200 → 0).
    /// </summary>
    internal static int KarmaAfterDeath(int karma) => karma <= 0 ? 0 : karma < 200 ? 0 : karma - (karma / 4);

    /// <summary>
    /// Mobius <c>PlayerStat.addExp</c>: gained exp (mob kill, quest) burns karma outside a PVP zone. Hitting 0 turns
    /// the name white for every watcher.
    /// </summary>
    void BurnKarmaByExp(ZoneEntity player, long exp)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Karma <= 0 || exp <= 0 || InPvpArea(player))
            return;
        int lost = CalculateKarmaLost(player.Karma, exp, _rates.RateXp);
        if (lost <= 0)
            return;
        player.Karma -= lost;
        PersistKarmaPk(player);
        ZoneLog.Info($"[pvp] karma -{lost} object={player.ObjectId} karma={player.Karma}");
        if (player.Karma == 0)
            BroadcastPvpAppearance(player);
        else if (player.Session != null)
            SelfUserInfo(player);
    }

    /// <summary>Mobius PvPFlagTask: blink for the last 20 s, then drop the flag and repaint the nameplate.</summary>
    void TickPvpFlags(long now)
    {
        foreach (var player in Players())
        {
            if (!player.PvpFlagged || player.PvpFlagUntilMs <= 0)
                continue;
            if (player.PvpFlagUntilMs <= now)
            {
                player.PvpFlagged = false;
                player.PvpFlagBlink = false;
                player.PvpFlagUntilMs = 0;
                ZoneLog.Info($"[pvp] flag clear object={player.ObjectId}");
                BroadcastPvpAppearance(player);
                continue;
            }
            if (!player.PvpFlagBlink && player.PvpFlagUntilMs - now <= PvpBlinkMs)
            {
                player.PvpFlagBlink = true;
                BroadcastPvpAppearance(player);
            }
        }
    }

    /// <summary>Self UserInfo + knownlist CharInfo so purple/red nameplates update for peers.</summary>
    void BroadcastPvpAppearance(ZoneEntity player)
    {
        if (player.Session != null)
            SelfUserInfo(player);
        if (NetBridge == null)
            return;
        var watchers = NetWatchersOfCharInfo(player);
        if (watchers is { Count: > 0 })
            NetBridge.FanoutCharInfo(watchers, player.ObjectId);
    }

    void PersistKarmaPk(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        _persist.SaveKarmaPk(
            player.Session.Account,
            player.ObjectId,
            player.Karma,
            player.PkKills,
            player.PvpKills);
    }

    /// <summary>
    /// L2J <c>isAutoAttackable</c> between two players: hostile without Ctrl when the target is flagged or has karma,
    /// or when both stand in a PVP zone — never a party mate.
    /// </summary>
    bool PlayerAutoAttackable(ZoneEntity attacker, ZoneEntity target)
    {
        if (ReferenceEquals(attacker, target))
            return false;
        // Mobius Player.isAutoAttackable: the other side of a running duel is hostile without Ctrl.
        if (DuelFoes(attacker, target))
            return true;
        if (SameParty(attacker.ObjectId, target.ObjectId))
            return false;
        // Mobius Player.isAutoAttackable: members of clans at mutual war are hostile without Ctrl.
        if (AtMutualWar(attacker, target))
            return true;
        if (target.PvpFlagged || target.Karma > 0)
            return true;
        return target.InstanceId != 0 || (InPvpArea(attacker) && InPvpArea(target));
    }

    /// <summary>
    /// L2J <c>useMagic</c> / <c>onForcedAttack</c>: a hostile act on a player that is not auto-attackable (white,
    /// or a party mate) needs Ctrl. Without it the request is answered with ActionFailed only.
    /// </summary>
    bool NeedsForce(ZoneEntity attacker, ZoneEntity target)
    {
        // Another player's summon is judged as its owner (Mobius Summon.isAutoAttackable → owner).
        if (target.Summon != null && OwnerOf(target) is { } owner && !ReferenceEquals(owner, attacker))
            target = owner;
        return attacker.Kind == ZoneEntityKind.Player && target.Kind == ZoneEntityKind.Player &&
            !PlayerAutoAttackable(attacker, target);
    }

    /// <summary>Test hook: apply PvP status as if <paramref name="attacker"/> damaged <paramref name="target"/>.</summary>
    internal void UpdatePvPStatusForTest(ZoneEntity attacker, ZoneEntity target)
    {
        lock (_gate)
            UpdatePvPStatus(attacker, target, _clock().ToUnixTimeMilliseconds());
    }

    /// <summary>Test hook: run kill karma/PK rules.</summary>
    internal void OnKillUpdatePvPKarmaForTest(ZoneEntity killer, ZoneEntity victim)
    {
        lock (_gate)
            OnKillUpdatePvPKarma(killer, victim, victim.PvpFlagged);
    }
}
