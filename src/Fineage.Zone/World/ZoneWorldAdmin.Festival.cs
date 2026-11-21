using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static bool IsAdminFestivalVerb(string verb) =>
        verb.Equals("admin_festival_enter", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("admin_festival_end", StringComparison.OrdinalIgnoreCase);

    string? TryAdminFestival(ZoneSession session, ZoneEntity player, string verb, string args)
    {
        if (verb.Equals("admin_festival_end", StringComparison.OrdinalIgnoreCase))
        {
            if (_party.Of(player.ObjectId) is not { } party)
                return "no_party";
            TryAdminFestivalEnd(party.PartyId);
            return null;
        }

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int tier))
            return "bad_request";
        var cabal = parts[1].Equals("dawn", StringComparison.OrdinalIgnoreCase)
            ? SevenSignsCabal.Dawn
            : parts[1].Equals("dusk", StringComparison.OrdinalIgnoreCase)
                ? SevenSignsCabal.Dusk
                : SevenSignsCabal.None;
        if (cabal == SevenSignsCabal.None)
            return "bad_request";
        return TryAdminFestivalEnter(player, tier, cabal);
    }
}