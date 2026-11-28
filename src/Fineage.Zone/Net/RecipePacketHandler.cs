using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Recipe / recipe-shop opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class RecipePacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public RecipePacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnBookOpenAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeRecipeBookOpen(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new RecipeBookOpenCommand(new RecipeBookOpenMsg { Dwarven = req.Dwarven != 0 }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Recipe book open failed.", ct).ConfigureAwait(false);
    }

    public async Task OnMakeSelfAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeRecipeMakeSelf(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new RecipeMakeSelfCommand(new RecipeMakeSelfMsg { RecipeId = req.RecipeId }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Recipe make self failed.", ct).ConfigureAwait(false);
    }

    public async Task OnBookDestroyAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeRecipeBookDestroy(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new RecipeBookDestroyCommand(new RecipeBookDestroyMsg { RecipeId = req.RecipeId }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Recipe book destroy failed.", ct).ConfigureAwait(false);
    }

    public async Task OnSetShopAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeSetRecipeShop(envelope.Payload);
        var items = (req.Items ?? []).Select(row => new RecipeShopRowMsg
        {
            RecipeId = row.RecipeId,
            Price = row.Price
        }).ToArray();
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new SetRecipeShopCommand(new SetRecipeShopMsg { Message = req.Message ?? "", Items = items }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Recipe shop open failed.", ct).ConfigureAwait(false);
    }

    public async Task OnShopMakeInfoAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeRecipeShopMakeInfo(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new RecipeShopMakeInfoCommand(
                new RecipeShopMakeInfoMsg
                {
                    ManufacturerObjectId = req.ManufacturerObjectId,
                    RecipeId = req.RecipeId
                }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Recipe shop make info failed.", ct).ConfigureAwait(false);
    }

    public async Task OnShopMakeItemAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeRecipeShopMakeItem(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new RecipeShopMakeItemCommand(
                new RecipeShopMakeItemMsg
                {
                    ManufacturerObjectId = req.ManufacturerObjectId,
                    RecipeId = req.RecipeId
                }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Recipe shop make item failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
