namespace Fineage.Zone.Inventory;

/// <summary>
/// Pure private-warehouse window gates before fee/slot mutation.
/// Soft refuses (notice + no error code) stay in World; this covers hard rejects only.
/// </summary>
public static class WarehouseSessionRules
{
    public static string? NpcWindowError(bool isWarehouseNpc) =>
        isWarehouseNpc ? null : "bad_request";

    public static string? LineDuplicateError(bool objectIdAlreadySeen) =>
        objectIdAlreadySeen ? "bad_request" : null;

    public static string? NonStackableCountError(bool stackable, int count) =>
        !stackable && count != 1 ? "bad_request" : null;
}
