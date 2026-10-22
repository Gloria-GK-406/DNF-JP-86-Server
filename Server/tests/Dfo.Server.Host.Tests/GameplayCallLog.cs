using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>Where one call into a decorated service ran: on the executor or not, and as which job.</summary>
internal sealed record GameplayCall(string Service, string Method, bool OnExecutor, Guid? JobId,
    GameplayCallSource? Source, string? CommandType, bool Cancelled);

/// <summary>
/// Records the execution position of every call into the services it decorates (spec "入口证据测试"):
/// the record is taken on the caller's flow, before the call starts, so it sees the caller's job token.
/// </summary>
internal sealed class GameplayCallLog
{
    private readonly ConcurrentQueue<GameplayCall> calls = new();

    /// <summary>Runs after a call returned; lets a test change the world between two phases.</summary>
    public Func<GameplayCall, ValueTask>? After { get; set; }

    public IReadOnlyList<GameplayCall> Calls => calls.ToArray();

    public IReadOnlyList<GameplayCall> Of(string method) => calls.Where(call => call.Method == method).ToArray();

    public void Clear() => calls.Clear();

    public GameplayCall Record(string service, string method, CancellationToken token)
    {
        var current = GameplayJob.Current;
        var call = new GameplayCall(service, method, GameplayJob.IsOnExecutor, current?.Id, current?.Source,
            current?.CommandType, token.IsCancellationRequested);
        calls.Enqueue(call);
        return call;
    }

    /// <summary>Wraps the last registration of <typeparamref name="T"/> in a recording proxy.</summary>
    public void Decorate<T>(IServiceCollection services) where T : class
    {
        var existing = services.Last(descriptor => descriptor.ServiceType == typeof(T) && !descriptor.IsKeyedService);
        services.Add(ServiceDescriptor.Describe(typeof(T), provider =>
        {
            var inner = (T)(existing.ImplementationInstance
                ?? existing.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, existing.ImplementationType!));
            var proxy = DispatchProxy.Create<T, RecordingProxy<T>>();
            ((RecordingProxy<T>)(object)proxy).Attach(inner, this);
            return proxy;
        }, existing.Lifetime));
    }
}

public class RecordingProxy<T> : DispatchProxy where T : class
{
    private T inner = null!;
    private GameplayCallLog log = null!;

    internal void Attach(T target, GameplayCallLog calls)
    {
        inner = target;
        log = calls;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var token = args?.OfType<CancellationToken>().LastOrDefault() ?? default;
        var call = log.Record(typeof(T).Name, targetMethod.Name, token);
        object? result;
        try
        {
            result = targetMethod.Invoke(inner, args);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        if (log.After is not { } after) return result;
        return result switch
        {
            ValueTask task => Then(task, after, call),
            Task task when targetMethod.ReturnType == typeof(Task) => ThenTask(task, after, call),
            _ when targetMethod.ReturnType.IsGenericType
                && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>) =>
                typeof(RecordingProxy<T>).GetMethod(nameof(ThenValue), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(targetMethod.ReturnType.GetGenericArguments()[0])
                    .Invoke(null, [result, after, call]),
            _ => result,
        };
    }

    private static async ValueTask Then(ValueTask task, Func<GameplayCall, ValueTask> after, GameplayCall call)
    {
        await task.ConfigureAwait(false);
        await after(call).ConfigureAwait(false);
    }

    private static async Task ThenTask(Task task, Func<GameplayCall, ValueTask> after, GameplayCall call)
    {
        await task.ConfigureAwait(false);
        await after(call).ConfigureAwait(false);
    }

    private static async ValueTask<TResult> ThenValue<TResult>(ValueTask<TResult> task, Func<GameplayCall, ValueTask> after,
        GameplayCall call)
    {
        var value = await task.ConfigureAwait(false);
        await after(call).ConfigureAwait(false);
        return value;
    }
}
