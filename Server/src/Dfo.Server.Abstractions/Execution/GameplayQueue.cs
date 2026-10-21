namespace Dfo.Server.Abstractions.Execution;

/// <summary>
/// Chooses the executor (RQ-002). Only one value space exists today: every key, whatever its scope,
/// selects the one gameplay executor. The scope is reserved for per-channel sharding.
/// </summary>
public readonly record struct GameplayShardKey(string? Scope)
{
    public static GameplayShardKey World => default;
}

public sealed record GameplayWork<T>(
    GameplayShardKey Key,
    GameplayCallChain Chain,
    string CommandType,
    Func<CancellationToken, ValueTask<T>> Body);

public sealed record GameplayWork(
    GameplayShardKey Key,
    GameplayCallChain Chain,
    string CommandType,
    Func<CancellationToken, ValueTask> Body);

/// <summary>
/// The one way gameplay work reaches the executor (spec "入队点"). The work is enqueued before the
/// call returns; the returned task completes when the work has run. Calling it from work already on
/// the executor is a program error (<see cref="GameplaySelfWaitException"/>), never an inline run.
/// </summary>
/// <remarks>
/// Deliberately not a <c>*DomainService</c>: command handlers must not be able to inject it (RQ-070(b)).
/// </remarks>
public interface IGameplayQueue
{
    ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken);

    ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken);
}

/// <summary>RQ-003: work on the executor waited on something that can only finish after it.</summary>
public sealed class GameplaySelfWaitException(string operation, GameplayCallChain chain)
    : InvalidOperationException(
        $"{operation} must not happen on the gameplay executor (root {chain.RootId}, depth {chain.Depth}: {chain.Describe()}).")
{
    public GameplayCallChain Chain { get; } = chain;
}

/// <summary>RQ-053: the work was not enqueued because its chain is deeper than the limit.</summary>
public sealed class GameplayDepthExceededException(GameplayCallChain chain, string commandType)
    : InvalidOperationException(
        $"Gameplay work {commandType} at depth {chain.Depth} exceeds the limit {GameplayCallDepth.Limit}: {chain.Describe(commandType)}.")
{
    public GameplayCallChain Chain { get; } = chain;
    public string CommandType { get; } = commandType;
}

/// <summary>RQ-009: the executor is stopping or stopped and takes no new work.</summary>
/// <remarks><see cref="Exception.InnerException"/> is set when the consumer loop itself faulted.</remarks>
public sealed class GameplayExecutorStoppedException(string commandType, Exception? cause = null)
    : InvalidOperationException($"The gameplay executor is stopping; {commandType} will not run.", cause);
