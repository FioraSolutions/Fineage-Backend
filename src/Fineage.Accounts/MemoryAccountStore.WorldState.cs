namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    public IReadOnlyList<WorldStateDocument> LoadWorldState()
    {
        using (EnterStore())
            return _data.WorldState.OrderBy(d => d.System, StringComparer.Ordinal).ToList();
    }

    public WorldStateDocument? LoadWorldState(string system)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        using (EnterStore())
            return _data.WorldState.FirstOrDefault(d => d.System == system);
    }

    public bool TryImportWorldState(WorldStateDocument document, WorldStateImport import)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(import);
        document.Validate();
        if (import.System != document.System)
            throw new ArgumentException($"import record names '{import.System}' but the document is '{document.System}'", nameof(import));
        using (EnterStore())
        {
            if (_data.WorldState.Any(d => d.System == document.System))
                return false;
            _data.WorldState.Add(document);
            _data.WorldStateImports.RemoveAll(i => i.System == import.System);
            _data.WorldStateImports.Add(import);
            Save();
            return true;
        }
    }

    public IReadOnlyList<WorldStateImport> ListWorldStateImports()
    {
        using (EnterStore())
            return _data.WorldStateImports.OrderBy(i => i.System, StringComparer.Ordinal).ToList();
    }

    /// <summary>Caller holds the store lock. A document older than the saved one is dropped, as in Postgres.</summary>
    void ApplyWorldStateLocked(WorldStateDocument document)
    {
        int at = _data.WorldState.FindIndex(d => d.System == document.System);
        if (at < 0)
            _data.WorldState.Add(document);
        else if (_data.WorldState[at].Revision < document.Revision)
            _data.WorldState[at] = document;
    }
}
