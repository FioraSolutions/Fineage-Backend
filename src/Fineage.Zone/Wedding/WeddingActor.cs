using Fineage.Accounts;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Wedding;

/// <summary>Global wedding couples (Mobius <c>CoupleManager</c>).</summary>
public sealed class WeddingActor : GlobalActor
{
    public const string ActorName = "wedding";

    readonly Dictionary<int, WeddingCouple> _byId = [];
    readonly Dictionary<int, int> _engageInviterByInvitee = [];
    int _nextId = 1;

    public WeddingActor(IReadOnlyList<WeddingCoupleRecord>? seed = null, int nextId = 1)
        : base(ActorName)
    {
        _nextId = Math.Max(1, nextId);
        if (seed == null)
            return;
        foreach (var row in seed.OrderBy(r => r.Id))
        {
            if (_byId.ContainsKey(row.Id))
                continue;
            _byId[row.Id] = new WeddingCouple(row);
            _nextId = Math.Max(_nextId, row.Id + 1);
        }
    }

    public IReadOnlyCollection<WeddingCouple> Couples
    {
        get
        {
            RequireActorThread();
            return _byId.Values.ToList();
        }
    }

    public int NextId
    {
        get
        {
            RequireActorThread();
            return _nextId;
        }
    }

    public WeddingCouple? Find(int coupleId)
    {
        RequireActorThread();
        return _byId.GetValueOrDefault(coupleId);
    }

    public WeddingCouple? FindByPlayer(int objectId)
    {
        RequireActorThread();
        foreach (var couple in _byId.Values)
        {
            if (couple.Contains(objectId))
                return couple;
        }

        return null;
    }

    public WeddingCouple Create(int player1Id, int player2Id, long nowUnixMs)
    {
        RequireActorThread();
        int id = _nextId++;
        var couple = new WeddingCouple(id, player1Id, player2Id, nowUnixMs);
        _byId[id] = couple;
        return couple;
    }

    public bool TryDelete(int coupleId, out WeddingCouple? removed)
    {
        RequireActorThread();
        if (!_byId.Remove(coupleId, out removed))
            return false;
        foreach (var (invitee, inviter) in _engageInviterByInvitee.ToList())
        {
            if (inviter == removed!.Player1Id || inviter == removed.Player2Id)
                _engageInviterByInvitee.Remove(invitee);
        }

        return true;
    }

    public void SetEngageInvite(int inviteeId, int inviterId)
    {
        RequireActorThread();
        _engageInviterByInvitee[inviteeId] = inviterId;
    }

    public bool TakeEngageInvite(int inviteeId, out int inviterId)
    {
        RequireActorThread();
        return _engageInviterByInvitee.Remove(inviteeId, out inviterId);
    }

    public bool HasEngageInvite(int inviteeId)
    {
        RequireActorThread();
        return _engageInviterByInvitee.ContainsKey(inviteeId);
    }

    public void ClearEngageInvite(int inviteeId) => _engageInviterByInvitee.Remove(inviteeId);

    public IReadOnlyList<WeddingCoupleRecord> Snapshot()
    {
        RequireActorThread();
        return _byId.Values.OrderBy(c => c.Id).Select(c => c.ToRecord()).ToList();
    }

    public void ReplaceAll(IReadOnlyList<WeddingCoupleRecord> rows, int nextId)
    {
        RequireActorThread();
        _byId.Clear();
        _engageInviterByInvitee.Clear();
        _nextId = Math.Max(1, nextId);
        foreach (var row in rows.OrderBy(r => r.Id))
        {
            _byId[row.Id] = new WeddingCouple(row);
            _nextId = Math.Max(_nextId, row.Id + 1);
        }
    }
}