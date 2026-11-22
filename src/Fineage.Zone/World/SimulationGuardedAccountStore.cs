using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Fineage.Accounts;

namespace Fineage.Zone.World;

/// <summary>
/// <see cref="DispatchProxy"/> over <see cref="IAccountStore"/> that times sync calls on the simulation thread.
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
            long started = Stopwatch.GetTimestamp();
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                return null;
            }
            finally
            {
                _guard.Record(
                    $"IAccountStore.{targetMethod.Name}",
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }
}
