namespace Fineage.Net.Contract
{
    /// <summary>
    /// One fishing event (Zone → client), after Mobius <c>ExFishingStart</c>, <c>ExFishingStartCombat</c>,
    /// <c>ExFishingHpRegen</c> and <c>ExFishingEnd</c>. <see cref="Kind"/>: 0 start, 1 combat start, 2 HP tick / pull,
    /// 3 end. Start, combat, pulls and end go to the fisher and everyone who sees them; plain HP ticks only to the fisher.
    /// </summary>
    public sealed class FishingEventNetMsg
    {
        public int Kind { get; set; }
        public int FisherObjectId { get; set; }
        /// <summary>Start: fish group (−1 = nothing will bite: wrong lure for the time of day).</summary>
        public int FishGroup { get; set; }
        /// <summary>Start: bait position on the water surface.</summary>
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool NightLure { get; set; }
        /// <summary>Combat / HP: seconds left.</summary>
        public int Time { get; set; }
        /// <summary>Combat: max HP. HP: current HP (the fish escapes at twice the max).</summary>
        public int Hp { get; set; }
        /// <summary>0 = fish resting (pump), 1 = fish fighting (reel).</summary>
        public int Mode { get; set; }
        /// <summary>Combat: 0 beginner, 1 normal, 2 upper grade.</summary>
        public int LureType { get; set; }
        /// <summary>1 = the fish's shown mode is reversed for the pull that works.</summary>
        public int Deceptive { get; set; }
        /// <summary>HP: 0 none, 1 good pull, 2 bad pull.</summary>
        public int GoodUse { get; set; }
        /// <summary>HP: 0 none, 1 pumping, 2 reeling.</summary>
        public int Anim { get; set; }
        public int Penalty { get; set; }
        /// <summary>End: the fish was caught.</summary>
        public bool Win { get; set; }
    }
}
