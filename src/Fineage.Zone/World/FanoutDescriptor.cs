namespace Fineage.Zone.World;

/// <summary>
/// One recorded fan-out. The descriptor is a struct; the arguments live in the batch's payload list,
/// and the watcher ids live in <see cref="AudienceSnapshots"/> under <see cref="Audience"/>.
/// </summary>
internal readonly record struct FanoutDescriptor(
    FanoutKind Kind,
    int SubjectId,
    AudienceSnapshotId Audience,
    int PayloadIndex,
    bool Superseded,
    bool Dropped = false);

/// <summary>
/// Arguments for one fan-out. Scalar fields cover the publisher methods; <see cref="Payload"/> holds a
/// protocol message, a copied <c>int[]</c>, a sound name, or a prepared send.
/// </summary>
internal readonly record struct FanoutArgs(
    int A = 0,
    int B = 0,
    int C = 0,
    int D = 0,
    int E = 0,
    int F = 0,
    int G = 0,
    float Fa = 0,
    float Fb = 0,
    bool Flag = false,
    bool Flag2 = false,
    object? Payload = null);
