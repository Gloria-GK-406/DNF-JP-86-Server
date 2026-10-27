using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Tests.Commands;
public sealed class InternalWriteResultCancellationTests
{
    [Theory]
    [InlineData("action", GameplayCallSource.Timer, true, 0)]
    [InlineData("action", GameplayCallSource.Event, true, 0)]
    [InlineData("action", GameplayCallSource.StartupRecovery, true, 0)]
    [InlineData("action", GameplayCallSource.Timer, false, 1)]
    [InlineData("scope-same", GameplayCallSource.Timer, true, 1)]
    [InlineData("commit", GameplayCallSource.Timer, true, 1)]
    [InlineData("committed", GameplayCallSource.Timer, true, 1)]
    [InlineData("readonly", GameplayCallSource.Timer, true, 1)]
    [InlineData("main", GameplayCallSource.Timer, true, 1)]
    public async Task Only_the_registered_clean_ordinary_action_cancellation_is_marshaled(string mode, GameplayCallSource source, bool declared, int errors)
    {
        using var state = new State(mode);
        var log = new CapturedLog();
        var services = new ServiceCollection().AddLogging();
        services.AddCommandDispatching(); services.AddSingleton(state); services.AddScoped<Resource>();
        services.AddSingleton<ILogger<GameplayExecutor>>(log);
        services.AddInternalCommandHandler<Probe, bool, Handler>(internalActionCancellation: declared
            ? InternalActionCancellationCapability.ConfirmedRollbackWriteResult : InternalActionCancellationCapability.None);
        await using var provider = services.BuildServiceProvider();
        var error = await Record.ExceptionAsync(() => provider.GetRequiredService<ICommandBus>().SendInternalAsync(new Probe(),
            new(source, GameplayCallChain.NewRoot(source), "test:write-result-cancellation", InternalCommandCancellation.BodyOnly), state.Cancellation.Token).AsTask());
        Assert.NotNull(error); Assert.Equal(errors, log.Errors);
        Assert.Equal(errors != 0, GameplayJob.EndedAJob(error));
        if (errors == 0) Assert.Same(state.Cause, error);
        Assert.True(state.Disposed);
    }
    [Fact]
    public void A_non_internal_registration_cannot_declare_the_cancellation_capability()
    {
        Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([
            new(typeof(Probe), typeof(Handler), typeof(object), CommandRouteKind.Protocol,
                InternalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult)]));
    }
    [Fact]
    public void Actual_registration_limits_the_new_action_observation_to_its_named_background_owners()
    {
        using var world = new Dfo.Server.Application.Tests.Inventory.ShopFixture();
        var owners = world.Services.GetServices<CommandHandlerRegistration>()
            .Where(x => x.InternalActionCancellation == InternalActionCancellationCapability.ConfirmedRollbackWriteResult).ToArray();
        Assert.All(owners, x => Assert.Equal(CommandRouteKind.Internal, x.Route));
        Assert.Equal(new[] { "AdvanceAltarDeadlineCommand", "AdvanceCreatureTickCommand", "AdvanceWeddingTickCommand", "ConsumeDomainEventCommand",
            "MaintainAltarCommand", "ReturnDungeonRunCommand", "ScheduledGameplayTaskCommand",
            "SweepTournamentCandidateCommand", "SweepTournamentRewardsCommand" }, owners.Select(x => x.CommandType.Name).Order());
    }
    private sealed record Probe : ICommand<bool>;
    private sealed class State(string mode) : IDisposable
    {
        internal string Mode => mode;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal OperationCanceledException Cause { get; } = new("actual requested action cancellation");
        internal bool Disposed;
        public void Dispose() => Cancellation.Dispose();
    }
    private sealed class Resource(State state) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { state.Disposed = true; return state.Mode == "scope-same" ? ValueTask.FromException(state.Cause) : ValueTask.CompletedTask; }
    }
    private sealed class Handler(State state, Resource resource, ICommandWorkUnits units, IDomainTransactionAccessor transactions) : ICommandHandler<Probe, bool>
    {
        public async ValueTask<bool> HandleAsync(CommandExecutionContext context, Probe command, CancellationToken token)
        {
            _ = resource;
            if (state.Mode == "committed") await units.WriteAsync(context, new("first", []), (_, _) => ValueTask.FromResult(true));
            if (state.Mode == "readonly")
                return await units.ReadOnlyAsync<bool>(context, new("query", []), _ => { state.Cancellation.Cancel(); throw state.Cause; }, token);
            var declaration = new CommandWorkUnitDeclaration("result", [], state.Mode == "main" ? CommandWorkUnitKind.MainSettlement : CommandWorkUnitKind.Ordinary);
            await units.WriteResultAsync<bool>(context, declaration, (_, _) =>
            {
                if (state.Mode == "commit")
                {
                    transactions.Current!.Enlist(new object(), () => new CommitCancellation(state));
                    return ValueTask.FromResult(true);
                }
                state.Cancellation.Cancel(); throw state.Cause;
            }, token);
            return true;
        }
    }
    private sealed class CommitCancellation(State state) : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) { state.Cancellation.Cancel(); return ValueTask.FromException(state.Cause); }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class CapturedLog : ILogger<GameplayExecutor>
    {
        internal int Errors;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Error) Errors++; }
    }
}
