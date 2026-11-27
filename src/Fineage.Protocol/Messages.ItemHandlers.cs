namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.Dice"/> — Mobius <c>Dice</c> broadcast after RollingDice.</summary>
public sealed class DiceMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Number { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ShowMiniMap"/> — Mobius <c>ShowMiniMap</c>.</summary>
public sealed class ShowMiniMapMsg
{
    public int MapId { get; set; }
    /// <summary>Seven Signs current period (Mobius writes a byte after the map id).</summary>
    public int SevenSignsPeriod { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ShowCalculator"/> — Mobius <c>ShowCalculator</c>.</summary>
public sealed class ShowCalculatorMsg
{
    public int ItemId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ShowXMasSeal"/> — Mobius <c>ShowXMasSeal</c>.</summary>
public sealed class ShowXMasSealMsg
{
    public int ItemId { get; set; }
}

/// <summary>Constants for Book / Maps / Dice / Calculator / SpecialXMas (#925).</summary>
public static class ItemHandlerProtocol
{
    public const int DiceRolled = 834;
    public const int DiceNotReady = 835;
    public const int OlympiadRefuse = 1508;

    public const string DiceRolledText = "{0} has rolled a {1}.";
    public const string DiceNotReadyText = "You may not throw the dice at this time. Try again later.";
    public const string OlympiadRefuseText = "You cannot use that item in a Grand Olympiad match.";

    /// <summary>Mobius flood protector interval for dice (~7 s on CT0).</summary>
    public const int DiceCooldownMs = 7000;

    public const string BookMissingHtml =
        "<html><body>My Text is missing:<br>data/html/help/{0}.htm</body></html>";
}
