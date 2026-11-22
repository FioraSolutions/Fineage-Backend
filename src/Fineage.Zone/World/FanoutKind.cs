namespace Fineage.Zone.World;

/// <summary>Issue #867 PR5: compact fan-out entry kind (replaces free-form strings on hot paths).</summary>
internal enum FanoutKind : byte
{
    Other = 0,
    Immediate = 1,
    MoveState = 2,
    ChangeMoveType = 3,
    StatusUpdate = 4,
    AbnormalStatusUpdate = 5,
}

internal static class FanoutKindMapping
{
    public static FanoutKind FromPresence(PresenceCoalesceKind kind) => kind switch
    {
        PresenceCoalesceKind.MoveState => FanoutKind.MoveState,
        PresenceCoalesceKind.ChangeMoveType => FanoutKind.ChangeMoveType,
        PresenceCoalesceKind.StatusUpdate => FanoutKind.StatusUpdate,
        PresenceCoalesceKind.AbnormalStatusUpdate => FanoutKind.AbnormalStatusUpdate,
        _ => FanoutKind.Other,
    };

    public static FanoutKind FromEmitName(string name) => name switch
    {
        nameof(IWorldEventPublisher.FanoutMoveState) => FanoutKind.MoveState,
        nameof(IWorldEventPublisher.FanoutChangeMoveType) => FanoutKind.ChangeMoveType,
        nameof(IWorldEventPublisher.FanoutStatusUpdate) => FanoutKind.StatusUpdate,
        nameof(IWorldEventPublisher.FanoutAbnormalStatusUpdate) => FanoutKind.AbnormalStatusUpdate,
        "immediate" => FanoutKind.Immediate,
        _ => FanoutKind.Other,
    };
}