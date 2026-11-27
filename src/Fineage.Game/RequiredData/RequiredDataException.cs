namespace Fineage.Game.RequiredData;

/// <summary>
/// Missing required game data. Message always names the kind, key, and catalog file —
/// never a silent default.
/// </summary>
public sealed class RequiredDataException : InvalidOperationException
{
    public RequiredDataException(RequiredDataKind kind, string key, string catalogFile, string detail)
        : base(Format(kind, key, catalogFile, detail))
    {
        Kind = kind;
        Key = key;
        CatalogFile = catalogFile;
    }

    public RequiredDataKind Kind { get; }
    public string Key { get; }
    public string CatalogFile { get; }

    public static string Format(RequiredDataKind kind, string key, string catalogFile, string detail) =>
        $"required-data {kind.ToId()} key={key} file={catalogFile}: {detail}";
}
