using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>Alliance requests (Block D). Not implemented yet: every alliance command is refused.</summary>
public sealed partial class ZoneWorld
{
    string? TryAllyCommand(ZoneSession session, ZoneEntity player, ZoneClan? clan, PledgeCommandNetMsg c, long now) =>
        PledgeProtocol.Denied;
}
