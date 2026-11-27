using System.Text.Json;
using Fineage.Accounts;
using Fineage.Game.Persistence;

namespace Fineage.Zone.Persistence;

/// <summary>A saved world subsystem in the database is unusable. The message names the system and where it lives.</summary>
public sealed class WorldStateException : InvalidOperationException
{
    public WorldStateException(string system, string detail, Exception? inner = null)
        : base($"World state '{system}' (database world_state system='{system}') is unusable: {detail}", inner)
    {
        System = system;
    }

    public string System { get; }
}

/// <summary>
/// The world subsystems' saved state, in the account database (#996). Loading reads the documents once at start.
/// A save never does I/O: it serializes the snapshot on the caller (the thread that owns the subsystem) and leaves
/// it dirty, newest per system. The economy commit takes the dirty documents with the bags and the ledger rows and
/// writes them in one transaction (<see cref="IAccountStore.CommitEconomy"/>), so a lottery ticket and the adena
/// that paid for it land together or not at all, and the simulation thread never waits on a file or a socket.
/// </summary>
public sealed class WorldStateStore
{
    readonly object _gate = new();
    readonly Dictionary<string, WorldStateDocument> _saved = new(StringComparer.Ordinal);
    readonly Dictionary<string, WorldStateDocument> _dirty = new(StringComparer.Ordinal);
    long _revision;

    WorldStateStore(IReadOnlyList<WorldStateDocument> saved)
    {
        foreach (var document in saved)
        {
            _saved[document.System] = document;
            _revision = Math.Max(_revision, document.Revision);
        }
    }

    /// <summary>Reads every saved document from <paramref name="accounts"/>.</summary>
    public static WorldStateStore Open(IAccountStore accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return new WorldStateStore(accounts.LoadWorldState());
    }

    /// <summary>Tests and tools: starts from these documents (none for a fresh world).</summary>
    public static WorldStateStore FromDocuments(IReadOnlyList<WorldStateDocument>? saved = null) => new(saved ?? []);

    /// <summary>
    /// The saved snapshot of <paramref name="system"/>, or <paramref name="createFresh"/> when it was never saved (a
    /// new world; <see cref="StateFilePolicy.RequireExistingEnvVar"/> makes that an error). A document whose schema
    /// version this build does not read, or whose payload does not parse, throws <see cref="WorldStateException"/>:
    /// there is no fallback to an empty state.
    /// </summary>
    public T Load<T>(string system, int schemaVersion, JsonSerializerOptions options, Func<T> createFresh, bool? requireExisting = null)
        where T : class, IVersionedStateSnapshot
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        WorldStateDocument? document;
        lock (_gate)
        {
            if (!_dirty.TryGetValue(system, out document))
                _saved.TryGetValue(system, out document);
        }

        if (document == null)
        {
            if (requireExisting ?? StateFilePolicy.RequireExisting)
                throw new WorldStateException(system, $"no saved state and {StateFilePolicy.RequireExistingEnvVar} is set");
            return createFresh();
        }

        return Parse<T>(document, schemaVersion, options);
    }

    /// <summary>Parses one document strictly: version in the row and in the payload must both be <paramref name="schemaVersion"/>.</summary>
    public static T Parse<T>(WorldStateDocument document, int schemaVersion, JsonSerializerOptions options)
        where T : class, IVersionedStateSnapshot
    {
        if (document.SchemaVersion != schemaVersion)
            throw new WorldStateException(document.System, $"schema version {document.SchemaVersion}; this build reads {schemaVersion}");
        T? data;
        try
        {
            data = JsonSerializer.Deserialize<T>(document.Json, options);
        }
        catch (JsonException ex)
        {
            throw new WorldStateException(document.System, $"invalid JSON: {ex.Message}", ex);
        }

        if (data == null)
            throw new WorldStateException(document.System, "payload is null");
        if (data.SchemaVersion != schemaVersion)
            throw new WorldStateException(document.System, $"payload schemaVersion {data.SchemaVersion} differs from the row's {schemaVersion}");
        return data;
    }

    /// <summary>Serializes <paramref name="snapshot"/> now and leaves it dirty for the next economy commit. No I/O.</summary>
    public void Save<T>(string system, T snapshot, JsonSerializerOptions options)
        where T : class, IVersionedStateSnapshot
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!WorldStateSystems.All.Contains(system, StringComparer.Ordinal))
            throw new InvalidOperationException($"world state system '{system}' is not in WorldStateSystems.All");
        if (snapshot.SchemaVersion <= 0)
            throw new WorldStateException(system, $"snapshot schemaVersion {snapshot.SchemaVersion} is not positive");
        string json = JsonSerializer.Serialize(snapshot, options);
        lock (_gate)
            _dirty[system] = new WorldStateDocument(system, snapshot.SchemaVersion, ++_revision, json);
    }

    public bool HasDirty
    {
        get
        {
            lock (_gate)
                return _dirty.Count > 0;
        }
    }

    /// <summary>Takes every dirty document for a commit. A failed commit hands them back (<see cref="Restore"/>).</summary>
    public List<WorldStateDocument> Steal()
    {
        lock (_gate)
        {
            if (_dirty.Count == 0)
                return [];
            var stolen = _dirty.Values.OrderBy(d => d.Revision).ToList();
            _dirty.Clear();
            return stolen;
        }
    }

    /// <summary>A failed commit: its documents are dirty again unless a newer save of the same system came since.</summary>
    public void Restore(IReadOnlyList<WorldStateDocument> stolen)
    {
        lock (_gate)
        {
            foreach (var document in stolen)
            {
                if (!_dirty.TryGetValue(document.System, out var newer) || newer.Revision < document.Revision)
                    _dirty[document.System] = document;
            }
        }
    }

    /// <summary>A commit landed: these documents are now what the database holds.</summary>
    public void Committed(IReadOnlyList<WorldStateDocument> written)
    {
        lock (_gate)
        {
            foreach (var document in written)
            {
                if (!_saved.TryGetValue(document.System, out var saved) || saved.Revision < document.Revision)
                    _saved[document.System] = document;
            }
        }
    }

    /// <summary>The newest state of every system: dirty if a save is pending, else what the database holds.</summary>
    public IReadOnlyList<WorldStateDocument> Current()
    {
        lock (_gate)
        {
            var all = new Dictionary<string, WorldStateDocument>(_saved, StringComparer.Ordinal);
            foreach (var (system, document) in _dirty)
                all[system] = document;
            return all.Values.OrderBy(d => d.System, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Whether this system has a save that is not durable yet.</summary>
    public bool IsDirty(string system)
    {
        lock (_gate)
            return _dirty.ContainsKey(system);
    }
}

/// <summary>
/// The adapter each subsystem store uses: one system id, one schema version, the JSON options its legacy sidecar was
/// written with (the cutover import reads those files with the same options).
/// </summary>
public sealed class WorldStateSlot<T>
    where T : class, IVersionedStateSnapshot
{
    readonly WorldStateStore _store;

    public WorldStateSlot(WorldStateStore store, string system, int schemaVersion, JsonSerializerOptions options, Func<T> createFresh, Action<T>? normalize = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        System = system;
        SchemaVersion = schemaVersion;
        Options = options;
        CreateFresh = createFresh;
        Normalize = normalize;
    }

    public string System { get; }

    public int SchemaVersion { get; }

    public JsonSerializerOptions Options { get; }

    public Func<T> CreateFresh { get; }

    public Action<T>? Normalize { get; }

    public T Load()
    {
        var data = _store.Load(System, SchemaVersion, Options, CreateFresh);
        Normalize?.Invoke(data);
        return data;
    }

    public void Save(T snapshot) => _store.Save(System, snapshot, Options);
}
