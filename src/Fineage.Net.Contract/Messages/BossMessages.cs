namespace Fineage.Net.Contract
{
    /// <summary>Client → zone: ask for one boss row, or every row when <see cref="NpcId"/> is 0.</summary>
    public sealed class BossStatusRequestNetMsg
    {
        public int NpcId { get; set; }
    }

    /// <summary>One boss row: the Mobius respawn schedule plus where the pawn is right now.</summary>
    public sealed class BossStatusEntryNet
    {
        public int NpcId { get; set; }
        public int ObjectId { get; set; }
        /// <summary>0 raid, 1 grand.</summary>
        public int Kind { get; set; }
        /// <summary>0 alive, 1 waiting, 2 fighting, 3 dead.</summary>
        public int Status { get; set; }
        public long RespawnUnixMs { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
        public bool Enabled { get; set; }
        /// <summary>True while the object id is in the world.</summary>
        public bool Spawned { get; set; }
    }

    /// <summary>Zone → client: the requested rows, sorted by npc id.</summary>
    public sealed class BossStatusNetMsg
    {
        public BossStatusEntryNet[] Bosses { get; set; } = System.Array.Empty<BossStatusEntryNet>();
    }

    /// <summary>
    /// Client → zone: admin schedule edit. <see cref="Action"/> 1 marks the boss dead (starting its respawn
    /// window), 2 force-spawns it ignoring the <c>enabled</c> flag. Gated by <c>FINEAGE_ZONE_ADMIN</c>.
    /// </summary>
    public sealed class AdminBossScheduleNetMsg
    {
        public int NpcId { get; set; }
        public int Action { get; set; }
    }
}
