using System.Runtime.CompilerServices;

namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// State that belongs to one running job and lives exactly as long as its token. Reading it off the
/// executor (no job, a finished job, or inside <see cref="GameplayJob.RunDetached(Action)"/>) is a
/// program error, so per-job state can never leak into the work that runs after it.
/// </summary>
public sealed class GameplayJobSlot<T> where T : class
{
    private readonly ConditionalWeakTable<GameplayJobToken, T> values = new();

    public T GetOrCreate(string operation, Func<T> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(create);
        var token = GameplayJob.Active ?? throw new GameplayOffExecutorException(operation);
        return values.GetValue(token, _ => create());
    }
}

/// <summary>The reverse of <see cref="GameplayJob.AssertNotOnExecutor"/>: the operation needs a running job.</summary>
public sealed class GameplayOffExecutorException(string operation)
    : InvalidOperationException($"{operation} must happen inside a gameplay job.");
