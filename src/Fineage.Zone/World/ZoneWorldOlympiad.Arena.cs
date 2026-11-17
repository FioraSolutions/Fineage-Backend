using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;

namespace Fineage.Zone.World;

/// <summary>
/// Block G2 arena rules after Mobius <c>OlympiadGame.removals</c> / <c>PlayersStatusBack</c> /
/// <c>PlayerStatus.reduceHp</c> in Olympiad mode: buffs and party drop and the bars fill on the way in; a knockout
/// is not a death (no exp loss, no drop) and decides the match; only the opponent can be hit, and only once the
/// fight began; hero skills rest while inside; the bars fill again on the way home.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly HashSet<int> _olympiadArena = [];
    readonly Dictionary<int, (int X, int Y, int Z, int InstanceId)> _olympiadObservers = [];

    internal bool InOlympiadArena(int objectId) => _olympiadArena.Contains(objectId);

    internal bool OlympiadTargetRefusedForTest(ZoneEntity attacker, ZoneEntity target)
    {
        lock (_gate)
            return OlympiadTargetRefused(attacker, target);
    }

    internal bool OlympiadObserving(int objectId) => _olympiadObservers.ContainsKey(objectId);

    void EnterOlympiadArena(ZoneEntity player)
    {
        if (!_olympiadArena.Add(player.ObjectId))
            return;
        EndOlympiadObserve(player);
        if (player.Session != null && _partyByPlayer.ContainsKey(player.ObjectId))
            TryPartyLeave(player.ObjectId);
        player.Abnormals.Clear();
        player.ActiveEnchantObjectId = 0;
        RefreshKnownSkills(player); // hero skills rest inside (AddStatusSkills)
        RefreshPlayerMaxima(player, fanout: false);
        FillBars(player);
        RefreshRunSpeed(player);
        if (player.Session != null)
        {
            NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
            NetBridge?.FanoutAbnormalStatusUpdate([player.ObjectId], AbnormalFor(player));
        }
    }

    void LeaveOlympiadArena(ZoneEntity player)
    {
        if (!_olympiadArena.Remove(player.ObjectId))
            return;
        bool wasDown = player.Dead;
        player.Dead = false;
        RefreshKnownSkills(player);
        RefreshPlayerMaxima(player, fanout: false);
        FillBars(player);
        if (wasDown)
            FanoutRevive(player);
        if (player.Session != null)
            NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
    }

    void FillBars(ZoneEntity player)
    {
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        if (player.Session == null)
            return;
        FanoutVitals(player, PlayerVitalsStatus(player));
        SelfUserInfo(player);
        SaveVitals(player.Session, player);
    }

    /// <summary>
    /// Mobius Olympiad <c>reduceHp</c>: at 0 HP the fighter is down (dead flag, no <c>doDie</c>), and the other side
    /// wins at once. Returns false when the player is not fighting in an arena.
    /// </summary>
    bool OlympiadKnockout(ZoneEntity player, int killerObjectId)
    {
        if (!_olympiadArena.Contains(player.ObjectId))
            return false;
        var state = _olympiad.MatchState(player.ObjectId);
        player.Dead = true;
        player.CurHp = 0;
        player.Running = false;
        player.Abnormals.Clear();
        if (player.Session != null)
            player.Session.SelectedTargetId = 0;
        FanoutPlayerDeath(player, killerObjectId);
        if (state is { Phase: OlympiadMatchPhase.Fighting } s)
        {
            var step = _olympiad.Report(s.MatchId, s.OpponentId, _clock().ToUnixTimeMilliseconds());
            if (step.Error == null)
                ApplyOlympiad(step);
        }
        ZoneLog.Info($"olympiad knockout object={player.ObjectId} killer={killerObjectId}");
        return true;
    }

    /// <summary>
    /// Mobius <c>isAutoAttackable</c> / <c>checkPvpSkill</c> in Olympiad mode: inside an arena only the opponent of a
    /// running fight; observers neither hit nor get hit; nobody outside reaches in.
    /// </summary>
    bool OlympiadTargetRefused(ZoneEntity attacker, ZoneEntity target)
    {
        if (_olympiadObservers.ContainsKey(attacker.ObjectId) || _olympiadObservers.ContainsKey(target.ObjectId))
            return true;
        bool attackerIn = _olympiadArena.Contains(attacker.ObjectId);
        bool targetIn = target.Kind == ZoneEntityKind.Player && _olympiadArena.Contains(target.ObjectId);
        if (!attackerIn && !targetIn)
            return false;
        if (!attackerIn || !targetIn)
            return true;
        var state = _olympiad.MatchState(attacker.ObjectId);
        return state is not { Phase: OlympiadMatchPhase.Fighting } s || s.OpponentId != target.ObjectId;
    }

    /// <summary>Mobius <c>Olympiad 3_N</c> / <c>addSpectator</c>: watch a stadium from inside its instance.</summary>
    string? BeginOlympiadObserve(ZoneSession session, ZoneEntity player, int stadiumId)
    {
        if (stadiumId < 0 || stadiumId >= _olympiad.Stadiums.Count)
            return "bad_request";
        if (_olympiadArena.Contains(player.ObjectId) || _olympiad.MatchState(player.ObjectId) != null)
            return OlympiadProtocol.Selected;
        var site = _olympiad.Stadiums[stadiumId];
        if (!_olympiadObservers.ContainsKey(player.ObjectId))
            _olympiadObservers[player.ObjectId] = (player.X, player.Y, player.Z, player.InstanceId);
        Relocate(player, site.X, site.Y, site.Z, site.ZoneId, persist: false);
        ZoneLog.Info($"olympiad observe object={player.ObjectId} stadium={stadiumId}");
        return null;
    }

    bool EndOlympiadObserve(ZoneEntity player)
    {
        if (!_olympiadObservers.Remove(player.ObjectId, out var back))
            return false;
        Relocate(player, back.X, back.Y, back.Z, back.InstanceId, persist: true);
        return true;
    }
}
