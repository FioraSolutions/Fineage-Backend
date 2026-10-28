namespace Fineage.Protocol;

/// <summary>
/// Zone inventory requests. Opcodes sit after <see cref="MsgType.PickupRequest"/> (14)
/// and before lobby <see cref="MsgType.Register"/> (20). They are not login opcodes.
/// MessagePack keys are appended only.
///
/// <para>Client → server</para>
/// <list type="bullet">
/// <item><description>15 <see cref="MsgType.UseItem"/> — 0 objectId (inventory instance, not a template id).
/// Equippable and in the bag: equip. Already worn: unequip.</description></item>
/// <item><description>16 <see cref="MsgType.UnequipItem"/> — 0 paperdollSlot (index 0 Under … 16 Hair2, not a body-slot name).</description></item>
/// <item><description>17 <see cref="MsgType.GiveItem"/> — 0 itemId, 1 count (default 1). Admin/dev only.
/// The zone process accepts it when <c>FINEAGE_ZONE_ADMIN</c> is <c>1</c>, <c>true</c>, or <c>yes</c>. Otherwise <see cref="Forbidden"/>.</description></item>
/// </list>
///
/// <para>Success replies</para>
/// <list type="bullet">
/// <item><description><see cref="UseItem"/> and <see cref="UnequipItem"/>: one <see cref="MsgType.InventoryUpdate"/> (1015)
/// whose rows are <see cref="InventoryChange.Modify"/> (1) for every item whose paperdoll slot changed, ordered by object id,
/// then <see cref="MsgType.UserInfo"/> (1016) to the owner. Each known player gets <see cref="MsgType.CharInfo"/> (1005) with key 6.</description></item>
/// <item><description><see cref="GiveItem"/>: one <see cref="MsgType.InventoryUpdate"/> add row (<see cref="InventoryChange.Add"/>). No paperdoll broadcast.</description></item>
/// </list>
///
/// <para>Paperdoll index order matches <c>PaperdollSlot</c>: 0 Under, 1 Lear, 2 Rear, 3 Neck, 4 LFinger, 5 RFinger,
/// 6 Head, 7 RHand, 8 LHand, 9 Gloves, 10 Chest, 11 Legs, 12 Feet, 13 Cloak, 14 Face, 15 Hair, 16 Hair2.
/// Equipped slot <c>-1</c> means the bag. A replaced item is modified back to <c>-1</c>.</para>
///
/// <para>Body slot to paperdoll (Interlude rules, not a copy of Mobius sources):</para>
/// <list type="bullet">
/// <item><description><c>rhand</c> → 7. <c>lhand</c> → 8. <c>lrhand</c> → 7 and clears 8.
/// Equipping <c>lhand</c> clears a two-handed weapon, except arrows stay with a bow and lures stay with a fishing rod.</description></item>
/// <item><description><c>chest</c> → 10. <c>fullarmor</c> → 10 and clears 11. <c>legs</c> → 11, rejected while full armor is worn.</description></item>
/// <item><description><c>both_ears</c> fills 1 then 2, then replaces 1. <c>both_fingers</c> fills 4 then 5, then replaces 4.</description></item>
/// <item><description><c>underwear</c> → 0, <c>head</c> → 6, <c>gloves</c> → 9, <c>feet</c> → 12, <c>neck</c> → 3.</description></item>
/// </list>
///
/// <para>Error codes (snake_case) on <see cref="MsgType.Error"/> (1099), keys 0 code, 1 message:</para>
/// <list type="bullet">
/// <item><description><see cref="NotOwned"/> — object id is missing or belongs to someone else.</description></item>
/// <item><description><see cref="NotEquippable"/> — the template has no body slot (adena, quest items).</description></item>
/// <item><description><see cref="Dead"/> — <c>ZoneEntity.Dead</c> is set. Spawn is alive. Low <c>CurHp</c> is not death; regen heals it.</description></item>
/// <item><description><see cref="Blocked"/> — legs over full armor, or formal wear rejects the slot.</description></item>
/// <item><description><see cref="BadSlot"/> — paperdoll index outside 0..16.</description></item>
/// <item><description><see cref="Empty"/> — unequip of an empty paperdoll index.</description></item>
/// <item><description><see cref="Forbidden"/> — <see cref="MsgType.GiveItem"/> while admin commands are off.</description></item>
/// <item><description><c>world</c> — <c>EnterWorld</c> has not completed. <c>bad_request</c> — body or count or unknown template.</description></item>
/// </list>
///
/// <para>Class and grade restrictions are not applied. Gear is written to the account store on each successful change and again on leave.</para>
/// </summary>
public static class InventoryProtocol
{
    public const string NotOwned = "not_owned";
    public const string NotEquippable = "not_equippable";
    public const string Dead = "dead";
    public const string Blocked = "blocked";
    public const string BadSlot = "bad_slot";
    public const string Empty = "empty";
    public const string Forbidden = "forbidden";
}
