namespace Fineage.Net.Contract
{
    /// <summary>Client → zone: ask for the Seven Signs record. Empty body.</summary>
    public sealed class SevenSignsStatusRequestNetMsg
    {
    }

    /// <summary>
    /// Client → zone: join a cabal or hand in seal stones.
    /// <see cref="Action"/> is <c>SevenSignsProtocol.ActionJoin</c> or <c>ActionContribute</c>.
    /// </summary>
    public sealed class SevenSignsActionRequestNetMsg
    {
        public int Action { get; set; }
        /// <summary>1 Dusk, 2 Dawn. Used by join.</summary>
        public int Cabal { get; set; }
        /// <summary>1 Avarice, 2 Gnosis, 3 Strife. Used by join.</summary>
        public int Seal { get; set; }
        public int BlueStones { get; set; }
        public int GreenStones { get; set; }
        public int RedStones { get; set; }
    }

    /// <summary>Zone → client: one record instead of the four Interlude SSQStatus pages.</summary>
    public sealed class SevenSignsStatusNetMsg
    {
        public int Period { get; set; }
        public int Cycle { get; set; }
        public int PlayerCabal { get; set; }
        public int PlayerSeal { get; set; }
        public int PlayerStoneCount { get; set; }
        public int PlayerAncientAdena { get; set; }
        public int PlayerContribution { get; set; }
        public int DawnScore { get; set; }
        public int DuskScore { get; set; }
        public int DawnFestivalScore { get; set; }
        public int DuskFestivalScore { get; set; }
        public long DawnStonePoints { get; set; }
        public long DuskStonePoints { get; set; }
        public int AvariceOwner { get; set; }
        public int GnosisOwner { get; set; }
        public int StrifeOwner { get; set; }
        public int DawnMembers { get; set; }
        public int DuskMembers { get; set; }
        public int WinningCabal { get; set; }
        public long NextChangeUnixMs { get; set; }
        public int SkyState { get; set; }
        public int DawnAvarice { get; set; }
        public int DawnGnosis { get; set; }
        public int DawnStrife { get; set; }
        public int DuskAvarice { get; set; }
        public int DuskGnosis { get; set; }
        public int DuskStrife { get; set; }
        public int PreviousWinner { get; set; }
    }

    /// <summary>Zone → client: sky tint. <see cref="State"/> 0 normal, 1 dusk, 2 dawn.</summary>
    public sealed class SevenSignsSkyNetMsg
    {
        public int State { get; set; }
        public int Period { get; set; }
        public int WinningCabal { get; set; }
    }
}
