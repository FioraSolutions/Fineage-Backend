namespace Fineage.Protocol;

/// <summary>
/// One Mobius shortcut. <see cref="Slot"/> is the client world slot (slot + page * 12).
/// <see cref="Type"/>: 1 item (id = item object id), 2 skill (id = skill id), 3 action, 4 macro, 5 recipe.
/// </summary>
public sealed class ShortCutMsg
{
    public int Type { get; set; }
    public int Slot { get; set; }
    public int Id { get; set; }
    public int Level { get; set; }
}

/// <summary>Full hotbar snapshot sent on enter world (Mobius ShortCutInit).</summary>
public sealed class ShortCutInitMsg
{
    public ShortCutMsg[] Shortcuts { get; set; } = [];
}
