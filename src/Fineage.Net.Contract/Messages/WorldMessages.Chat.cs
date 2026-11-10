namespace Fineage.Net.Contract
{
    public sealed class SayNetMsg
    {
        public string Text { get; set; } = "";
        public int ChatType { get; set; }
    }

    public sealed class CreatureSayNetMsg
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public int ChatType { get; set; }
    }

    public sealed class SystemMessageNetMsg
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public string[] Params { get; set; } = System.Array.Empty<string>();
    }

    public sealed class TeleportToLocationNetMsg
    {
        public int ObjectId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
    }

    public sealed class NpcTalkNetMsg
    {
        public int NpcObjectId { get; set; }
    }

    public sealed class BypassRequestNetMsg
    {
        public int NpcObjectId { get; set; }
        public string Command { get; set; } = "";
    }

    public sealed class NpcHtmlNetMsg
    {
        public int NpcObjectId { get; set; }
        public int NpcId { get; set; }
        public string Html { get; set; } = "";
        public int ItemId { get; set; }
    }
}
