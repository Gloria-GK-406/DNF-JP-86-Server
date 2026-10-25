using Dfo.Server.Abstractions.Execution;
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
        var log = new RecordingExecutionLogger<BossTowerProjectionDelivery>();
        using var f = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ILogger<BossTowerProjectionDelivery>>(log);
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
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
            Assert.Equal("The legacy invocation has no explicit footprint source.", attempt.Error!.Message);
            Assert.Null(attempt.Chain.SourceConnection);
            Assert.Equal(GameplayCallSource.Timer, attempt.Chain.Source);
            Assert.Null(attempt.Recipient!.Metadata);
        });
        Assert.Same(f.Connection, trace.Attempts[0].Recipient!.Connection);
        Assert.All(log.Entries.Where(entry => entry.Level == LogLevel.Warning), entry =>
            Assert.Equal("The legacy invocation has no explicit footprint source.", entry.Exception!.Message));
        Assert.Equal(2, log.Entries.Count(entry => entry.Level == LogLevel.Warning));
        TestGameplay.Run(f.Services, () =>
        {
            Assert.NotNull(Tower(f).Find(f.Session));
            Assert.NotNull(Tower(f).Find(peer));
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
        var log = new RecordingExecutionLogger<IllusionTowerProjectionDelivery>();
        using var f = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ILogger<IllusionTowerProjectionDelivery>>(log);
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
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
            Assert.Equal("The legacy invocation has no explicit footprint source.", attempt.Error!.Message);
            Assert.Null(attempt.Recipient);
            Assert.Null(attempt.Chain.SourceConnection);
            Assert.Equal(GameplayCallSource.Timer, attempt.Chain.Source);
        });
        Assert.Equal(2, log.Entries.Count(entry => entry.Level == LogLevel.Warning));
        TestGameplay.Run(f.Services, () =>
        {
            Assert.NotNull(Tower(f).Find(f.Session));
            Assert.NotNull(Tower(f).Find(peer));
        });
        Assert.Empty(f.Sender.Outbound.Entries);
    }
}

internal sealed class TowerReturnSourceTrace
{
    internal List<TowerReturnSourceAttempt> Attempts { get; } = [];
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
        services.AddScoped(typeof(ICommandPipelineBehavior<>), typeof(TowerReturnSourceObserver<>));
    }
}

internal sealed record TowerReturnSourceAttempt(SelectedSessionIdentity Session, DomainTransaction Transaction,
    GameplayCallChain Chain, SelectedSessionRecipient? Recipient)
{
    internal Exception? Error { get; set; }
    internal ICommand? Command { get; set; }
    internal object? Result { get; set; }
    internal CancellationToken Token { get; set; }
}

internal sealed class TowerReturnSourceObserver<T>(TowerReturnSourceTrace trace, IDomainTransactionAccessor transactions,
    SelectedSessionCommandScope selected, ICharacterDomainService characters, TowerReturnScopeResource resource) : ICommandPipelineBehavior<T> where T : notnull
{
    public int Order => 61;
    public async ValueTask<T> ExecuteAsync(CommandExecutionContext context, ICommand command,
        CommandExecutionDelegate<T> next, CancellationToken token)
    {
        var session = command switch
        {
            LeaveBossTowerCommand boss => boss.Context.Session,
            LeaveIllusionTowerCommand illusion => illusion.Context.Session,
            StartBossTowerCommand start => start.Context.Session,
            _ => null,
        };
        if (session is null) return await next();
        var attempt = new TowerReturnSourceAttempt(session, Assert.IsType<DomainTransaction>(transactions.Current),
            GameplayJob.Current!.Value, selected.Current) { Command = command, Token = token };
        trace.Attempts.Add(attempt);
        trace.Steps.Add(command.GetType().Name);
        try
        {
            var result = await next();
            attempt.Result = result;
            if (session == trace.CancelAfterSession && trace.CancelAfterCommit is { } cancellation)
                attempt.Transaction.RegisterAfterCommit(new object(), _ => { cancellation.Cancel(); return ValueTask.CompletedTask; });
            if (trace.FailureMode is not null)
            {
                await characters.Get(session.CharacterId).AddGoldAsync(7, token);
                if (session == trace.FaultSession)
                {
                    if (trace.FailureMode == "uncertain")
                        attempt.Transaction.Enlist(new object(), () => new TowerReturnNonFallibleFailure(trace.Cause));
                    if (trace.FailureMode == "scope") resource.Failure = trace.Cause;
                }
            }
            return result;
        }
        catch (Exception error) { attempt.Error = error; throw; }
    }
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
