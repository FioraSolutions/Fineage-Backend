using Fineage.Protocol;

namespace Fineage.Zone.Petition;

/// <summary>Mobius <c>PetitionManager</c> — one in-memory GM petition queue.</summary>
public sealed class PetitionQueue
{
    readonly Dictionary<int, PetitionEntry> _pending = new();
    readonly Dictionary<int, PetitionEntry> _completed = new();
    readonly Dictionary<int, int> _dailyCounts = new();
    readonly Action<string> _warn;
    int _nextId = 1;

    public PetitionQueue(Action<string>? warn = null) => _warn = warn ?? (_ => { });

    public int PendingCount => _pending.Count;

    public bool TryGet(int petitionId, out PetitionEntry entry) =>
        _pending.TryGetValue(petitionId, out entry!);

    public bool IsPlayerPending(int characterId) =>
        _pending.Values.Any(p => p.PetitionerObjectId == characterId);

    public bool IsPlayerInConsultation(int characterId) =>
        _pending.Values.Any(p =>
            p.State == PetitionState.InProcess &&
            (p.PetitionerObjectId == characterId || p.ResponderObjectId == characterId));

    public int PlayerTotalCount(int characterId) =>
        _dailyCounts.GetValueOrDefault(characterId, 0);

    public int Submit(int petitionerObjectId, string petitionerName, string content, int type)
    {
        var entry = new PetitionEntry
        {
            Id = _nextId++,
            PetitionerObjectId = petitionerObjectId,
            PetitionerName = petitionerName,
            Content = content,
            Type = type,
            State = PetitionState.Pending,
            SubmitUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        _pending[entry.Id] = entry;
        _dailyCounts[petitionerObjectId] = PlayerTotalCount(petitionerObjectId) + 1;
        return entry.Id;
    }

    public bool CancelByPlayer(int characterId, out PetitionEntry? entry)
    {
        entry = _pending.Values.FirstOrDefault(p => p.PetitionerObjectId == characterId);
        if (entry == null)
            return false;
        return End(entry.Id, PetitionState.PetitionerCancel, out entry);
    }

    public bool Accept(int gmObjectId, string gmName, int petitionId, out PetitionEntry? entry)
    {
        if (!_pending.TryGetValue(petitionId, out entry))
        {
            _warn($"[petition] missing petition id={petitionId} accept by gm={gmObjectId}");
            entry = null;
            return false;
        }

        if (entry.ResponderObjectId > 0)
            return false;

        entry.ResponderObjectId = gmObjectId;
        entry.ResponderName = gmName;
        entry.State = PetitionState.InProcess;
        return true;
    }

    public bool End(int petitionId, PetitionState endState, out PetitionEntry? entry)
    {
        if (!_pending.Remove(petitionId, out entry))
        {
            _warn($"[petition] missing petition id={petitionId} end state={endState}");
            entry = null;
            return false;
        }

        entry.State = endState;
        _completed[petitionId] = entry;
        return true;
    }

    public bool TryActiveFor(int characterId, out PetitionEntry entry)
    {
        entry = _pending.Values.FirstOrDefault(p =>
            p.State == PetitionState.InProcess &&
            (p.PetitionerObjectId == characterId || p.ResponderObjectId == characterId))!;
        return entry != null;
    }
}

public sealed class PetitionEntry
{
    public int Id { get; init; }
    public int PetitionerObjectId { get; init; }
    public string PetitionerName { get; init; } = "";
    public int ResponderObjectId { get; set; }
    public string ResponderName { get; set; } = "";
    public string Content { get; init; } = "";
    public int Type { get; init; }
    public PetitionState State { get; set; }
    public long SubmitUnixMs { get; init; }
    public List<(int ChatType, int SpeakerObjectId, string Name, string Text)> Log { get; } = new();
}
