using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed record TowerReturnsDue<T>(DateTimeOffset CapturedAt, ImmutableArray<T> Contexts);

public abstract class TowerReturnSweepSource<T>(ICommandBus commands, TimeProvider clock, ILogger logger)
    : BackgroundService, IGameplayProducerDrain where T : notnull
{
    private readonly object lifecycle = new();
    private readonly HashSet<Task> rounds = [];
    private bool draining;
    private int outstanding;
    protected abstract string SourceName { get; }
    protected abstract ICommand<ImmutableArray<TowerDepartureEntryId>> CaptureDepartures { get; }
    protected abstract ICommand<bool> Departure(TowerDepartureEntryId id);
    protected abstract ICommand<TowerReturnsDue<T>> CaptureDue { get; }
    protected abstract ICommand<bool> Return(T context, DateTimeOffset at);

    public Task TickAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifecycle)
        {
            if (draining) return Task.FromException(new GameplayExecutorStoppedException(SourceName));
            rounds.Add(completion.Task);
        }
        _ = CompleteAsync();
        return WaitRoundAsync(completion.Task);

        async Task CompleteAsync()
        {
            try { await RunRoundAsync(token).ConfigureAwait(false); completion.TrySetResult(); }
            catch (OperationCanceledException error) { completion.TrySetException(error); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { lock (lifecycle) rounds.Remove(completion.Task); }
        }
    }
    private static async Task WaitRoundAsync(Task completion) => await completion.ConfigureAwait(false);
    private async Task RunRoundAsync(CancellationToken token)
    {
        var root = GameplayCallChain.NewRoot(GameplayCallSource.Timer).ForCommand(SourceName);
        var pending = await commands.SendInternalAsync(CaptureDepartures, Origin(root, SourceName), token).ConfigureAwait(false);
        foreach (var id in pending)
            await RunItemAsync(Departure(id), root.Child(GameplayCallSource.Timer), SourceName + "/departure", token).ConfigureAwait(false);
        var dueChain = root.Child(GameplayCallSource.Timer).ForCommand(SourceName + "/due");
        var due = await commands.SendInternalAsync(CaptureDue, Origin(dueChain, SourceName + "/due"), token).ConfigureAwait(false);
        foreach (var context in due.Contexts)
            await RunItemAsync(Return(context, due.CapturedAt), dueChain.Child(GameplayCallSource.Timer), SourceName + "/run", token).ConfigureAwait(false);
    }
    private async ValueTask RunItemAsync(ICommand<bool> command, GameplayCallChain chain, string label, CancellationToken token)
    {
        try { await commands.SendInternalAsync(command, Origin(chain, label), token).ConfigureAwait(false); }
        catch (Exception error) when (GameplayJob.EndedAJob(error))
        { logger.LogWarning("Timer {CommandType} item failed; continuing with the next captured item", label); }
    }
    private static InternalCommandOrigin Origin(GameplayCallChain chain, string label) =>
        new(GameplayCallSource.Timer, chain, label, InternalCommandCancellation.BodyOnly);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (Interlocked.CompareExchange(ref outstanding, 1, 0) != 0) continue;
            try { await TickAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (GameplayJob.EndedAJob(error))
            { logger.LogWarning("Timer {CommandType} scan failed; the hosted service continues with its next tick", SourceName); }
            catch (Exception error) { logger.LogError(error, "Unexpected tower return source failure; the hosted service will continue"); }
            finally { Volatile.Write(ref outstanding, 0); }
        }
    }
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        Task[] accepted;
        lock (lifecycle) { draining = true; accepted = rounds.ToArray(); }
        try { await Task.WhenAll(accepted).WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch when (accepted.All(task => task.IsCompleted)) { /* Each caller owns its own round outcome. */ }
    }
}
public sealed class BossTowerReturnSweepService(ICommandBus commands, TimeProvider clock, ILogger<BossTowerReturnSweepService> logger)
    : TowerReturnSweepSource<BossTowerActionContext>(commands, clock, logger)
{
    protected override string SourceName => "timer:BossTowerReturnSweepService";
    protected override ICommand<ImmutableArray<TowerDepartureEntryId>> CaptureDepartures => new CaptureBossTowerDeparturesCommand();
    protected override ICommand<bool> Departure(TowerDepartureEntryId id) => new ProjectBossTowerDepartureCommand(id);
    protected override ICommand<TowerReturnsDue<BossTowerActionContext>> CaptureDue => new CaptureBossTowerReturnsDueCommand();
    protected override ICommand<bool> Return(BossTowerActionContext context, DateTimeOffset at) => new ReturnBossTowerRunCommand(context, at);
}
public sealed class IllusionTowerReturnSweepService(ICommandBus commands, TimeProvider clock, ILogger<IllusionTowerReturnSweepService> logger)
    : TowerReturnSweepSource<TowerActionContext>(commands, clock, logger)
{
    protected override string SourceName => "timer:IllusionTowerReturnSweepService";
    protected override ICommand<ImmutableArray<TowerDepartureEntryId>> CaptureDepartures => new CaptureIllusionTowerDeparturesCommand();
    protected override ICommand<bool> Departure(TowerDepartureEntryId id) => new ProjectIllusionTowerDepartureCommand(id);
    protected override ICommand<TowerReturnsDue<TowerActionContext>> CaptureDue => new CaptureIllusionTowerReturnsDueCommand();
    protected override ICommand<bool> Return(TowerActionContext context, DateTimeOffset at) => new ReturnIllusionTowerRunCommand(context, at);
}
