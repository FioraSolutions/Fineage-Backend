namespace Fineage.Zone.World;

/// <summary>Mobius <c>Duel.DuelState</c> of one fighter.</summary>
enum DuelFighterState : byte
{
    /// <summary>Not fighting (no duel, or the countdown still runs).</summary>
    None = 0,
    Duelling = 1,
    /// <summary>Beaten down to 1 HP: takes no damage, cannot act until the duel ends.</summary>
    Dead = 2,
    Winner = 3,
    /// <summary>Someone outside the duel hit this fighter, or it hit an outsider (1v1 is canceled).</summary>
    Interrupted = 4,
}

sealed partial class ZoneEntity
{
    /// <summary>Running or starting duel (0 = none). Set from the accept on, so a second challenge is refused.</summary>
    public int DuelId { get; set; }
    /// <summary>Team circle on the wire (Mobius setTeam): 0 none, 1 blue (challengers), 2 red.</summary>
    public byte DuelTeam { get; set; }
    public DuelFighterState DuelState { get; set; }
    /// <summary>Pending challenge received: requester object id (0 = none), party flag and expiry.</summary>
    public int DuelAskFrom { get; set; }
    public bool DuelAskParty { get; set; }
    public long DuelAskUntilMs { get; set; }
    /// <summary>Answered -1: further duel asks this session are refused.</summary>
    public bool DuelRefuse { get; set; }

    /// <summary>The duel is fighting and this player still takes part (not beaten, not the winner).</summary>
    public bool DuelActive => DuelId != 0 && DuelState is DuelFighterState.Duelling or DuelFighterState.Interrupted;
}
