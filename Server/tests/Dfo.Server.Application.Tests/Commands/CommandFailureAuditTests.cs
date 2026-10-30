using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Commands;

/// <summary>The audit follows the registration capability and the unit's declared role, never a command or unit name.</summary>
public sealed class CommandFailureAuditTests
{
    [Theory]
    [InlineData(true, CommandFailureAuditRole.Execution, true)]
    [InlineData(false, CommandFailureAuditRole.Execution, false)]
    [InlineData(true, CommandFailureAuditRole.None, false)]
    [InlineData(true, CommandFailureAuditRole.Preparation, false)]
    public async Task Only_a_declared_execution_unit_of_a_registered_command_is_audited(bool registered, CommandFailureAuditRole role, bool audited)
    {
        var state = new State(role);
        var services = new ServiceCollection().AddLogging();
        services.AddCommandDispatching(); services.AddSingleton(state);
        services.AddSingleton<ILogger<GameplayExecutor>>(new SilentLog());
        services.AddInternalCommandHandler<Grant, bool, Handler>(failureAudit: registered
            ? CommandFailureAuditCapability.AuditedFailure : CommandFailureAuditCapability.None);
        await using var provider = services.AddSessionSafetyStandIns().BuildServiceProvider();
        state.Services = provider;
        var command = new Grant();
        var error = await Record.ExceptionAsync(async () => await provider.GetRequiredService<ICommandBus>().SendInternalAsync(
            command, new(GameplayCallSource.Gm, GameplayCallChain.NewRoot(GameplayCallSource.Gm), "gm:POST /probe", InternalCommandCancellation.BodyOnly)));
        Assert.NotNull(error);
        if (audited)
        {
            var carrier = Assert.IsType<CommandAuditedFailureException>(error);
            Assert.Equal(CommandFailureAuditOutcome.Unknown, carrier.Outcome);
            Assert.Same(state.Cause, carrier.AuditedCause.InnerException);
            Assert.Equal(1, state.Records);
        }
        else
        {
            Assert.IsNotType<CommandAuditedFailureException>(error);
            Assert.Equal(0, state.Records);
        }
    }

    private sealed record Grant : ICommand<bool>;
    private sealed class State(CommandFailureAuditRole role)
    {
        internal CommandFailureAuditRole Role { get; } = role;
        internal Exception Cause { get; } = new IOException("post-durable commit fault");
        internal ServiceProvider Services = null!;
        internal int Records;
    }
    private sealed class Handler(State state, ICommandWorkUnits units, ICommandFailureAudit audit) : ICommandHandler<Grant, bool>
    {
        public async ValueTask<bool> HandleAsync(CommandExecutionContext context, Grant command, CancellationToken token)
        {
            try
            {
                return await units.WriteAsync(context, new("probe-grant", [], FailureAudit: state.Role), (_, _) =>
                {
                    state.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist(new object(), () => new Participant(state));
                    return ValueTask.FromResult(true);
                }, token);
            }
            catch (Exception error)
            {
                await audit.RecordAsync(context, error, (_, _) => { state.Records++; return ValueTask.CompletedTask; }, token);
                throw;
            }
        }
    }
    private sealed class Participant(State state) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.FromException(state.Cause);
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
    private sealed class SilentLog : ILogger<GameplayExecutor>
    {
        public IDisposable? BeginScope<TState>(TState s) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState s, Exception? error, Func<TState, Exception?, string> formatter) { }
    }
}
