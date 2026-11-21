using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Work for the simulation thread that is not player input: session enter and leave, shortcut edits, and the
/// read-only projections handlers send. It goes through the command ingress, so it is applied in the commands stage
/// in submission order with the commands around it, but it is neither logged nor replayed.
/// </summary>
internal sealed record ZoneRequest<T>(string What, Func<TickContext, T> Run) : ZoneCommand
{
    public override string Name => $"request:{What}";

    internal override bool Logged => false;

    /// <summary>What <see cref="Run"/> returned; read it once the reply completed.</summary>
    public T Result { get; private set; } = default!;

    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick)
    {
        Result = Run(tick);
        return null;
    }
}

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Runs <paramref name="body"/> on the simulation thread for <paramref name="session"/>: right away when the
    /// caller may touch the world (nothing drives it, or the caller is the step), otherwise as a request applied by
    /// the next step, whose fan-out has been sent when the task completes.
    /// </summary>
    internal Task<T> OnSimulationAsync<T>(ZoneSession session, string what, Func<T> body, CancellationToken ct)
    {
        if (_affinity.MayTouch)
        {
            try
            {
                return Task.FromResult(body());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        return RequestAsync(session, new ZoneRequest<T>(what, _ => body()), ct);
    }

    /// <summary>
    /// Submits <paramref name="command"/> like <see cref="ExecuteAsync"/> and runs <paramref name="around"/> on the
    /// simulation thread in the step that applies it: <paramref name="around"/> gets a function that applies the
    /// command (logged as usual) and returns its rejection, and reads what it needs before and after. It must not
    /// change the world, since replay applies the command alone.
    /// </summary>
    internal Task<T> ExecuteAroundAsync<T>(
        ZoneSession session, ZoneCommand command, Func<Func<string?>, T> around, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return RequestAsync(session, new ZoneRequest<T>(command.Name, tick => around(() => ApplyLogged(session, command, tick))), ct);
    }

    /// <summary>Test hook: runs <paramref name="body"/> as the world's owner, after any step running on another thread.</summary>
    internal T OnStepForTest<T>(Func<T> body)
    {
        using (_affinity.Step())
            return body();
    }

    async Task<T> RequestAsync<T>(ZoneSession session, ZoneRequest<T> request, CancellationToken ct)
    {
        await _ingress.Submit(session, request, ct).ConfigureAwait(false);
        return request.Result;
    }

    string? ApplyLogged(ZoneSession session, ZoneCommand command, TickContext tick)
    {
        string? error;
        try
        {
            error = command.Apply(this, session, tick);
        }
        catch (Exception ex)
        {
            _commandLog?.AppendCommand(tick.Tick, tick.WallUnixMs, session.ObjectId, command, $"threw {ex.GetType().Name}");
            throw;
        }

        _commandLog?.AppendCommand(tick.Tick, tick.WallUnixMs, session.ObjectId, command, error ?? "ok");
        return error;
    }
}
