using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutShowBoard(IReadOnlyList<int> watcherObjectIds, ShowBoardMsg board)
    {
        var payload = NetCodec.EncodeShowBoard(new ShowBoardNetMsg
        {
            Show = board.Show,
            Content = board.Content ?? ""
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ShowBoard, payload);
    }
}
