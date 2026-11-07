using Fineage.Accounts;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius PvP flag + karma/PK: <c>updatePvPStatus</c>, <c>onKillUpdatePvPKarma</c>,
/// <c>Formulas.calculateKarmaGain</c>. Flag lasts <see cref="PvpNormalTimeMs"/> vs white /
/// <see cref="PvpVsPvpTimeMs"/> vs already-flagged (Config PvPVsNormalTime / PvPVsPvPTime).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>Config.PVP_NORMAL_TIME</c> default (ms).</summary>
    public const int PvpNormalTimeMs = 120_000;

    /// <summary>Mobius <c>Config.PVP_PVP_TIME</c> default (ms).</summary>
    public const int PvpVsPvpTimeMs = 60_000;

    /// <summary>
    /// Mobius <c>Player.updatePvPStatus(Creature)</c>: flag the attacker when damaging a karma-0 player
    /// outside a mutual PvP zone (zones not imported — always apply).
    /// </summary>
    void UpdatePvPStatus(ZoneEntity attacker, ZoneEntity target, long now)
    {
        if (attacker.Kind != ZoneEntityKind.Player || target.Kind != ZoneEntityKind.Player)
            return;
        if (ReferenceEquals(attacker, target) || target.Karma > 0)
            return;

        long lasts = now + (CheckIfPvP(attacker, target) ? PvpVsPvpTimeMs : PvpNormalTimeMs);
        if (lasts > attacker.PvpFlagUntilMs)
            attacker.PvpFlagUntilMs = lasts;
        if (!attacker.PvpFlagged)
        {
            attacker.PvpFlagged = true;
            ZoneLog.Info($"[pvp] flag on object={attacker.ObjectId} until={attacker.PvpFlagUntilMs}");
            if (attacker.Session != null)
                SelfUserInfo(attacker);
        }
    }

    /// <summary>
    /// Mobius <c>Playable.checkIfPvP</c>: both players karma-free and target already flagged.
    /// </summary>
    static bool CheckIfPvP(ZoneEntity attacker, ZoneEntity target) =>
        attacker.Karma == 0 && target.Karma == 0 && target.PvpFlagged;

    /// <summary>
    /// Mobius <c>Player.onKillUpdatePvPKarma</c> (no clan-war / faction): flagged or karma victim → PvP kill;
    /// white victim → PK + karma gain.
    /// </summary>
    void OnKillUpdatePvPKarma(ZoneEntity killer, ZoneEntity victim)
    {
        if (killer.Kind != ZoneEntityKind.Player || victim.Kind != ZoneEntityKind.Player)
            return;
        if (ReferenceEquals(killer, victim))
            return;

        if (CheckIfPvP(killer, victim) && victim.PvpFlagged)
        {
            killer.PvpKills++;
            PersistKarmaPk(killer);
            ZoneLog.Info($"[pvp] pvp-kill killer={killer.ObjectId} victim={victim.ObjectId} pvp={killer.PvpKills}");
            if (killer.Session != null)
                SelfUserInfo(killer);
            return;
        }

        if (victim.Karma > 0)
            return; // KARMA_AWARD_PK_KILL off — no PvP credit for karma kills

        if (victim.PvpFlagged)
            return;

        killer.Karma += CalculateKarmaGain(killer.PkKills, isSummon: false);
        killer.PkKills++;
        PersistKarmaPk(killer);
        ZoneLog.Info(
            $"[pvp] pk killer={killer.ObjectId} victim={victim.ObjectId} karma={killer.Karma} pk={killer.PkKills}");
        if (killer.Session != null)
            SelfUserInfo(killer);
    }

    /// <summary>Mobius <c>Formulas.calculateKarmaGain</c> (High Five retail table).</summary>
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

    void TickPvpFlags(long now)
    {
        foreach (var player in Players())
        {
            if (!player.PvpFlagged || player.PvpFlagUntilMs <= 0 || player.PvpFlagUntilMs > now)
                continue;
            player.PvpFlagged = false;
            player.PvpFlagUntilMs = 0;
            ZoneLog.Info($"[pvp] flag clear object={player.ObjectId}");
            if (player.Session != null)
                SelfUserInfo(player);
        }
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
            OnKillUpdatePvPKarma(killer, victim);
    }
}
