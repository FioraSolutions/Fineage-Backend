namespace Fineage.Net.Contract
{
    public sealed class ShowBoardRequestNetMsg
    {
        public int Unused { get; set; }
    }

    public sealed class BBSwriteNetMsg
    {
        public string Url { get; set; } = "";
        public string Arg1 { get; set; } = "";
        public string Arg2 { get; set; } = "";
        public string Arg3 { get; set; } = "";
        public string Arg4 { get; set; } = "";
        public string Arg5 { get; set; } = "";
    }

    public sealed class ShowBoardNetMsg
    {
        public byte Show { get; set; } = 1;
        public string Content { get; set; } = "";
    }
}
