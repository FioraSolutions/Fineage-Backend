namespace Fineage.Net.Contract
{
    /// <summary>Client → zone: ask for the Olympiad sheet. Empty body.</summary>
    public sealed class OlympiadStatusRequestNetMsg
    {
    }

    /// <summary>Client → zone: join a queue. <see cref="Classed"/> 1 is the class queue, 0 the non-classed one.</summary>
    public sealed class OlympiadRegisterRequestNetMsg
    {
        public int Classed { get; set; }
    }

    /// <summary>Client → zone: leave the queue. Empty body.</summary>
    public sealed class OlympiadUnregisterRequestNetMsg
    {
    }

    /// <summary>Client → zone: leave the running match. Counts as a loss. Empty body.</summary>
    public sealed class OlympiadLeaveRequestNetMsg
    {
    }

    /// <summary>Client → zone: admin match report. Gated by <c>FINEAGE_ZONE_ADMIN</c>; winner 0 is a draw.</summary>
    public sealed class AdminReportOlympiadResultNetMsg
    {
        public int MatchId { get; set; }
        public int WinnerObjectId { get; set; }
    }

    /// <summary>Zone → client: the Olympiad period, this character's standing, and its queue/match slot.</summary>
    public sealed class OlympiadStatusNetMsg
    {
        public int Period { get; set; }
        public int Cycle { get; set; }
        public bool InCompetition { get; set; }
        public long CompetitionStartUnixMs { get; set; }
        public long CompetitionEndUnixMs { get; set; }
        public long OlympiadEndUnixMs { get; set; }
        public long ValidationEndUnixMs { get; set; }
        public long NextWeeklyUnixMs { get; set; }
        public bool RegistrationOpen { get; set; }
        public bool Noble { get; set; }
        public int Points { get; set; }
        public int CompetitionsDone { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
        public int Drawn { get; set; }
        /// <summary>0 none, 1 classed queue, 2 non-classed queue.</summary>
        public int Queue { get; set; }
        public int MatchId { get; set; }
        /// <summary>Stadium index, or -1 when this character is not in a match.</summary>
        public int StadiumId { get; set; }
        public int ClassId { get; set; }
    }

    /// <summary>Zone → client: the match this character was placed in, sent to both participants.</summary>
    public sealed class OlympiadMatchNetMsg
    {
        public int MatchId { get; set; }
        public int StadiumId { get; set; }
        public int ZoneId { get; set; }
        public bool Classed { get; set; }
        /// <summary>0 countdown, 1 pause, 2 battle countdown, 3 fighting, 4 returning.</summary>
        public int Phase { get; set; }
        public int PlayerOneId { get; set; }
        public int PlayerTwoId { get; set; }
        public string PlayerOneName { get; set; } = "";
        public string PlayerTwoName { get; set; } = "";
        public int InstanceId { get; set; }
    }

    /// <summary>Zone → client: one countdown step for both participants.</summary>
    public sealed class OlympiadCountdownNetMsg
    {
        public int MatchId { get; set; }
        public int Kind { get; set; }
        public int Seconds { get; set; }
    }

    /// <summary>Zone → client: the finished match and what it paid.</summary>
    public sealed class OlympiadResultNetMsg
    {
        public int MatchId { get; set; }
        /// <summary>0 tie, 1/2 player one/two won, 3/4 disconnect, 5 both, 6/7 default, 8 both defaulted.</summary>
        public int Outcome { get; set; }
        /// <summary>0 when there is no single winner.</summary>
        public int WinnerObjectId { get; set; }
        public int PointsOne { get; set; }
        public int PointsTwo { get; set; }
        public int DeltaOne { get; set; }
        public int DeltaTwo { get; set; }
        public int RewardItemId { get; set; }
        public int RewardCount { get; set; }
        public int CompetitionsDoneOne { get; set; }
        public int CompetitionsDoneTwo { get; set; }
    }
}
