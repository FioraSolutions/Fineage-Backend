namespace Fineage.Net.Contract
{
    public sealed class MakeMacroNetMsg
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Descr { get; set; } = "";
        public string Acronym { get; set; } = "";
        public int Icon { get; set; }
        public MacroCmdNet[] Commands { get; set; } = System.Array.Empty<MacroCmdNet>();
    }

    public sealed class MacroCmdNet
    {
        public int Entry { get; set; }
        public int Type { get; set; }
        public int D1 { get; set; }
        public int D2 { get; set; }
        public string Cmd { get; set; } = "";
    }

    public sealed class DeleteMacroNetMsg
    {
        public int Id { get; set; }
    }

    public sealed class MacroListNetMsg
    {
        public int Revision { get; set; }
        public int Count { get; set; }
        public bool HasMacro { get; set; }
        public int MacroId { get; set; }
        public string Name { get; set; } = "";
        public string Descr { get; set; } = "";
        public string Acronym { get; set; } = "";
        public int Icon { get; set; }
        public MacroCmdNet[] Commands { get; set; } = System.Array.Empty<MacroCmdNet>();
    }
}
