using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void LaunchFestivalRegistration(int partyId)
    {
        var reg = FestivalBoard.Registrations().FirstOrDefault(r => r.PartyId == partyId);
        if (reg == null || reg.Launched)
            return;
        if (!_byId.TryGetValue(reg.LeaderObjectId, out var leader))
            return;
        reg.Launched = true;
        var error = TryStartFestivalEnter(leader, reg.TierId, reg.Cabal, forceScheduled: true, skipScheduleGate: false);
        if (error != null)
            reg.Launched = false;
    }
}