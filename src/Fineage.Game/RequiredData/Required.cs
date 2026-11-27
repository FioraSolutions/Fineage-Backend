namespace Fineage.Game.RequiredData;

/// <summary>
/// Shared refuse path for missing required game data. Call sites must not substitute defaults.
/// </summary>
public static class Required
{
    static RequiredDataRegistry? _registry;

    /// <summary>
    /// Optional hook for <c>fineage.data.silent_fallback</c>. Zone wires this to OpenTelemetry;
    /// unit tests can capture increments. Must stay at zero in production.
    /// </summary>
    public static Action<RequiredDataKind, string>? SilentFallbackObserver { get; set; }

    public static RequiredDataRegistry Registry =>
        _registry ??= RequiredDataRegistry.LoadDefault();

    /// <summary>Replace the loaded registry (tests). Pass null to reload from disk on next use.</summary>
    public static void UseRegistry(RequiredDataRegistry? registry) => _registry = registry;

    public static RequiredDataException Missing(RequiredDataKind kind, string key, string detail)
    {
        var entry = Registry.Get(kind);
        return new RequiredDataException(kind, key, entry.File, detail);
    }

    public static RequiredDataException Missing(RequiredDataKind kind, int key, string detail) =>
        Missing(kind, key.ToString(), detail);

    /// <summary>
    /// Returns <paramref name="value"/> or throws naming the missing key. Prefer this over
    /// <c>?? throw</c> so every refuse goes through the registry file name.
    /// </summary>
    public static T RequireValue<T>(RequiredDataKind kind, string key, T? value, string detail)
        where T : class
    {
        if (value is not null)
            return value;
        throw Missing(kind, key, detail);
    }

    public static T RequireValue<T>(RequiredDataKind kind, int key, T? value, string detail)
        where T : class =>
        RequireValue(kind, key.ToString(), value, detail);

    /// <summary>
    /// Record a remaining silent-fallback site during migration. Logs via observer; does not throw.
    /// New code must not call this — use <see cref="Missing"/> / <see cref="RequireValue{T}"/> instead.
    /// </summary>
    public static void NoteSilentFallback(RequiredDataKind kind, string key)
    {
        SilentFallbackObserver?.Invoke(kind, key);
    }
}
