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
    const int OlympiadEquipItemMessage = 1507, OlympiadUseItemMessage = 1508;
    /// <summary>Mobius removals: every skill whose reuse delay is at most 15 minutes is ready again on entry.</summary>
    const long OlympiadReuseResetMaxMs = 900_000;

    /// <summary>
    /// Mobius <c>ItemTemplate.checkCondition</c> in Olympiad mode: hero items and the configured restricted list cannot
    /// be equipped or used inside an arena. Unequipping is always allowed.
    /// </summary>
    bool OlympiadRestrictedItem(int itemId) =>
        IsHeroItem(itemId) || _olympiad.Board.Settings.RestrictedItems.Contains(itemId);

    string? OlympiadItemRefused(ZoneEntity player, ItemInstance item)
    {
        if (!_olympiadArena.Contains(player.ObjectId) || item.IsEquipped || !OlympiadRestrictedItem(item.ItemId))
            return null;
        if (player.Session is { } session)
        {
            bool equip = item.Template.Equippable;
            ShotNotice(session, equip ? OlympiadEquipItemMessage : OlympiadUseItemMessage,
                equip ? "You cannot equip that item in a Grand Olympiad Games match." : "You cannot use that item in a Grand Olympiad Games match.");
        }
        return InventoryProtocol.OlympiadRestricted;
    }

    /// <summary>Mobius <c>Player.checkItemRestriction</c> on arena entry: take off whatever the arena refuses.</summary>
    void UnequipOlympiadRestricted(ZoneEntity player)
    {
        if (player.Inventory is not { } inventory || player.Session is not { } session)
            return;
        var doomed = inventory.Items.Where(i => i.IsEquipped && OlympiadRestrictedItem(i.ItemId)).ToList();
        if (doomed.Count == 0)
            return;
        var before = PaperdollSnapshot(inventory);
        foreach (var item in doomed)
            inventory.Unequip(item);
        player.SoulshotCharged = false;
        player.SpiritshotCharge = 0;
        PublishGear(session, player, before);
        SaveInventory(session, inventory);
    }

    /// <summary>Mobius removals: skills with reuse up to 15 minutes come back; longer ones keep their timer.</summary>
    void ResetOlympiadReuse(ZoneEntity player)
    {
        foreach (int skillId in player.SkillReuseUntilMs.Keys.ToList())
        {
            if (_game.Skills.Find(skillId) is { } skill && skill.ReuseDelayMs <= OlympiadReuseResetMaxMs)
                player.SkillReuseUntilMs.Remove(skillId);
        }
    }
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
        AbortCast(player, "olympiad", notify: false);
        if (player.Session != null && _partyByPlayer.ContainsKey(player.ObjectId))
            TryPartyLeave(player.ObjectId);
        player.Abnormals.Clear();
        player.ActiveEnchantObjectId = 0;
        // Mobius: shots discharged, auto shots off, restricted gear off, short reuses ready.
        player.SoulshotCharged = false;
        player.SpiritshotCharge = 0;
        foreach (int shotId in player.AutoShots.ToList())
        {
            player.AutoShots.Remove(shotId);
            if (player.Session is { } shotSession)
                SendAutoShotState(shotSession, player, shotId, _game.Items.TryGet(shotId, out var shot) ? shot!.Name : "", false);
        }
        UnequipOlympiadRestricted(player);
        ResetOlympiadReuse(player);
        RefreshClanSkills(player, ClanOf(player.ObjectId)); // clan skills rest inside
        RefreshKnownSkills(player); // hero skills rest inside (AddStatusSkills)
        RefreshPlayerMaxima(player, fanout: false);
        FillBars(player);
        RefreshRunSpeed(player);
        if (player.Session != null)
        {
            NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
            NetBridge?.FanoutAbnormalStatusUpdate([player.ObjectId], AbnormalFor(player));
        }
    }

    void LeaveOlympiadArena(ZoneEntity player)
    {
        if (!_olympiadArena.Remove(player.ObjectId))
            return;
        bool wasDown = player.Dead;
        player.Dead = false;
        RefreshClanSkills(player, ClanOf(player.ObjectId));
        RefreshKnownSkills(player);
        RefreshPlayerMaxima(player, fanout: false);
        FillBars(player);
        if (wasDown)
            FanoutRevive(player);
        if (player.Session != null)
            NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
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
        var state = _olympiad.Board.MatchState(player.ObjectId);
        player.Dead = true;
        player.CurHp = 0;
        player.Running = false;
        player.Abnormals.Clear();
        if (player.Session != null)
            player.Session.SelectedTargetId = 0;
        FanoutPlayerDeath(player, killerObjectId);
        if (state is { Phase: OlympiadMatchPhase.Fighting } s)
        {
            var step = _olympiad.Board.Report(s.MatchId, s.OpponentId, NowUnixMs());
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
        var state = _olympiad.Board.MatchState(attacker.ObjectId);
        return state is not { Phase: OlympiadMatchPhase.Fighting } s || s.OpponentId != target.ObjectId;
    }

    /// <summary>Mobius <c>Olympiad 3_N</c> / <c>addSpectator</c>: watch a stadium from inside its instance.</summary>
    string? BeginOlympiadObserve(ZoneSession session, ZoneEntity player, int stadiumId)
    {
        if (stadiumId < 0 || stadiumId >= _olympiad.Board.Stadiums.Count)
            return "bad_request";
        if (_olympiadArena.Contains(player.ObjectId) || _olympiad.Board.MatchState(player.ObjectId) != null)
            return OlympiadProtocol.Selected;
        var site = _olympiad.Board.Stadiums[stadiumId];
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
