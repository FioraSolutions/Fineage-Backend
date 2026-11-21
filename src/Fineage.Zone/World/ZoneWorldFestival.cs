using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Punishment;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int FestivalInstanceBase = 3_000_000;

    SevenSignsFestivalBoard FestivalBoard => _festivalActor.Board;

    public bool PlayerInFestivalArena(int objectId) => PlayerZoneChecks.IsInFestivalArena(this, objectId);

    void TickSevenSignsFestival(Simulation.TickContext tick)
    {
        foreach (var effect in _festivalActor.Step(tick))
            ApplyFestivalEffect(effect);
    }

    /// <summary>GM / test entry: party leader starts a festival instance (competition period only).</summary>
    internal string? TryAdminFestivalEnter(ZoneEntity leader, int tierId, SevenSignsCabal cabal) =>
        TryStartFestivalEnter(leader, tierId, cabal, forceScheduled: true, skipScheduleGate: true);

    string? TryStartFestivalEnter(ZoneEntity leader, int tierId, SevenSignsCabal cabal, bool forceScheduled = false, bool skipScheduleGate = false)
    {
        if (_sevenSigns.Engine.Period != SevenSignsPeriod.Competition)
            return "festival_closed";
        if (!skipScheduleGate && !forceScheduled && !FestivalBoard.FestivalSliceActive(NowUnixMs()))
            return "festival_closed";
        if (tierId is < 0 or > 4)
            return "bad_request";
        if (cabal is not (SevenSignsCabal.Dawn or SevenSignsCabal.Dusk))
            return "bad_request";
        if (_party.Of(leader.ObjectId) is not { } party || party.LeaderObjectId != leader.ObjectId)
            return "no_party";
        if (FestivalBoard.PartyInFestival(party.PartyId))
            return "in_festival";

        var members = PartyMembersOf(party);
        int costEach = 1;
        foreach (var member in members)
        {
            if (member.Inventory == null || member.Inventory.CountOf(_festivalCatalog.OfferingItemId) < costEach)
                return "no_offerings";
        }

        return RecordBagChange(
            leader,
            LedgerTxKind.SevenSignsContribute,
            LedgerAccount.Of(SystemAccount.SevenSigns),
            () =>
            {
                long offeringScore = 0;
                foreach (var member in members)
                {
                    if (member.Session == null || member.Inventory == null)
                        return (string?)"world";
                    var changes = new List<InventoryUpdateEntryMsg>();
                    Take(member.Inventory, _festivalCatalog.OfferingItemId, costEach, changes);
                    offeringScore += costEach * _festivalCatalog.OfferingValue;
                    if (changes.Count > 0 && member.Session.ObjectId == leader.Session?.ObjectId)
                    {
                        NetBridge?.FanoutInventoryUpdate([member.Session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
                        SaveInventory(member.Session, member.Inventory);
                    }
                    else if (member.Session.ObjectId != leader.Session?.ObjectId)
                        SaveInventory(member.Session, member.Inventory);
                }

                int instanceId = ++_nextFestivalInstanceId;
                long now = NowUnixMs();
                _festivalActor.StartInstance(
                    party.PartyId,
                    leader.ObjectId,
                    tierId,
                    cabal,
                    party.MemberIds,
                    instanceId,
                    offeringScore,
                    now);
                foreach (var member in members)
                    TeleportToFestivalArena(member, tierId, cabal, instanceId);
                return (string?)null;
            },
            _ => tierId);
    }

    internal void TryAdminFestivalEnd(int partyId) => EndFestivalInstance(partyId, recordScore: true);

    void TeleportToFestivalArena(ZoneEntity player, int tierId, SevenSignsCabal cabal, int instanceId)
    {
        var tier = _festivalCatalog.Tier(tierId);
        var side = _festivalCatalog.Side(tier, cabal);
        var rng = Rng(_serviceRandom, player);
        int max = _festivalCatalog.MaxOffset;
        int ox = rng.Next(-max, max + 1);
        int oy = rng.Next(-max, max + 1);
        Relocate(player, side.PlayerSpawn.X + ox, side.PlayerSpawn.Y + oy, side.PlayerSpawn.Z);
        player.InstanceId = instanceId;
        PlayerZoneChecks.RegisterFestivalParticipation(this, player.ObjectId, inside: true);
    }

    void EndFestivalInstance(int partyId, bool recordScore)
    {
        if (FestivalBoard.OfParty(partyId) is not { } instance)
            return;
        foreach (int id in instance.MemberSnapshot)
        {
            if (_byId.TryGetValue(id, out var member) && member.Session != null)
            {
                member.InstanceId = 0;
                PlayerZoneChecks.RegisterFestivalParticipation(this, id, inside: false);
                Relocate(member, 83400, 147943, -3404);
            }
        }

        FestivalBoard.RememberPreviousParticipants(instance.Cabal, instance.TierId, instance.LeaderObjectId, instance.MemberSnapshot);
        FestivalBoard.UnregisterParty(partyId);
        _ = recordScore;

        UnspawnFestivalInstance(instance);
        _festivalActor.KillInstance(partyId);
    }
}