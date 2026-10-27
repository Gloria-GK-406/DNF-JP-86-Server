using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

internal sealed record ExecutionLogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    Exception? Exception);

internal sealed class RecordingExecutionLogger<T> : ILogger<T>
{
    public ConcurrentQueue<ExecutionLogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            foreach (var pair in pairs) fields[pair.Key] = pair.Value;
        Entries.Enqueue(new ExecutionLogEntry(logLevel, formatter(state, exception), fields, exception));
    }
}

internal static class ExecutionTest
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public static (GameplayExecutor Executor, RecordingExecutionLogger<GameplayExecutor> Log) Executor()
    {
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        return (new GameplayExecutor(log), log);
    }

    public static GameplayCallChain Root(string? connection = null) =>
        GameplayCallChain.NewRoot(
            GameplayCallSource.Connection,
            connection is null ? null : new Dfo.Server.Data.Networking.NetworkConnectionId(connection));

    public static GameplayWork<T> Work<T>(
        Func<CancellationToken, ValueTask<T>> body,
        string commandType = "TestCommand",
        GameplayCallChain? chain = null) =>
        new(GameplayShardKey.World, chain ?? Root(), commandType, body);

    public static GameplayWork Work(
        Func<CancellationToken, ValueTask> body,
        string commandType = "TestCommand",
        GameplayCallChain? chain = null) =>
        new(GameplayShardKey.World, chain ?? Root(), commandType, body);
}
