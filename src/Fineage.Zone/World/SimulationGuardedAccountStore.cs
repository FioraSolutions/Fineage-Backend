using System.Reflection;
using Fineage.Accounts;

namespace Fineage.Zone.World;

/// <summary>
/// <see cref="DispatchProxy"/> over <see cref="IAccountStore"/> that reports sync I/O on the simulation thread
/// during enter/command scopes.
/// </summary>
static class SimulationGuardedAccountStore
{
    public static IAccountStore Wrap(IAccountStore inner, SimulationSyncIoGuard guard)
    {
        var proxy = DispatchProxy.Create<IAccountStore, GuardedAccountStoreProxy>()!;
        ((GuardedAccountStoreProxy)(object)proxy).Initialize(inner, guard);
        return proxy;
    }

    class GuardedAccountStoreProxy : DispatchProxy
    {
        IAccountStore _inner = null!;
        SimulationSyncIoGuard _guard = null!;

        internal void Initialize(IAccountStore inner, SimulationSyncIoGuard guard)
        {
            _inner = inner;
            _guard = guard;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            _guard.Report($"IAccountStore.{targetMethod.Name}");
            return targetMethod.Invoke(_inner, args);
        }
    }
}