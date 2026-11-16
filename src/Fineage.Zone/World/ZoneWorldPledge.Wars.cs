using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Clan wars after Mobius <c>RequestStartPledgeWar</c>, <c>RequestStopPledgeWar</c>, <c>RequestSurrenderPledgeWar</c>,
/// <c>ClanTable.storeClanWars / deleteClanWars</c>, <c>Player.onKillUpdatePvPKarma</c> (war kills) and
/// <c>Player.getRelation</c> / <c>RelationChanged</c>. One side declaring is a one-sided war; both sides is mutual.
/// </summary>
public sealed partial class ZoneWorld
{
    bool IsAtWar(ZoneClan clan) => clan.D.WarTargets.Count > 0 || _clans.Values.Any(c => c.D.WarTargets.Contains(clan.Id));

    bool Declared(int fromClanId, int toClanId) =>
        _clans.TryGetValue(fromClanId, out var from) && from.D.WarTargets.Contains(toClanId);

    bool MutualWar(int a, int b) => a != 0 && b != 0 && a != b && Declared(a, b) && Declared(b, a);

    /// <summary>Mobius war PvP: both in clans at mutual war and neither is an academy member.</summary>
    bool AtMutualWar(ZoneEntity a, ZoneEntity b) =>
        a.Kind == ZoneEntityKind.Player && b.Kind == ZoneEntityKind.Player &&
        a.PledgeType != PledgeProtocol.Academy && b.PledgeType != PledgeProtocol.Academy &&
        MutualWar(ClanIdFor(a.ObjectId), ClanIdFor(b.ObjectId));

    /// <summary>Mobius <c>Player.getRelation(target)</c>: bits <paramref name="subject"/> shows to <paramref name="viewer"/>.</summary>
    int RelationOf(ZoneEntity subject, ZoneEntity viewer)
    {
        int relation = 0;
        if (subject.PvpFlagged)
            relation |= PledgeProtocol.RelationPvpFlag;
        if (subject.Karma > 0)
            relation |= PledgeProtocol.RelationHasKarma;
        if (subject.ClanLeader)
            relation |= PledgeProtocol.RelationLeader;
        int sc = ClanIdFor(subject.ObjectId);
        int vc = ClanIdFor(viewer.ObjectId);
        if (sc != 0 && vc != 0 && sc != vc && subject.PledgeType != PledgeProtocol.Academy && viewer.PledgeType != PledgeProtocol.Academy)
        {
            if (Declared(vc, sc))
            {
                relation |= PledgeProtocol.RelationOneSidedWar;
                if (Declared(sc, vc))
                    relation |= PledgeProtocol.RelationMutualWar;
            }
        }
        int sa = subject.AllyId, va = viewer.AllyId;
        if (sa != 0 && sa == va && sc != vc)
            relation |= PledgeProtocol.RelationAlly;
        return relation;
    }

    /// <summary>Mobius <c>broadcastRelationChanged</c>: every Net watcher of <paramref name="player"/> gets its own bits.</summary>
    void BroadcastRelation(ZoneEntity player)
    {
        if (NetBridge == null)
            return;
        foreach (int id in player.Knownlist)
        {
            if (!_byId.TryGetValue(id, out var viewer) || viewer.Session == null || viewer.Kind != ZoneEntityKind.Player)
                continue;
            SendRelation(player, viewer);
            SendRelation(viewer, player);
        }
    }

    void SendRelation(ZoneEntity subject, ZoneEntity viewer)
    {
        if (viewer.Session == null || NetBridge == null)
            return;
        NetBridge.FanoutRelationChanged([viewer.ObjectId], new RelationChangedNetMsg
        {
            ObjectId = subject.ObjectId,
            Relation = RelationOf(subject, viewer),
            AutoAttackable = PlayerAutoAttackable(viewer, subject),
            Karma = subject.Karma,
            PvpFlag = PvpFlagWire(subject)
        });
    }

    const int SpawnRelationBits = PledgeProtocol.RelationMutualWar | PledgeProtocol.RelationOneSidedWar | PledgeProtocol.RelationAlly;

    /// <summary>
    /// Mobius sends RelationChanged with every CharInfo. A fresh pawn starts at relation 0, so only war / ally bits
    /// need a follow-up frame: null when there is nothing to say.
    /// </summary>
    internal byte[]? SpawnRelationFrame(ZoneEntity subject, ZoneEntity viewer)
    {
        lock (_gate)
        {
            if (subject.Kind != ZoneEntityKind.Player || viewer.Kind != ZoneEntityKind.Player || subject.ObjectId == viewer.ObjectId)
                return null;
            int relation = RelationOf(subject, viewer);
            if ((relation & SpawnRelationBits) == 0)
                return null;
            return NetCodec.EncodeRelationChanged(new RelationChangedNetMsg
            {
                ObjectId = subject.ObjectId,
                Relation = relation,
                AutoAttackable = PlayerAutoAttackable(viewer, subject),
                Karma = subject.Karma,
                PvpFlag = PvpFlagWire(subject)
            });
        }
    }

    void BroadcastClanRelations(ZoneClan clan)
    {
        foreach (var session in OnlineMembers(clan))
        {
            if (_byId.TryGetValue(session.ObjectId, out var entity))
                BroadcastRelation(entity);
        }
    }

    string? DeclareWar(ZoneSession session, ZoneClan clan, string? targetName)
    {
        const string sizeText = "A clan war can only be declared if the clan is level 3 or above, and the number of clan members is fifteen or greater.";
        if (clan.Level < PledgeProtocol.WarMinClanLevel || clan.Members.Count < PledgeProtocol.WarMinMembers)
        {
            SelfLine(session, 1564, sizeText);
            return PledgeProtocol.Conditions;
        }
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivWarDeclaration))
        {
            SelfLine(session, 794, "You are not authorized to do that.");
            return PledgeProtocol.Denied;
        }
        var target = string.IsNullOrWhiteSpace(targetName)
            ? null
            : _clans.Values.FirstOrDefault(c => string.Equals(c.Name, targetName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target == null || target.Id == clan.Id)
        {
            SelfLine(session, 1565, "A clan war cannot be declared against a clan that does not exist!");
            return PledgeProtocol.NoSuchClan;
        }
        if (clan.D.AllyId != 0 && clan.D.AllyId == target.D.AllyId)
        {
            SelfLine(session, 1569, "A declaration of Clan War against an allied clan can't be made.");
            return PledgeProtocol.WarAlly;
        }
        if (target.Level < PledgeProtocol.WarMinClanLevel || target.Members.Count < PledgeProtocol.WarMinMembers)
        {
            SelfLine(session, 1564, sizeText);
            return PledgeProtocol.Conditions;
        }
        if (clan.D.WarTargets.Contains(target.Id))
        {
            SelfLine(session, 628, $"You have already been at war with the {target.Name} clan: 5 days must pass before you can declare war again.", target.Name);
            return PledgeProtocol.AlreadyAtWar;
        }

        clan.D.WarTargets.Add(target.Id);
        if (!SaveClan(clan))
        {
            clan.D.WarTargets.Remove(target.Id);
            return ClanProtocol.Store;
        }

        ClanLine(OnlineIds(clan), 1562, $"A clan war has been declared against the clan, {target.Name}. If you are killed during the Clan War by members of the opposing clan, you will only lose a quarter of the normal experience from death.", target.Name);
        ClanLine(OnlineIds(target), 1561, $"Clan {clan.Name} has declared clan war.", clan.Name);
        BroadcastClan(clan);
        BroadcastClan(target);
        BroadcastClanRelations(clan);
        BroadcastClanRelations(target);
        return null;
    }

    string? StopWar(ZoneSession session, ZoneClan clan, string? targetName, bool surrender)
    {
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivWarDeclaration))
        {
            SelfLine(session, 794, "You are not authorized to do that.");
            return PledgeProtocol.Denied;
        }
        var target = string.IsNullOrWhiteSpace(targetName)
            ? null
            : _clans.Values.FirstOrDefault(c => string.Equals(c.Name, targetName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            SelfLine(session, 1565, "The clan does not exist.");
            return PledgeProtocol.NoSuchClan;
        }
        if (!clan.D.WarTargets.Contains(target.Id))
        {
            return PledgeProtocol.NotAtWar;
        }
        // Mobius RequestStopPledgeWar: refused while any member is in combat (1677).
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var member in OnlineMembers(clan))
        {
            if (_byId.TryGetValue(member.ObjectId, out var e) && Clan.ClanRules.IsInCombat(e.CombatUntilUnixMs, now))
            {
                SelfLine(session, 1677, "A ceasefire during a Clan War can not be called while members of your clan are engaged in battle.");
                return PledgeProtocol.WarCombat;
            }
        }

        clan.D.WarTargets.Remove(target.Id);
        if (surrender)
            target.D.WarTargets.Remove(clan.Id);
        if (!SaveClan(clan) || (surrender && !SaveClan(target)))
            return ClanProtocol.Store;

        if (surrender)
        {
            ClanLine(OnlineIds(clan), 218, $"You have surrendered to the {target.Name} clan.", target.Name);
            ClanLine(OnlineIds(target), 1567, $"The clan war with {clan.Name} has ended.", clan.Name);
        }
        else
        {
            ClanLine(OnlineIds(clan), 1567, $"The war against {target.Name} Clan has been stopped.", target.Name);
            ClanLine(OnlineIds(target), 1566, $"The clan {clan.Name} has decided to stop the war.", clan.Name);
        }
        BroadcastClan(clan);
        BroadcastClan(target);
        BroadcastClanRelations(clan);
        BroadcastClanRelations(target);
        return null;
    }

    void SendWarList(ZoneSession session, ZoneClan clan)
    {
        var declared = clan.D.WarTargets
            .Where(_clans.ContainsKey)
            .Select(id => new PledgeWarNet { ClanId = id, Name = _clans[id].Name, Mutual = Declared(id, clan.Id) })
            .ToArray();
        var attackers = _clans.Values
            .Where(c => c.D.WarTargets.Contains(clan.Id))
            .Select(c => new PledgeWarNet { ClanId = c.Id, Name = c.Name, Mutual = clan.D.WarTargets.Contains(c.Id) })
            .ToArray();
        NetBridge?.FanoutPledgeWarList([session.ObjectId], new PledgeWarListNetMsg { Declared = declared, UnderAttack = attackers });
    }

    /// <summary>Mobius war kill reputation: killer clan +1 when the victim clan's score is above 0; victim clan −1 when the killer clan's is.</summary>
    void ApplyWarKillReputation(ZoneEntity killer, ZoneEntity victim)
    {
        if (!_clans.TryGetValue(ClanIdFor(killer.ObjectId), out var killerClan) ||
            !_clans.TryGetValue(ClanIdFor(victim.ObjectId), out var victimClan))
            return;
        bool victimHadRep = victimClan.D.Reputation > 0;
        bool killerHadRep = killerClan.D.Reputation > 0;
        if (victimHadRep)
            ChangeReputation(killerClan, PledgeProtocol.ReputationPerKill);
        if (killerHadRep)
            ChangeReputation(victimClan, -PledgeProtocol.ReputationPerKill);
    }

    /// <summary>Drops every war row naming <paramref name="clanId"/> (clan destroyed).</summary>
    void ForgetWars(int clanId)
    {
        foreach (var other in _clans.Values)
        {
            if (other.D.WarTargets.Remove(clanId))
            {
                SaveClan(other);
                BroadcastClan(other);
            }
        }
    }

    /// <summary>Test hook: both clans declare on each other (skips the size rules), relations re-sent.</summary>
    internal bool SetMutualWarForTest(int a, int b)
    {
        lock (_gate)
        {
            if (!_clans.TryGetValue(a, out var ca) || !_clans.TryGetValue(b, out var cb))
                return false;
            if (!ca.D.WarTargets.Contains(b))
                ca.D.WarTargets.Add(b);
            if (!cb.D.WarTargets.Contains(a))
                cb.D.WarTargets.Add(a);
            SaveClan(ca);
            SaveClan(cb);
            BroadcastClanRelations(ca);
            BroadcastClanRelations(cb);
            return true;
        }
    }
}
