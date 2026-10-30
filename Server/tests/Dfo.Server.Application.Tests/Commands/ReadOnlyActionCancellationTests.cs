using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Commands;
public sealed class ReadOnlyActionCancellationTests
{
    [Theory]
    [InlineData(true, GameplayCallSource.Timer, InternalCommandCancellation.BodyOnly, false, 0)]
    [InlineData(true, GameplayCallSource.Gm, InternalCommandCancellation.BodyOnly, false, 0)]
    [InlineData(true, GameplayCallSource.StartupRecovery, InternalCommandCancellation.BodyOnly, false, 1)]
    [InlineData(false, GameplayCallSource.Timer, InternalCommandCancellation.BodyOnly, false, 1)]
    [InlineData(true, GameplayCallSource.Timer, InternalCommandCancellation.BodyOnly, true, 1)]
    public async Task Only_a_registered_read_action_can_marshal_its_single_clean_requested_cancellation(
        bool registered, GameplayCallSource source, InternalCommandCancellation mode, bool failCleanup, int expectedErrors)
    {
        using var cancellation = new CancellationTokenSource();
        var state = new State(cancellation, failCleanup);
        var log = new ExecutorLog();
        var services = new ServiceCollection().AddLogging();
        services.AddCommandDispatching(); services.AddSingleton(state);
        services.AddSingleton<ILogger<GameplayExecutor>>(log); services.AddScoped<Resource>();
        services.AddInternalCommandHandler<Snapshot, bool, Handler>(registered
            ? ReadOnlyActionCancellationCapability.ConfirmedRollbackRead : ReadOnlyActionCancellationCapability.None);
        await using var provider = services.AddSessionSafetyStandIns().BuildServiceProvider();
        var error = await Record.ExceptionAsync(async () => await provider.GetRequiredService<ICommandBus>().SendInternalAsync(
            new Snapshot(), new(source, GameplayCallChain.NewRoot(source), "test:snapshot", mode), cancellation.Token));
        Assert.NotNull(error);
        Assert.Equal(expectedErrors, log.Errors);
        if (!failCleanup) Assert.Same(state.Cause, error);
        else Assert.True(Count(error, state.Cause) >= 2, error.ToString());
    }

    [Fact]
    public void A_protocol_registration_cannot_claim_read_cancellation() =>
        Assert.StartsWith("Read-only cancellation", Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([new(typeof(Snapshot),
            typeof(Handler), typeof(object), CommandRouteKind.Protocol, ReadOnlyActionCancellation: ReadOnlyActionCancellationCapability.ConfirmedRollbackRead)])).Message);

    private static int Count(Exception error, Exception cause) => ReferenceEquals(error, cause) ? 1 : error is AggregateException aggregate
        ? aggregate.InnerExceptions.Sum(item => Count(item, cause)) : error.InnerException is {} inner ? Count(inner, cause) : 0;
    private sealed record Snapshot : ICommand<bool>;
    private sealed record State(CancellationTokenSource Cancellation, bool FailCleanup)
    { public OperationCanceledException Cause { get; } = new("timer requested cancellation"); }
    private sealed class Resource(State state) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => state.FailCleanup ? ValueTask.FromException(state.Cause) : ValueTask.CompletedTask;
    }
    private sealed class Handler(State state, Resource resource, ICommandWorkUnits units) : ICommandHandler<Snapshot, bool>
    {
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, Snapshot command, CancellationToken token)
        {
            _ = resource;
            return units.ReadOnlyAsync<bool>(context, new("any.snapshot", []), _ =>
            { state.Cancellation.Cancel(); throw state.Cause; }, token);
        }
    }
    private sealed class ExecutorLog : ILogger<GameplayExecutor>
    {
        public int Errors;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Error) Interlocked.Increment(ref Errors); }
    }
}
