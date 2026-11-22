using Fineage.Net.Contract;

namespace Fineage.Zone.Net;

/// <summary>Compact NPC fields for off-thread <see cref="EntitySpawnMsg"/> build during enter encode.</summary>
readonly record struct NpcPresenceCapture(
    ulong EntityValue,
    uint EntityGeneration,
    string Name,
    int TemplateId,
    int X,
    int Y,
    int Z,
    int Heading,
    float RunSpeed,
    float WalkSpeed,
    bool Running,
    bool Attackable,
    int Level,
    int MaxHp,
    int CurHp,
    int PAtkSpd,
    string Title,
    SummonKindNet SummonKind,
    int OwnerObjectId,
    int Rhand,
    int Lhand);