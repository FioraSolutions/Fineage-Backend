namespace Fineage.Protocol;

/// <summary>
/// Catacomb and necropolis gates. Opcodes are the next free ids after Seven Signs
/// <see cref="SevenSignsProtocol.ActionRequestOpcode"/> (91) and
/// <see cref="SevenSignsProtocol.SkyOpcode"/> (1091).
/// Layout: <c>docs/dungeon-access-v2-protocol.md</c>.
/// </summary>
public static class DungeonProtocol
{
    public const ushort EnterRequestOpcode = 92;
    public const ushort EnteredOpcode = 1092;

    public const int ActionEnter = 1;
    public const int ActionExit = 2;
    public const int ActionKick = 3;

    /// <summary>Interlude adena.</summary>
    public const int AdenaItemId = 57;

    /// <summary>Interlude Ancient Adena. The Ziggurat gate does not charge it; priest menus do.</summary>
    public const int AncientAdenaItemId = 5575;

    /// <summary>Retail <c>SystemMessageId.ONLY_A_LORD_OF_DAWN_MAY_USE_THIS</c>.</summary>
    public const int DawnOnlyMessage = 1301;

    /// <summary>Retail <c>SystemMessageId.ONLY_A_REVOLUTIONARY_OF_DUSK_MAY_USE_THIS</c>.</summary>
    public const int DuskOnlyMessage = 1302;

    /// <summary>Retail <c>SystemMessageId.YOU_DO_NOT_HAVE_ENOUGH_ADENA</c>.</summary>
    public const int NotEnoughAdenaMessage = 279;

    /// <summary>
    /// Retail <c>SystemMessageId</c> 701, "You do not have enough required items."
    /// Ancient Adena keeps that id and substitutes the Mobius plain-text fee line.
    /// </summary>
    public const int NotEnoughItemsMessage = 701;

    /// <summary>
    /// No Interlude system-message id. Ziggurat <c>necro_no.htm</c> / <c>cata_no.htm</c>.
    /// Sits after the Seven Signs lines 120–136.
    /// </summary>
    public const int GateRefusedMessage = 137;

    /// <summary>Plain text from <c>SevenSigns.teleLosingCabalFromDungeons</c> when results or seal validation begins.</summary>
    public const int KickedValidationMessage = 138;

    /// <summary>Plain text from the same method when the new period is recruitment or competition.</summary>
    public const int KickedUnsignedMessage = 139;

    public const string DawnOnlyText = "Only a Lord of Dawn may use this.";
    public const string DuskOnlyText = "Only a Revolutionary of Dusk may use this.";
    public const string GateRefusedText = "Did you think you could deceive me? You may not pass.";
    public const string NotEnoughAdenaText = "You do not have enough adena.";
    public const string NotEnoughAncientAdenaText = "You do not have enough Ancient Adena.";
    public const string NotEnoughItemsText = "You do not have enough required items.";
    public const string KickedValidationText = "You have been teleported to the nearest town due to the beginning of the Seal Validation period.";
    public const string KickedUnsignedText = "You have been teleported to the nearest town because you have not signed for any cabal.";

    public const string Unknown = "dungeon_unknown";
    public const string Cabal = "dungeon_cabal";
    public const string Dawn = "dungeon_dawn";
    public const string Dusk = "dungeon_dusk";
    public const string Fee = "dungeon_fee";
}

/// <summary>One SystemMessage 1040 line produced by a gate refusal or a kick.</summary>
public readonly record struct DungeonNotice(int Id, string Text);
