using Fineage.Accounts;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Clan;

/// <summary>Function lease expiry ticks on the same calendar schedule as hall auctions.</summary>
public readonly record struct HallFunctionDue(int HallId, int Type);

public sealed class HallFunctionActor() : GlobalActor<HallFunctionDue>(StageName, ActorTime.Calendar)
{
    public const string StageName = "clan.hall-functions";

    Dictionary<int, ClanHallSave>? _halls;

    public Dictionary<int, ClanHallSave>? Halls
    {
        get
        {
            RequireActorThread();
            return _halls;
        }
        set
        {
            RequireActorThread();
            _halls = value;
        }
    }

    protected override void OnStep(long nowUnixMs, List<HallFunctionDue> effects)
    {
        if (_halls == null)
            return;
        foreach (var hall in _halls.Values.OrderBy(h => h.Id))
        {
            if (hall.OwnerClanId <= 0)
                continue;
            foreach (var fn in hall.Functions)
            {
                if (fn.Level > 0 && fn.EndUnixMs > 0 && fn.EndUnixMs <= nowUnixMs)
                    effects.Add(new HallFunctionDue(hall.Id, fn.Type));
            }

            if (hall.PaidUntilUnixMs > 0 && hall.PaidUntilUnixMs <= nowUnixMs)
                effects.Add(new HallFunctionDue(hall.Id, Type: 0));
        }
    }
}