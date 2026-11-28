using Npgsql;

namespace Fineage.Agent.Identity;

/// <summary>Resolves character ownership from the game <c>characters</c> table.</summary>
public sealed class PostgresGameCharacterOwnership : IGameCharacterOwnership
{
    readonly NpgsqlDataSource _dataSource;

    public PostgresGameCharacterOwnership(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public bool TryGetOwnerAccount(CharacterId characterId, out string gameAccount)
    {
        gameAccount = "";
        if (!int.TryParse(characterId.Value, out int objectId))
            return false;

        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT account FROM characters WHERE id = @id";
        cmd.Parameters.AddWithValue("id", objectId);
        object? value = cmd.ExecuteScalar();
        if (value is not string account || string.IsNullOrWhiteSpace(account))
            return false;
        gameAccount = account;
        return true;
    }
}
