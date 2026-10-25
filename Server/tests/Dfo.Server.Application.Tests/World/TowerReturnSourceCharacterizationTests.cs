using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Return_source_attempts_both_due_runs_with_real_bus_and_independent_transactions()
    {
        var trace = new TowerReturnSourceTrace();
        var log = new RecordingExecutionLogger<BossTowerProjectionDriver>();
        using var f = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
            services.AddSingleton<ILogger<BossTowerProjectionDriver>>(log);
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65);
            await f.Seed(65, 2749110, 3);
            peer = await AddMember(f, 3);
            Assert.NotNull((await Enter(f)).Run);
            Assert.NotNull((await Tower(f).EnterAsync([new(peer, 0)], peer, 43, default)).Run);
        });
        f.Clock.Advance(TimeSpan.FromSeconds(121));

        await f.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single().TickAsync(default);

        Assert.Equal([f.Session, peer], trace.Attempts.Select(attempt => attempt.Session));
        Assert.Equal(2, trace.Attempts.Select(attempt => attempt.Transaction).Distinct().Count());
        Assert.All(trace.Attempts, attempt =>
        {
            Assert.Equal(DomainTransactionCompletion.Committed, attempt.Transaction.Completion);
            Assert.Null(attempt.Error);
            Assert.Null(attempt.Chain.SourceConnection);
            Assert.Equal(GameplayCallSource.Timer, attempt.Chain.Source);
            Assert.Null(attempt.Recipient!.Metadata);
        });
        Assert.Same(f.Connection, trace.Attempts[0].Recipient!.Connection);
        Assert.All(log.Entries.Where(entry => entry.Level == LogLevel.Warning), entry =>
            Assert.Equal("The legacy invocation has no explicit footprint source.", entry.Exception!.Message));
        Assert.DoesNotContain(trace.ExecutorLog.Entries, entry => entry.Level == LogLevel.Error);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            Assert.Null(Tower(f).Find(peer));
        });
        Assert.Empty(f.Sender.Outbound.Entries);
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Return_source_attempts_both_due_runs_with_real_bus_and_independent_transactions()
    {
        var trace = new TowerReturnSourceTrace();
        var log = new RecordingExecutionLogger<IllusionTowerProjectionDriver>();
        using var f = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
            services.AddSingleton<ILogger<IllusionTowerProjectionDriver>>(log);
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 3);
            peer = await AddMember(f, 3);
            foreach (var session in new[] { f.Session, peer })
            {
                var run = (await Tower(f).EnterAsync([new(session, 0)], session, 42, default)).Run!;
                var context = Context(run, session);
                await Tower(f).LoadAsync(context, default);
                await Tower(f).StartAsync(context, default);
                await TestEffectFootprints.Run([session], cap => Tower(f).DieAsync(context, cap, default));
                Assert.True((await Tower(f).MarkSettledAsync(context, new([]), default)).Changed);
            }
        });
        f.Clock.Advance(TimeSpan.FromMinutes(5));

        await f.Services.GetServices<IHostedService>().OfType<IllusionTowerReturnSweepService>().Single().TickAsync(default);

        Assert.Equal([f.Session, peer], trace.Attempts.Select(attempt => attempt.Session));
        Assert.Equal(2, trace.Attempts.Select(attempt => attempt.Transaction).Distinct().Count());
        Assert.All(trace.Attempts, attempt =>
        {
            Assert.Equal(DomainTransactionCompletion.Committed, attempt.Transaction.Completion);
            Assert.Null(attempt.Error);
            Assert.Null(attempt.Recipient);
            Assert.Null(attempt.Chain.SourceConnection);
            Assert.Equal(GameplayCallSource.Timer, attempt.Chain.Source);
        });
        Assert.DoesNotContain(trace.ExecutorLog.Entries, entry => entry.Level == LogLevel.Error);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session));
            Assert.Null(Tower(f).Find(peer));
        });
        Assert.Empty(f.Sender.Outbound.Entries);
    }
}

internal sealed class TowerReturnSourceTrace
{
    internal List<TowerReturnSourceAttempt> Attempts { get; } = [];
    internal RecordingExecutionLogger<GameplayExecutor> ExecutorLog { get; } = new();
    internal string? FailureMode { get; set; }
    internal SelectedSessionIdentity? FaultSession { get; set; }
    internal IOException Cause { get; } = new("tower return controlled participant or scope failure");
    internal List<string> Steps { get; } = [];
    internal CancellationTokenSource? CancelAfterCommit { get; set; }
    internal SelectedSessionIdentity? CancelAfterSession { get; set; }
    internal void Install(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.AddScoped<TowerReturnScopeResource>();
        services.AddSingleton<CommandWorkUnits>();
        services.AddScoped<ICommandWorkUnits, TowerReturnSourceObserver>();
        services.AddSingleton<ILogger<GameplayExecutor>>(ExecutorLog);
    }
}

internal sealed record TowerReturnSourceAttempt(SelectedSessionIdentity Session, DomainTransaction Transaction,
    GameplayCallChain Chain, SelectedSessionRecipient? Recipient)
{
    internal Exception? Error { get; set; }
    internal object? Command { get; set; }
    internal object? Result { get; set; }
    internal CancellationToken Token { get; set; }
}

internal sealed record TowerUnitObserved(SelectedSessionIdentity Session, Guid RunId, long RoomVersion, IReadOnlyList<SelectedSessionIdentity> Sessions);
internal sealed class TowerReturnSourceObserver(CommandWorkUnits actual, TowerReturnSourceTrace trace,
    IDomainTransactionAccessor transactions, ICharacterDomainService characters, TowerReturnScopeResource resource) : ICommandWorkUnits
{
    public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull =>
        actual.WriteAsync(context, declaration, async (unit, ct) =>
        {
            trace.Steps.Add(declaration.Name);
            var result = await action(unit, ct);
            if (result is not BossTowerActionResult && result is not Dfo.Server.Abstractions.World.IllusionTower.TowerActionResult) return result;
            var owners = declaration.FootprintOwners ?? declaration.Participants.Select(p => p.Session).ToArray();
            var remaining = result is BossTowerActionResult b ? b.Run!.Members : ((Dfo.Server.Abstractions.World.IllusionTower.TowerActionResult)(object)result).Run!.Members;
            var reporter = declaration.Participants.FirstOrDefault(p => p.Connection is not null);
            var run = result is BossTowerActionResult boss ? (boss.Run!.RunId, boss.Run.Room.Version)
                : (((Dfo.Server.Abstractions.World.IllusionTower.TowerActionResult)(object)result).Run!.RunId, ((Dfo.Server.Abstractions.World.IllusionTower.TowerActionResult)(object)result).Run!.Room.Version);
            var prior = trace.Attempts.Where(previous => previous.Chain.Id == GameplayJob.Current!.Value.Id
                && previous.Command is TowerUnitObserved observed && observed.RunId == run.Item1).Select(previous => previous.Session);
            var session = reporter?.Session ?? owners.Except(remaining.Select(member => member.Session)).Except(prior).Single();
            var attempt = new TowerReturnSourceAttempt(session, Assert.IsType<DomainTransaction>(transactions.Current), GameplayJob.Current!.Value, reporter)
                { Command = new TowerUnitObserved(session, run.Item1, run.Item2, owners), Token = ct, Result = result };
            trace.Attempts.Add(attempt);
            try
            {
                if (session == trace.CancelAfterSession && trace.CancelAfterCommit is { } cancellation)
                    attempt.Transaction.RegisterAfterCommit(new object(), _ => { cancellation.Cancel(); return ValueTask.CompletedTask; });
                if (trace.FailureMode is not null)
                {
                    await characters.Get(session.CharacterId).AddGoldAsync(7, ct);
                    if (session == trace.FaultSession)
                    {
                        if (trace.FailureMode == "uncertain") attempt.Transaction.Enlist(new object(), () => new TowerReturnNonFallibleFailure(trace.Cause));
                        if (trace.FailureMode == "scope") resource.Failure = trace.Cause;
                    }
                }
                return result;
            }
            catch (Exception error) { attempt.Error = error; throw; }
        }, token);
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => actual.WriteResultAsync(c, d, a, ct);
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => actual.ReadOnlyAsync(c, d, a, ct);
}

internal sealed class TowerReturnScopeResource : IAsyncDisposable
{
    internal Exception? Failure { get; set; }
    internal bool Disposed { get; private set; }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return Failure is { } error ? ValueTask.FromException(error) : ValueTask.CompletedTask;
    }
}

internal sealed class TowerReturnNonFallibleFailure(Exception cause) : ITransactionParticipant
{
    public bool CommitCanFail => false;
    public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
    public ValueTask CommitAsync(CancellationToken token) => ValueTask.FromException(cause);
    public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
}
