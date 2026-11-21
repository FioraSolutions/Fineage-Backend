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

namespace Fineage.Net.Contract
{
    /// <summary>
    /// Zone → client: a castle gate or wall (Mobius <c>DoorStatusUpdate</c> / <c>StaticObject</c>). Sent after the
    /// door's <c>EntitySpawn</c> and on every HP / open change. The client binds its static door mesh by
    /// <see cref="DoorId"/> (Mobius Doors.xml id) and keeps <see cref="ObjectId"/> for targeting and attacks.
    /// </summary>
    public sealed class DoorStatusNetMsg
    {
        public int ObjectId { get; set; }
        public int DoorId { get; set; }
        public bool Open { get; set; }
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        /// <summary>True while a siege runs at <see cref="CastleId"/>: attackers may hit the door.</summary>
        public bool Attackable { get; set; }
        public bool Wall { get; set; }
        public int CastleId { get; set; }
        /// <summary>Clan hall id when this row is an agit gate (<see cref="CastleId"/> is 0).</summary>
        public int HallId { get; set; }
        /// <summary>Chamberlain upgrade (Mobius <c>getUpgradeHpRatio</c>): 1 none, 2 / 3 / 5 HP multiplier.</summary>
        public int UpgradeRatio { get; set; } = 1;
    }

    /// <summary>Castle lord / chamberlain actions (<see cref="CastleCommandNetMsg.Kind"/>).</summary>
    public static class CastleCommandKind
    {
        /// <summary>Castle page: <see cref="CastleInfoNetMsg"/> back.</summary>
        public const int Info = 0;
        /// <summary>Amount = new tax percent (0..15). Leader or <c>PrivCastleVault</c>; not during a siege.</summary>
        public const int SetTax = 1;
        /// <summary>Amount = adena moved from the treasury to the inventory.</summary>
        public const int Withdraw = 2;
        /// <summary>Amount = adena moved from the inventory to the treasury.</summary>
        public const int Deposit = 3;
        /// <summary>IntArg1 = door id, IntArg2 = HP ratio 2 / 3 / 5 (Mobius chamberlain <c>upgrade_doors_confirm</c>).</summary>
        public const int UpgradeDoor = 4;
        /// <summary>Lord's Crown for the castle lord (Mobius chamberlain <c>give_crown</c>).</summary>
        public const int TakeCrown = 5;
        /// <summary>The castle circlet for any owner-clan member.</summary>
        public const int TakeCirclet = 6;
        /// <summary>IntArg1 = door id, IntArg2 = 1 open / 0 close (<c>PrivCastleOpenDoor</c>; refused during a siege).</summary>
        public const int OperateDoor = 7;
    }

    /// <summary>Client → zone: one castle lord action. The chamberlain must be in interaction range.</summary>
    public sealed class CastleCommandNetMsg
    {
        public int Kind { get; set; }
        public int CastleId { get; set; }
        public int IntArg1 { get; set; }
        public int IntArg2 { get; set; }
        public long Amount { get; set; }
    }

    public sealed class CastleDoorNet
    {
        public int DoorId { get; set; }
        public int UpgradeType { get; set; }
        public int UpgradeRatio { get; set; } = 1;
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        public bool Open { get; set; }
    }

    /// <summary>Zone → client: castle page for the owner clan (others see owner / tax / siege date, treasury 0).</summary>
    public sealed class CastleInfoNetMsg
    {
        public int CastleId { get; set; }
        public string Name { get; set; } = "";
        public int OwnerClanId { get; set; }
        public string OwnerClanName { get; set; } = "";
        public int TaxPercent { get; set; }
        public int MaxTaxPercent { get; set; }
        public long Treasury { get; set; }
        public long SiegeUnixMs { get; set; }
        public bool InProgress { get; set; }
        public CastleDoorNet[] Doors { get; set; } = System.Array.Empty<CastleDoorNet>();
    }
}
