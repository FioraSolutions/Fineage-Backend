namespace Fineage.Zone.Tests;

/// <summary>
/// Hand-driven net visibility tests share loopback hosts; serializing them avoids wall-clock timeouts on saturated CI runners.
/// </summary>
[CollectionDefinition(nameof(NetMoveVisibilityCollection), DisableParallelization = true)]
public sealed class NetMoveVisibilityCollection;