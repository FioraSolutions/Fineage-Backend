using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPackageToList(IReadOnlyList<int> watcherObjectIds, PackageToListMsg list)
    {
        var rows = list.Characters ?? [];
        var payload = NetCodec.EncodePackageToList(new PackageToListNetMsg
        {
            Characters = rows.Select(c => new PackageToListEntryNet { ObjectId = c.ObjectId, Name = c.Name ?? "" }).ToArray()
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PackageToList, payload);
    }

    void IWorldEventPublisher.FanoutPackageSendableList(IReadOnlyList<int> watcherObjectIds, PackageSendableListMsg list)
    {
        var payload = NetCodec.EncodePackageSendableList(new PackageSendableListNetMsg
        {
            RecipientObjectId = list.RecipientObjectId,
            Adena = list.Adena,
            Items = ToNetWarehouseItemRows(list.Items)
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PackageSendableList, payload);
    }
}