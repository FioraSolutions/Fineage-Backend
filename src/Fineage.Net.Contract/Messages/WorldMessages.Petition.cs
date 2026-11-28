namespace Fineage.Net.Contract
{
    public sealed class PetitionRequestNetMsg
    {
        public string Content { get; set; } = "";
        public int Type { get; set; }
    }

    public sealed class PetitionCancelNetMsg
    {
    }
}
