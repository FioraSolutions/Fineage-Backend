using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    public bool TryHardDelete(string account, int characterId)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            var character = row?.Characters.FirstOrDefault(c => c.Id == characterId);
            if (row == null || character == null || character.ClanId != 0)
                return false;
            if (_data.Clans.Any(c => c.Members.Any(m => m.CharacterId == characterId)))
                return false;
            row.Characters.Remove(character);
            Save();
            return true;
        }
    }

    public CharacterRecord? FindById(string account, int characterId)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            var character = row?.Characters.FirstOrDefault(c => c.Id == characterId);
            return character == null || row == null ? null : ToRecord(row.Name, character);
        }
    }

    public CharacterRecord? FindByName(string account, string name)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return null;
            var character = row.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return character == null ? null : ToRecord(row.Name, character);
        }
    }

    public IReadOnlyList<CharacterRecord> List(string account) => List(account, DateTimeOffset.UtcNow);

    public IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return [];
            if (PurgeUnlocked(row, now) > 0)
                Save();
            return Ordered(row);
        }
    }

    CharacterDto? FindCharacter(string account, int characterId) =>
        FindAccount(account)?.Characters.FirstOrDefault(c => c.Id == characterId);

    static int PurgeUnlocked(AccountDto row, DateTimeOffset now) =>
        row.Characters.RemoveAll(c => c.ClanId == 0 && c.DeleteAt is { } at && at < now);

    static List<CharacterRecord> Ordered(AccountDto row) =>
        row.Characters
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => ToRecord(row.Name, c))
            .ToList();

    AccountDto? FindAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return null;
        string key = account.Trim();
        return _data.Accounts.FirstOrDefault(a =>
            string.Equals(a.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    AccountDto? FindByEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        string key = email.Trim();
        return _data.Accounts.FirstOrDefault(a =>
            !string.IsNullOrEmpty(a.Email) &&
            string.Equals(a.Email, key, StringComparison.OrdinalIgnoreCase));
    }

    bool NameTaken(string name) =>
        _data.Accounts.Any(a => a.Characters.Any(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

    ClanDto? FindClanDto(string name) =>
        _data.Clans.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
