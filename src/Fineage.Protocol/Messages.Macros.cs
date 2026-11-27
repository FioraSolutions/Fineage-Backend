namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.RequestMakeMacro"/> — Mobius <c>RequestMakeMacro</c>.</summary>
public sealed class MakeMacroMsg
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Descr { get; set; } = "";
    public string Acronym { get; set; } = "";
    public int Icon { get; set; }
    public MacroCmdMsg[] Commands { get; set; } = [];
}

public sealed class MacroCmdMsg
{
    public int Entry { get; set; }
    public int Type { get; set; }
    public int D1 { get; set; }
    public int D2 { get; set; }
    public string Cmd { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.RequestDeleteMacro"/> — Mobius <c>RequestDeleteMacro</c>.</summary>
public sealed class DeleteMacroMsg
{
    public int Id { get; set; }
}

/// <summary>Opcode <see cref="MsgType.MacroList"/> — one Mobius <c>SendMacroList</c> packet.</summary>
public sealed class MacroListMsg
{
    public int Revision { get; set; }
    public int Count { get; set; }
    /// <summary>Null when the list is empty (Mobius sends a single empty packet).</summary>
    public MacroEntryMsg? Macro { get; set; }
}

public sealed class MacroEntryMsg
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Descr { get; set; } = "";
    public string Acronym { get; set; } = "";
    public int Icon { get; set; }
    public MacroCmdMsg[] Commands { get; set; } = [];
}

/// <summary>Constants for macro make/delete (#927).</summary>
public static class MacroProtocol
{
    public const int MaxMacros = 48;
    public const int MaxCommands = 12;
    public const int MaxCommandsLength = 255;
    public const int MaxDescrLength = 32;

    public const int LimitReached = 797;
    public const int InvalidMacro = 810;
    public const int DescrTooLong = 837;
    public const int EmptyName = 838;

    public const string LimitReachedText = "You may create up to 48 macros.";
    public const string InvalidMacroText = "Invalid macro. Refer to the Help file for instructions.";
    public const string DescrTooLongText = "Macro descriptions may contain up to 32 characters.";
    public const string EmptyNameText = "Enter the name of the macro.";
}
