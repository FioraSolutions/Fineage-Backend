namespace Fineage.Zone.World;

/// <summary>Stable id for an immutable watcher audience within one <see cref="WorldFanoutBatch"/>; hashed/compared by id only.</summary>
internal readonly record struct AudienceSnapshotId(ulong Value);