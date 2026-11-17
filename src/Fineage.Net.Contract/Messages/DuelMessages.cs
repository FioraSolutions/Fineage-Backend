namespace Fineage.Net.Contract
{
    /// <summary>What a <see cref="DuelCommandNetMsg"/> asks for (Interlude RequestDuelStart / RequestDuelAnswerStart / /surrender).</summary>
    public enum DuelCommandKind : byte
    {
        /// <summary>/duel or /partyduel: challenge <see cref="DuelCommandNetMsg.TargetName"/> (empty = the selected target).</summary>
        Request = 1,
        /// <summary>Answer the pending ask: <see cref="DuelCommandNetMsg.Response"/> 1 accept, 0 decline, -1 decline and refuse further asks this session.</summary>
        Answer = 2,
        /// <summary>/surrender: give up the running duel (the whole team in a party duel).</summary>
        Surrender = 3,
    }

    /// <summary>Client → zone: one duel action (<see cref="NetOpcode.DuelCommand"/>).</summary>
    public sealed class DuelCommandNetMsg
    {
        public DuelCommandKind Kind { get; set; }
        public string TargetName { get; set; } = "";
        public bool Party { get; set; }
        public int Response { get; set; }
    }

    /// <summary>What a <see cref="DuelEventNetMsg"/> reports (Mobius ExDuelAskStart / ExDuelReady / ExDuelStart / ExDuelEnd).</summary>
    public enum DuelEventKind : byte
    {
        /// <summary>Someone challenges you: <see cref="DuelEventNetMsg.Name"/> = the requester (party duel: its leader). Answer with <see cref="DuelCommandKind.Answer"/>.</summary>
        Ask = 1,
        /// <summary>Countdown is over, fighting starts now (Mobius ExDuelReady).</summary>
        Ready = 2,
        /// <summary>The duel runs (Mobius ExDuelStart); <see cref="DuelEventNetMsg.Team"/> = your team.</summary>
        Start = 3,
        /// <summary>The duel is over (Mobius ExDuelEnd); <see cref="DuelEventNetMsg.Name"/> = the winner (empty on a draw / cancel).</summary>
        End = 4,
    }

    /// <summary>Zone → client: duel state change (<see cref="NetOpcode.DuelEvent"/>).</summary>
    public sealed class DuelEventNetMsg
    {
        public DuelEventKind Kind { get; set; }
        public bool Party { get; set; }
        public string Name { get; set; } = "";
        /// <summary>0 none, 1 blue (challenger side), 2 red (the side that accepted).</summary>
        public byte Team { get; set; }
    }

    /// <summary>Zone → client: an opponent's bars during a duel (Mobius ExDuelUpdateUserInfo).</summary>
    public sealed class DuelUserInfoNetMsg
    {
        public string Name { get; set; } = "";
        public int ObjectId { get; set; }
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int Cp { get; set; }
        public int MaxCp { get; set; }
    }
}
