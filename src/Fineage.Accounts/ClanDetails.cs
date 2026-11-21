using System.Text.Json;

namespace Fineage.Accounts;

/// <summary>
/// Interlude clan state beyond the roster (Mobius <c>clan_data</c> columns, <c>clan_subpledges</c>, <c>clan_privs</c>,
/// <c>clan_wars</c>, member <c>power_grade</c> / <c>apprentice</c> / <c>sponsor</c> / <c>lvl_joined_academy</c>, and the
/// clan warehouse). Persisted inside the clan payload (snapshot + <c>clans.payload</c> jsonb).
/// </summary>
public sealed class ClanDetails
{
    public int Reputation { get; set; }
    public int LargeCrestId { get; set; }
    public int AllyId { get; set; }
    public string? AllyName { get; set; }
    public int AllyCrestId { get; set; }
    public long AllyPenaltyExpiryUnixMs { get; set; }
    /// <summary>Mobius <c>PENALTY_TYPE_*</c>: 1 clan left, 2 clan dismissed, 3 dismissed a clan, 4 dissolved ally.</summary>
    public int AllyPenaltyType { get; set; }
    /// <summary>Mobius <c>new_leader_id</c>: delegation waiting for the next server start.</summary>
    public int NewLeaderId { get; set; }
    /// <summary>Mobius <c>hasHideout</c>: owned clan hall id.</summary>
    public int HallId { get; set; }
    public List<SubPledgeSave> SubPledges { get; set; } = [];
    /// <summary>Mobius <c>clan_privs</c>: power grade 1–9 → privilege mask.</summary>
    public Dictionary<int, int> RankPrivileges { get; set; } = [];
    /// <summary>Clans this clan declared war on (Mobius <c>clan_wars.clan1 = this</c>).</summary>
    public List<int> WarTargets { get; set; } = [];
    /// <summary>Enemy clan id → unix ms when a new declaration is allowed again (Mobius post-war penalty, five days).</summary>
    public Dictionary<int, long> WarCooldownUntilUnixMs { get; set; } = [];
    public Dictionary<int, ClanMemberDetails> Members { get; set; } = [];
    public List<ClanItemSave> Warehouse { get; set; } = [];

    public ClanDetails Clone() =>
        JsonSerializer.Deserialize<ClanDetails>(JsonSerializer.Serialize(this)) ?? new ClanDetails();
}

public sealed class SubPledgeSave
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public int LeaderId { get; set; }
}

public sealed class ClanMemberDetails
{
    public int PowerGrade { get; set; }
    public int SponsorId { get; set; }
    public int ApprenticeId { get; set; }
    public int LevelJoinedAcademy { get; set; }
}

public sealed class ClanItemSave
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Uploaded crest blob (Mobius <c>crests</c>): kind 0 pledge 16x12, 1 large 256x64 (8 parts), 2 ally 8x12.</summary>
public sealed record CrestSave(int Id, int Kind, byte[] Data);

/// <summary>Clan hall ownership and auction state (Mobius <c>clanhall</c> + <c>auction</c> + <c>auction_bid</c>).</summary>
public sealed class ClanHallSave
{
    public int Id { get; set; }
    public int OwnerClanId { get; set; }
    public long PaidUntilUnixMs { get; set; }
    public long AuctionEndUnixMs { get; set; }
    public long StartingBid { get; set; }
    public List<ClanHallBidSave> Bids { get; set; } = [];
    public List<ClanHallFunctionSave> Functions { get; set; } = [];
    public int DecoFront { get; set; }
    public int DecoCurtains { get; set; }
}

public sealed class ClanHallFunctionSave
{
    public int Type { get; set; }
    public int Level { get; set; }
    public int Lease { get; set; }
    public long EndUnixMs { get; set; }
}

public sealed class ClanHallBidSave
{
    public int ClanId { get; set; }
    public string BidderName { get; set; } = "";
    public long Amount { get; set; }
    public long TimeUnixMs { get; set; }
}

/// <summary>Mobius <c>siegable_clanhall</c> + <c>clanhall_siege_attackers</c>.</summary>
public sealed record SiegableClanHallSave(
    int Id,
    int OwnerClanId,
    long NextSiegeUnixMs,
    bool RegistrationClosed,
    bool InProgress,
    long EndsUnixMs,
    IReadOnlyList<int>? Attackers = null,
    int PendingWinnerClanId = 0);
