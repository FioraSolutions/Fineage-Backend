using Fineage.Agent.Contracts;

namespace Fineage.Agent.Identity;

/// <summary>Looks up which game account owns a character. Used only at bind time.</summary>
public interface IGameCharacterOwnership
{
    bool TryGetOwnerAccount(CharacterId characterId, out string gameAccount);
}

/// <summary>In-memory directory for tests and hosts that already resolved ownership.</summary>
public sealed class MemoryGameCharacterOwnership : IGameCharacterOwnership
{
    readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

    public void Add(CharacterId characterId, string gameAccount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameAccount);
        _owners[characterId.Value] = gameAccount.Trim();
    }

    public bool TryGetOwnerAccount(CharacterId characterId, out string gameAccount) =>
        _owners.TryGetValue(characterId.Value, out gameAccount!);
}
