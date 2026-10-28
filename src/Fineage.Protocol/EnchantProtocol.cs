namespace Fineage.Protocol;

/// <summary>
/// Zone enchant. Request, confirm, and cancel sit at 39–41, after clan 32–38 and spoil 30–31.
/// The result is 1060, after the clan band 1050–1059 and the chat band 1040–1049.
/// 1019 and 1034–1039 stay free. This is not an Interlude opcode.
/// </summary>
/// <remarks>
/// Wire layout for the client enchant window:
/// <list type="bullet">
/// <item><description>39 <see cref="MsgType.EnchantRequest"/> — key 0 scroll object id. Opens the window.
/// Reply is <see cref="MsgType.SystemMessage"/> <see cref="SelectItem"/>.</description></item>
/// <item><description>40 <see cref="MsgType.EnchantConfirm"/> — key 0 target object id. The scroll is the one selected by the request.</description></item>
/// <item><description>41 <see cref="MsgType.EnchantCancel"/> — empty map. Closes the window.</description></item>
/// <item><description>1060 <see cref="MsgType.EnchantResult"/> — the finished roll, or cancel.</description></item>
/// </list>
/// Rejections before a roll are <see cref="MsgType.Error"/> (1099), snake_case <c>code</c>, and the request is cleared.
/// A finished roll consumes one scroll and is not an Error.
/// </remarks>
public static class EnchantProtocol
{
    public const int ResultCancel = 0;
    public const int ResultSuccess = 1;
    public const int ResultKeep = 2;
    public const int ResultReset = 3;
    public const int ResultCrystallize = 4;

    public const string NotScroll = "not_scroll";
    public const string NoRequest = "no_request";
    public const string NotEnchantable = "not_enchantable";
    public const string GradeMismatch = "grade_mismatch";
    public const string SlotMismatch = "slot_mismatch";
    public const string MaxEnchant = "max_enchant";

    /// <summary>Select a target. Params are the scroll object id and the scroll item id.</summary>
    public const int SelectItem = 10;

    /// <summary>The player closed the window.</summary>
    public const int Cancelled = 11;

    /// <summary>Enchant rose from +0. Params[0] is the item name. The line does not include a number.</summary>
    public const int SuccessPlain = 12;

    /// <summary>
    /// Enchant rose from +1 or higher. Params are the enchant before the increase, then the item name.
    /// The result packet carries the level after the increase.
    /// </summary>
    public const int SuccessLevel = 13;

    /// <summary>Crystal scroll failed. The enchant is unchanged.</summary>
    public const int Kept = 14;

    /// <summary>Blessed scroll failed. The enchant is now 0.</summary>
    public const int Reset = 15;

    /// <summary>An enchanted item was removed before a blessed or normal failure. Params are the old level and the name.</summary>
    public const int EquipmentRemoved = 16;

    /// <summary>A +0 item was removed before a normal failure. Params[0] is the name.</summary>
    public const int Disarmed = 17;

    /// <summary>Normal failure of an enchanted item. Params are the old level and the name.</summary>
    public const int CrystallizedLevel = 18;

    /// <summary>Normal failure of a +0 item. Params[0] is the name.</summary>
    public const int CrystallizedPlain = 19;

    /// <summary>Crystals gained from a normal failure. Params are the count and the crystal name.</summary>
    public const int Earned = 20;
}
