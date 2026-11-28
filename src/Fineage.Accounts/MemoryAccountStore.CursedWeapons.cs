namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    public IReadOnlyList<CursedWeaponRecord> ListCursedWeapons()
    {
        using (EnterStore())
            return _data.CursedWeapons.Select(CursedWeaponRecordClone).ToList();
    }

    public bool SaveCursedWeapons(IReadOnlyList<CursedWeaponRecord> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        using (EnterStore())
        {
            _data.CursedWeapons = rows.Select(CursedWeaponRecordClone).ToList();
            return true;
        }
    }

    static CursedWeaponRecord CursedWeaponRecordClone(CursedWeaponRecord row) => new()
    {
        ItemId = row.ItemId,
        CharacterId = row.CharacterId,
        PlayerReputation = row.PlayerReputation,
        PlayerPkKills = row.PlayerPkKills,
        NbKills = row.NbKills,
        EndTimeUnixMs = row.EndTimeUnixMs,
        Activated = row.Activated,
        Dropped = row.Dropped,
        GroundObjectId = row.GroundObjectId
    };
}