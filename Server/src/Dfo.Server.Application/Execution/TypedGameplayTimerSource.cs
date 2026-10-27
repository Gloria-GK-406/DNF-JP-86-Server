using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution;

public abstract class TypedGameplayTimerSource(ICommandBus commands, ILogger logger) : BackgroundService, IGameplayProducerDrain
{
    private readonly object lifecycle = new();
    private readonly HashSet<Task> rounds = [];
    private readonly SemaphoreSlim serial = new(1);
    private bool draining;
    private bool hostedOutstanding;
    protected virtual bool SerializeRounds => false;
    protected abstract string SourceName { get; }
    protected abstract Task RunRoundAsync(GameplayCallChain root, CancellationToken token);
    protected Task RunAsync(CancellationToken token, GameplayCallSource source = GameplayCallSource.Timer, bool hosted = false)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifecycle)
        {
            if (draining) return Task.FromException(new GameplayExecutorStoppedException(SourceName));
            if (hosted && (hostedOutstanding || SerializeRounds && rounds.Count != 0)) return Task.CompletedTask;
            if (hosted) hostedOutstanding = true;
            rounds.Add(completion.Task);
        }
        _ = CompleteAsync();
        return completion.Task;
        async Task CompleteAsync()
        {
            // Admission is None: a cancelled accepted round still reaches its original body.
            if (SerializeRounds) await serial.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                var label = source == GameplayCallSource.StartupRecovery ? SourceName.Replace("timer:", "startup:", StringComparison.Ordinal) : SourceName;
                await RunRoundAsync(GameplayCallChain.NewRoot(source).ForCommand(label), token).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (SerializeRounds) serial.Release();
                lock (lifecycle)
                {
                    if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
                    rounds.Remove(completion.Task);
                    if (hosted) hostedOutstanding = false;
                }
            }
        }
    }
    protected ValueTask<T> SubmitAsync<T>(ICommand<T> command, GameplayCallChain chain, string label, CancellationToken token) where T : notnull =>
        commands.SendInternalAsync(command, new(chain.Source, chain, label, InternalCommandCancellation.BodyOnly), token);
    protected async ValueTask ItemAsync(ICommand<bool> command, GameplayCallChain root, string label, CancellationToken token)
    {
        try { await SubmitAsync(command, root.Child(root.Source), label, token).ConfigureAwait(false); }
        catch (Exception error) when (GameplayJob.EndedAJob(error))
        { logger.LogWarning("Timer {CommandType} item failed; continuing with the next captured item", label); }
    }
    protected async Task HostedAsync(CancellationToken token)
    {
        try { await RunAsync(token, hosted: true).ConfigureAwait(false); }
        catch (Exception error) when (GameplayJob.EndedAJob(error))
        { logger.LogWarning("Timer {CommandType} failed; the hosted service continues with its next tick", SourceName); }
    }
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        Task[] accepted;
        lock (lifecycle) { draining = true; accepted = rounds.ToArray(); }
        try { await Task.WhenAll(accepted).WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch when (accepted.All(task => task.IsCompleted)) { }
    }
}

public static class TypedGameplayTimerRegistration
{
    public static IServiceCollection AddTypedGameplayTimer<T>(this IServiceCollection services) where T : TypedGameplayTimerSource
    {
        services.TryAddSingleton<T>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<T>());
        services.AddSingleton<IGameplayProducerDrain>(provider => provider.GetRequiredService<T>());
        return services;
    }
}
