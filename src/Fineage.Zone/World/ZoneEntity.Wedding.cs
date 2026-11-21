namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    public ZoneWeddingSessionPayload? WeddingSession { get; set; }

    ZoneWeddingSessionPayload EnsureWeddingSession() => WeddingSession ??= new ZoneWeddingSessionPayload();

    public int EngageInviterId
    {
        get => WeddingSession?.EngageInviterId ?? 0;
        set => EnsureWeddingSession().EngageInviterId = value;
    }

    public bool MarryRequest
    {
        get => WeddingSession?.MarryRequest ?? false;
        set => EnsureWeddingSession().MarryRequest = value;
    }

    public bool MarryAccepted
    {
        get => WeddingSession?.MarryAccepted ?? false;
        set => EnsureWeddingSession().MarryAccepted = value;
    }
}