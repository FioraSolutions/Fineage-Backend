using Fineage.Net.Contract;

namespace Fineage.LoadTest.Bot;

public sealed class BotBagTracker
{
    public const int AdenaItemId = 57;

    readonly Dictionary<int, (int ItemId, int Count)> _byObjectId = new();
    long _adena;

    public long Adena => _adena;
    public IReadOnlyDictionary<int, (int ItemId, int Count)> Items => _byObjectId;

    public void ApplyItemList(ItemListNetMsg list)
    {
        _byObjectId.Clear();
        _adena = 0;
        foreach (var row in list.Items ?? [])
        {
            if (row.Count <= 0)
                continue;
            _byObjectId[row.ObjectId] = (row.ItemId, row.Count);
            if (row.ItemId == AdenaItemId)
                _adena = row.Count;
        }
    }

    public void ApplyInventoryUpdate(InventoryUpdateNetMsg update)
    {
        foreach (var change in update.Changes ?? [])
        {
            if (change.Change == 3)
            {
                _byObjectId.Remove(change.ObjectId);
                if (change.ItemId == AdenaItemId)
                    _adena = Math.Max(0, _adena - change.Count);
                continue;
            }

            _byObjectId[change.ObjectId] = (change.ItemId, change.Count);
            if (change.ItemId == AdenaItemId)
                _adena = change.Count;
        }
    }

    public int? FirstTradableObjectId()
    {
        foreach (var (objectId, (itemId, count)) in _byObjectId)
        {
            if (itemId == AdenaItemId || count < 1)
                continue;
            return objectId;
        }

        return null;
    }
}