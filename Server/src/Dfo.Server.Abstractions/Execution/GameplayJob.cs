namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// "On the executor" means holding the token of a job that is still running. The token travels in an
/// <see cref="AsyncLocal{T}"/>, so work that copies the context keeps a reference to it; the token is
/// therefore invalidated when the job ends, and a stale copy reads as off the executor.
/// Only an executor (a <see cref="GameplayJobHost"/>) enters and exits jobs; everyone else may only
/// read, assert or run detached. "On the executor" means on any executor: production has one (RQ-001).
/// </summary>
public static class GameplayJob
{
    internal static readonly AsyncLocal<GameplayJobToken?> Running = new();

    public static bool IsOnExecutor => Active is not null;

    /// <summary>The running job's chain, stamped with its CMD type; children derive from it.</summary>
    public static GameplayCallChain? Current => Active?.Chain;

    /// <summary>
    /// Throws when called from a running job (RQ-003): <paramref name="operation"/> would wait on the
    /// executor. The error also sticks to the job, so the CMD fails even if the caller swallows it.
    /// </summary>
    public static void AssertNotOnExecutor(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (Active is { } token) throw token.Fault(new GameplaySelfWaitException(operation, token.Chain));
    }

    /// <summary>
    /// Runs <paramref name="action"/> as off the executor and restores the token afterwards. For
    /// transport callbacks that may run inline inside a job: clearing the token outright would leak
    /// back into the job.
    /// </summary>
    public static void RunDetached(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = Running.Value;
        Running.Value = null;
        try
        {
            action();
        }
        finally
        {
            Running.Value = previous;
        }
    }

    public static T RunDetached<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = Running.Value;
        Running.Value = null;
        try
        {
            return action();
        }
        finally
        {
            Running.Value = previous;
        }
    }

    public static async ValueTask RunDetachedAsync(Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = Running.Value;
        Running.Value = null;
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            Running.Value = previous;
        }
    }

    public static async ValueTask<T> RunDetachedAsync<T>(Func<ValueTask<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = Running.Value;
        Running.Value = null;
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Running.Value = previous;
        }
    }

    internal static GameplayJobToken? Active => Running.Value is { IsActive: true } token ? token : null;
}

/// <summary>An opaque handle on one running job; only the <see cref="GameplayJobHost"/> that issued it can use it.</summary>
public sealed class GameplayJobToken
{
    private volatile bool active = true;
    private Exception? programError;

    internal GameplayJobToken(GameplayJobHost owner, GameplayCallChain chain)
    {
        Owner = owner;
        Chain = chain;
    }

    internal GameplayJobHost Owner { get; }

    internal GameplayCallChain Chain { get; }

    internal bool IsActive => active;

    /// <summary>The first program error raised inside this job; it decides the job's outcome.</summary>
    internal Exception? ProgramError => Volatile.Read(ref programError);

    internal Exception Fault(Exception error)
    {
        Interlocked.CompareExchange(ref programError, error, null);
        return error;
    }

    internal void Deactivate() => active = false;
}

/// <summary>
/// The only way into and out of a job token. An executor derives from it so that Abstractions keeps
/// its internals to itself instead of opening them to the executor's assembly. A host only accepts the
/// tokens it issued, and <c>GameplayExecutor</c> is the only host (RQ-072, GameplayJobHostStructureTests).
/// </summary>
public abstract class GameplayJobHost
{
    /// <summary>The running job's token when that job belongs to this executor; self-waits are judged by it.</summary>
    protected GameplayJobToken? JobRunningHere => GameplayJob.Active is { } token && ReferenceEquals(token.Owner, this)
        ? token
        : null;

    /// <summary>The running job's token on any executor.</summary>
    protected static GameplayJobToken? JobRunningAnywhere => GameplayJob.Active;

    protected GameplayJobToken EnterJob(GameplayCallChain chain)
    {
        var token = new GameplayJobToken(this, chain);
        GameplayJob.Running.Value = token;
        return token;
    }

    protected void ExitJob(GameplayJobToken token)
    {
        OwnedOrThrow(token, nameof(ExitJob));
        token.Deactivate();
        if (ReferenceEquals(GameplayJob.Running.Value, token)) GameplayJob.Running.Value = null;
    }

    protected GameplayCallChain ChainOf(GameplayJobToken token)
    {
        OwnedOrThrow(token, nameof(ChainOf));
        return token.Chain;
    }

    /// <summary>Records <paramref name="error"/> as the job's program error (first one wins) and returns it.</summary>
    protected Exception Fault(GameplayJobToken token, Exception error)
    {
        OwnedOrThrow(token, nameof(Fault));
        return token.Fault(error);
    }

    protected Exception? ProgramErrorOf(GameplayJobToken token)
    {
        OwnedOrThrow(token, nameof(ProgramErrorOf));
        return token.ProgramError;
    }

    /// <summary>
    /// Records <paramref name="error"/> on the job running on this flow, whichever executor runs it, and
    /// returns it. A depth refusal sticks to the enqueuing job even across executors; anyone can already
    /// do the same through <see cref="GameplayJob.AssertNotOnExecutor"/>.
    /// </summary>
    protected static Exception FaultRunningJob(Exception error) =>
        GameplayJob.Active is { } token ? token.Fault(error) : error;

    /// <summary>Throws a self-wait if called from a job of this executor; it sticks to that job.</summary>
    protected void ThrowIfRunningHere(string operation)
    {
        if (JobRunningHere is { } token) throw token.Fault(new GameplaySelfWaitException(operation, token.Chain));
    }

    /// <summary>A token reaching another host is a defect of the executor itself, so it only throws (RQ-072).</summary>
    private void OwnedOrThrow(GameplayJobToken token, string operation)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!ReferenceEquals(token.Owner, this))
            throw new InvalidOperationException($"{operation} was given a job token of another executor.");
    }
}
