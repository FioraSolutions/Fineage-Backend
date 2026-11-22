using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// GC allocation probes must run without other tests on the same worker thread.
/// </summary>
[CollectionDefinition(nameof(KnownlistPlanDiffCollection), DisableParallelization = true)]
public sealed class KnownlistPlanDiffCollection;