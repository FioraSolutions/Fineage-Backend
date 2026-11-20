using System.Diagnostics.CodeAnalysis;
using Fineage.Accounts;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Clan;

internal sealed class ZoneClan
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int LeaderId { get; set; }
    public long DissolvingExpiryUnixMs { get; set; }
    public long CharPenaltyExpiryUnixMs { get; set; }
    /// <summary>Mobius <c>hasCastle</c>. 0 when this clan owns no castle.</summary>
    public int CastleId { get; set; }
    /// <summary>Mobius <c>crest_id</c> (pledge crest, 0 none).</summary>
    public int CrestId { get; set; }
    /// <summary>Mobius <c>clan_skills</c> book, copied onto the members.</summary>
    public Dictionary<int, int> Skills { get; } = [];
    public List<ZoneClanMember> Members { get; } = [];
    /// <summary>Reputation, sub-units, privileges, wars, ally, member grades, warehouse rows (persisted in the clan payload).</summary>
    public ClanDetails D { get; set; } = new();
    /// <summary>Mobius <c>ClanWarehouse</c>, rebuilt from <see cref="ClanDetails.Warehouse"/> on first use.</summary>
    public Fineage.Game.Services.PrivateWarehouse? Warehouse { get; set; }
}

internal sealed class ZoneClanMember
{
    public int CharacterId { get; init; }
    public string Account { get; set; } = "";
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Level { get; set; }
    /// <summary>Mobius <c>ClanMember.getPledgeType</c>: 0 main, -1 academy, 100/200 royal, 1001..2002 knights.</summary>
    public int PledgeType { get; set; }
}

internal readonly record struct PendingClanInvite(int InviterObjectId, int ClanId, long ExpiresUnixMs, int PledgeType = 0);

/// <summary>A clan whose dissolution deadline passed: the world destroys it.</summary>
internal readonly record struct ClanDissolutionDue(int ClanId);

/// <summary>
/// Clans as a social actor keyed by clan id: the clans, which character belongs to which, the join and create
/// penalties of the characters seen, and pending invites. Commands read and change them as requests; the step,
/// on the calendar, finds the clans whose dissolution deadline passed, in ascending id order. Clans are listed in
/// ascending id order.
/// </summary>
internal sealed class ClanActor : GlobalActor<ClanDissolutionDue>
{
    public const string StageName = "clan.dissolutions";

    readonly SortedDictionary<int, ZoneClan> _clans = [];
    readonly Dictionary<int, int> _byCharacter = [];
    readonly Dictionary<int, long> _joinExpiry = [];
    readonly Dictionary<int, long> _createExpiry = [];
    readonly SortedDictionary<int, PendingClanInvite> _invites = [];
    int _nextClanId = 1;

    public ClanActor()
        : base(StageName, ActorTime.Calendar)
    {
    }

    public int Count
    {
        get
        {
            RequireActorThread();
            return _clans.Count;
        }
    }

    public IEnumerable<ZoneClan> All
    {
        get
        {
            RequireActorThread();
            return _clans.Values;
        }
    }

    public bool Contains(int clanId)
    {
        RequireActorThread();
        return _clans.ContainsKey(clanId);
    }

    public bool TryGet(int clanId, [NotNullWhen(true)] out ZoneClan? clan)
    {
        RequireActorThread();
        return _clans.TryGetValue(clanId, out clan);
    }

    public ZoneClan Get(int clanId)
    {
        RequireActorThread();
        return _clans.TryGetValue(clanId, out var clan) ? clan : throw new KeyNotFoundException($"clan {clanId} is not on the roster");
    }

    /// <summary>The id of a clan created without a store.</summary>
    public int AllocateId()
    {
        RequireActorThread();
        return _nextClanId++;
    }

    public void Add(ZoneClan clan)
    {
        RequireActorThread();
        _clans[clan.Id] = clan;
    }

    public void Remove(int clanId)
    {
        RequireActorThread();
        _clans.Remove(clanId);
    }

    /// <summary>Forgets every clan and membership (the store is read again).</summary>
    public void Clear()
    {
        RequireActorThread();
        _clans.Clear();
        _byCharacter.Clear();
    }

    /// <summary>The clan of the character, or 0 when it has none or that clan is gone.</summary>
    public int ClanIdOf(int characterId)
    {
        RequireActorThread();
        return _byCharacter.TryGetValue(characterId, out int id) && _clans.ContainsKey(id) ? id : 0;
    }

    public void Link(int characterId, int clanId)
    {
        RequireActorThread();
        _byCharacter[characterId] = clanId;
    }

    public void Unlink(int characterId)
    {
        RequireActorThread();
        _byCharacter.Remove(characterId);
    }

    public bool TryGetJoinExpiry(int characterId, out long unixMs)
    {
        RequireActorThread();
        return _joinExpiry.TryGetValue(characterId, out unixMs);
    }

    public void SetJoinExpiry(int characterId, long unixMs)
    {
        RequireActorThread();
        _joinExpiry[characterId] = unixMs;
    }

    public bool TryGetCreateExpiry(int characterId, out long unixMs)
    {
        RequireActorThread();
        return _createExpiry.TryGetValue(characterId, out unixMs);
    }

    public void SetCreateExpiry(int characterId, long unixMs)
    {
        RequireActorThread();
        _createExpiry[characterId] = unixMs;
    }

    /// <summary>Pending invites by invitee, in ascending invitee order (a copy).</summary>
    public List<KeyValuePair<int, PendingClanInvite>> Invites
    {
        get
        {
            RequireActorThread();
            return [.. _invites];
        }
    }

    public void Invite(int inviteeObjectId, PendingClanInvite invite)
    {
        RequireActorThread();
        _invites[inviteeObjectId] = invite;
    }

    public bool TryGetInvite(int inviteeObjectId, out PendingClanInvite invite)
    {
        RequireActorThread();
        return _invites.TryGetValue(inviteeObjectId, out invite);
    }

    public bool TakeInvite(int inviteeObjectId, out PendingClanInvite invite)
    {
        RequireActorThread();
        return _invites.Remove(inviteeObjectId, out invite);
    }

    public void DropInvite(int inviteeObjectId)
    {
        RequireActorThread();
        _invites.Remove(inviteeObjectId);
    }

    /// <summary>The player has a clan invite pending, received or sent.</summary>
    public bool InInvite(int objectId)
    {
        RequireActorThread();
        return _invites.ContainsKey(objectId) || _invites.Values.Any(i => i.InviterObjectId == objectId);
    }

    protected override void OnStep(long nowUnixMs, List<ClanDissolutionDue> effects)
    {
        foreach (var clan in _clans.Values)
        {
            if (ClanRules.DissolveDue(clan.DissolvingExpiryUnixMs, nowUnixMs))
                effects.Add(new ClanDissolutionDue(clan.Id));
        }
    }
}
