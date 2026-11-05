namespace Fineage.Net.Contract
{
    /// <summary>Client → zone: ask for one castle's siege state.</summary>
    public sealed class SiegeListRequestNetMsg
    {
        public int CastleId { get; set; }
    }

    /// <summary>
    /// Client → zone: register or withdraw. <see cref="Attacker"/> 1 means attacker, 0 means defender;
    /// <see cref="Joining"/> 1 registers, any other value unregisters.
    /// </summary>
    public sealed class SiegeRegisterRequestNetMsg
    {
        public int CastleId { get; set; }
        public int Attacker { get; set; }
        public int Joining { get; set; }
    }

    /// <summary>Client → zone: the owner answers a waiting defender. <see cref="Approved"/> 1 accepts.</summary>
    public sealed class SiegeDefenderAnswerRequestNetMsg
    {
        public int CastleId { get; set; }
        public int ClanId { get; set; }
        public int Approved { get; set; }
    }

    /// <summary>Client → zone: admin capture. Gated by <c>FINEAGE_ZONE_ADMIN</c>.</summary>
    public sealed class AdminCaptureCastleNetMsg
    {
        public int CastleId { get; set; }
        public int ClanId { get; set; }
    }

    /// <summary>One registered clan. Side is owner -1, defender 0, attacker 1, or pending 2.</summary>
    public sealed class SiegeClanNet
    {
        public int ClanId { get; set; }
        public int Side { get; set; }
    }

    /// <summary>Zone → client: the castle and its current registrations.</summary>
    public sealed class SiegeInfoNetMsg
    {
        public int CastleId { get; set; }
        public string Name { get; set; } = "";
        public int OwnerClanId { get; set; }
        public int TaxPercent { get; set; }
        public long SiegeUnixMs { get; set; }
        public long RegistrationDeadlineUnixMs { get; set; }
        public bool RegistrationClosed { get; set; }
        public bool InProgress { get; set; }
        public SiegeClanNet[] Clans { get; set; } = System.Array.Empty<SiegeClanNet>();
        /// <summary>Unix milliseconds when the siege ends. 0 when it is not in progress.</summary>
        public long EndsUnixMs { get; set; }
    }
}
