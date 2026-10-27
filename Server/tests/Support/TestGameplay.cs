using System.Reflection;
using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Testing;

/// <summary>Runs a test's own setup and direct calls as one job on a real gameplay executor.</summary>
/// <remarks>
/// <para>
/// Only for what the test body does itself: opening write transactions, touching gameplay state,
/// taking gates, calling a handler it built. A test whose subject is a production entry (the real
/// dispatcher, an event dispatcher, a hosted service tick, an HTTP endpoint, a real socket,
/// <c>ICommandBus.SendAsync</c>) drives that entry unwrapped; wrapping it would hide an entry that
/// was never moved onto the executor.
/// </para>
/// <para>
/// The executor is chosen in this order: the one already running this flow (the body runs inline,
/// which keeps the outer job's ambient transaction and frames visible), the
/// <see cref="GameplayExecutor"/> registered in <c>services</c>, otherwise a fresh executor that is
/// disposed as soon as this one job has run. A test that has a container passes it, so that once the
/// container registers the executor, a production entry wrapped by mistake fails as a self-wait.
/// </para>
/// </remarks>
internal static class TestGameplay
{
    private const GameplayCallSource Source = GameplayCallSource.Connection;

    public static Task RunAsync(Func<Task> body, [CallerMemberName] string caller = "") =>
        RunCoreAsync(null, Untyped(body), caller, released: null);

    public static Task<T> RunAsync<T>(Func<Task<T>> body, [CallerMemberName] string caller = "") =>
        RunCoreAsync(null, body, caller, released: null);

    public static Task RunAsync(IServiceProvider services, Func<Task> body, [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(services);
        return RunCoreAsync(services, Untyped(body), caller, released: null);
    }

    public static Task<T> RunAsync<T>(IServiceProvider services, Func<Task<T>> body, [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(services);
        return RunCoreAsync(services, body, caller, released: null);
    }

    /// <summary>Blocks until the job has run; for synchronous tests, constructors and fixtures.</summary>
    public static void Run(Action body, [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        RunCore(null, () =>
        {
            body();
            return true;
        }, caller, released: null, async: IsAsyncLambda(body));
    }

    /// <inheritdoc cref="Run(Action, string)"/>
    public static T Run<T>(Func<T> body, [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        return RunCore(null, body, caller, released: null, async: false);
    }

    /// <inheritdoc cref="Run(Action, string)"/>
    public static void Run(IServiceProvider services, Action body, [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(body);
        RunCore(services, () =>
        {
            body();
            return true;
        }, caller, released: null, async: IsAsyncLambda(body));
    }

    /// <inheritdoc cref="Run(Action, string)"/>
    public static T Run<T>(IServiceProvider services, Func<T> body, [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(body);
        return RunCore(services, body, caller, released: null, async: false);
    }

    /// <summary>For TestGameplayTests: hands over the executor this call created, once it is released.</summary>
    internal static Task<T> RunObservedAsync<T>(Func<Task<T>> body, Action<GameplayExecutor> released,
        [CallerMemberName] string caller = "") => RunCoreAsync(null, body, caller, released);

    /// <inheritdoc cref="RunObservedAsync{T}"/>
    internal static T RunObserved<T>(Func<T> body, Action<GameplayExecutor> released,
        [CallerMemberName] string caller = "") => RunCore(null, body, caller, released, async: false);

    private static Func<Task<bool>> Untyped(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return async () =>
        {
            await body();
            return true;
        };
    }

    private static Task<T> RunCoreAsync<T>(
        IServiceProvider? services, Func<Task<T>> body, string caller, Action<GameplayExecutor>? released)
    {
        ArgumentNullException.ThrowIfNull(body);
        // Nested inside a job (a wrapped test calling a fixture that wraps itself): a new job would
        // run under suppressed flow and lose the outer job's ambient transaction, so run inline.
        if (GameplayJob.IsOnExecutor) return body();
        var work = Work(caller, async _ => await body());
        if (Registered(services) is { } shared) return shared.RunAsync(work, CancellationToken.None).AsTask();
        return OnOwnExecutorAsync(work, released);
    }

    private static async Task<T> OnOwnExecutorAsync<T>(GameplayWork<T> work, Action<GameplayExecutor>? released)
    {
        var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        T result;
        try
        {
            result = await executor.RunAsync(work, CancellationToken.None);
        }
        finally
        {
            await executor.DisposeAsync();
        }
        EnsureStopped(executor);
        released?.Invoke(executor);
        return result;
    }

    private static T RunCore<T>(
        IServiceProvider? services, Func<T> body, string caller, Action<GameplayExecutor>? released, bool async)
    {
        // An async lambda or a task result would end the job before the work does.
        if (async)
            throw new ArgumentException("Run takes synchronous work; use RunAsync for an async lambda.", nameof(body));
        if (IsAwaitable(typeof(T)))
            throw new ArgumentException($"Run cannot wait for a {typeof(T).Name}; use RunAsync.", nameof(body));
        if (GameplayJob.IsOnExecutor) return body();
        var work = Work(caller, _ => ValueTask.FromResult(body()));
        if (Registered(services) is { } shared)
            return shared.RunAsync(work, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        T result;
        try
        {
            result = executor.RunAsync(work, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            executor.Dispose();
        }
        EnsureStopped(executor);
        released?.Invoke(executor);
        return result;
    }

    private static GameplayWork<T> Work<T>(string caller, Func<CancellationToken, ValueTask<T>> body) =>
        new(GameplayShardKey.World, GameplayCallChain.NewRoot(Source), "test:" + caller, ct =>
        {
            // The proof, at every call, that the body really runs as a job.
            if (!GameplayJob.IsOnExecutor)
                throw new InvalidOperationException("TestGameplay ran its body off the gameplay executor.");
            return body(ct);
        });

    private static GameplayExecutor? Registered(IServiceProvider? services) =>
        services?.GetService(typeof(GameplayExecutor)) as GameplayExecutor;

    private static void EnsureStopped(GameplayExecutor executor)
    {
        if (!executor.Completion.IsCompleted)
            throw new InvalidOperationException("TestGameplay left its executor's consumer loop running.");
    }

    private static bool IsAsyncLambda(Delegate body) =>
        body.Method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null;

    private static bool IsAwaitable(Type type) =>
        typeof(Task).IsAssignableFrom(type)
        || type == typeof(ValueTask)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));
}
