namespace Fineage.Net.Contract
{
    /// <summary>
    /// Client → zone: enter or leave a catacomb or necropolis.
    /// <see cref="Action"/> is <c>DungeonProtocol.ActionEnter</c> or <c>ActionExit</c>.
    /// </summary>
    public sealed class DungeonEnterRequestNetMsg
    {
        public int DungeonId { get; set; }
        public int Action { get; set; }
    }

    /// <summary>
    /// Zone → client: the catacomb or necropolis teleport result, both the answer to a request and the
    /// validation-cycle kick. <see cref="Inside"/> false means the player is outside with <see cref="Action"/> telling
    /// why (exit or kick).
    /// </summary>
    public sealed class DungeonEnteredNetMsg
    {
        public int DungeonId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool Inside { get; set; }
        public int Action { get; set; }
    }
}
