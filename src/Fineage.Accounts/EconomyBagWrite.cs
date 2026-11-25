namespace Fineage.Accounts;

/// <summary>
/// One character's containers in an <see cref="IAccountStore.CommitEconomy"/> call.
/// A null list is left untouched. An empty list is an empty container.
/// </summary>
public sealed class EconomyBagWrite
{
    public EconomyBagWrite(string account, int characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        Account = account;
        CharacterId = characterId;
    }

    public string Account { get; }

    public int CharacterId { get; }

    public IReadOnlyList<StoredItem>? Inventory { get; set; }

    public IReadOnlyList<StoredItem>? Warehouse { get; set; }

    public IReadOnlyList<StoredItem>? Freight { get; set; }
}
