namespace Fineage.Net.Contract
{
    public sealed class DiceNetMsg
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Number { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    public sealed class ShowMiniMapNetMsg
    {
        public int MapId { get; set; }
        public int SevenSignsPeriod { get; set; }
    }

    public sealed class ShowCalculatorNetMsg
    {
        public int ItemId { get; set; }
    }

    public sealed class ShowXMasSealNetMsg
    {
        public int ItemId { get; set; }
    }
}
