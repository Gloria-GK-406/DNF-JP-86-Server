using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Transactions;

/// <summary>
/// The use case boundary is where the guarantee is handed out. These tests drive it the way a
/// protocol command does, so what they prove is what a handler gets for free: the handler
/// itself never mentions a transaction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UnitOfWorkBehaviorTests
{
    [Fact]
    public async Task HandlerFailureAndRollbackFailureBothSurviveUnitOfWorkDisposal()
    {
        var cause = new IOException("handler failed");
        var rollback = new IOException("rollback failed");
        var probe = new ParticipantProbe { HandlerFailure = cause, RollbackFailure = rollback };
        await using var provider = BuildProvider(probe, FixtureUnitKind.Transactional);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await DomainCommandFixture.Bus(provider).SendAsync(new TestCommand(), default));
        Assert.Equal(TransactionUncertaintyReason.RollbackFailure, failure.Reason);
        var aggregate = Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Contains(cause, aggregate.InnerExceptions);
        Assert.Contains(rollback, aggregate.InnerExceptions);
        Assert.True(TransactionOutcomeUncertainException.Contains(new IOException("wrapped", new AggregateException(failure))));
        Assert.Null(provider.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    [Fact]
    public async Task TransactionalCommand_MakesEveryChangeVisibleWhenItSucceeds()
    {
        var probe = new ParticipantProbe();
        await using var provider = BuildProvider(probe, FixtureUnitKind.Transactional);

        await DomainCommandFixture.Bus(provider)
            .SendAsync(new TestCommand(), CancellationToken.None);

        Assert.All(probe.Participants, participant => Assert.True(participant.Applied));
        Assert.Equal(
            new[] { "prepare:durable", "prepare:memory", "commit:durable", "commit:memory" },
            probe.Log);
    }

    [Fact]
    public async Task TransactionalCommand_MakesNoChangeVisibleWhenTheHandlerFails()
    {
        var probe = new ParticipantProbe
        {
            HandlerFailure = new InvalidOperationException("business failure"),
        };
        await using var provider = BuildProvider(probe, FixtureUnitKind.Transactional);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DomainCommandFixture.Bus(provider)
                .SendAsync(new TestCommand(), CancellationToken.None));

        Assert.NotEmpty(probe.Participants);
        Assert.All(probe.Participants, participant => Assert.False(participant.Applied));
        Assert.All(probe.Participants, participant => Assert.True(participant.RolledBack));
    }

    [Fact]
    public async Task TransactionalCommand_FailsWhenASecondParticipantWhoseCommitCanFailJoins()
    {
        var probe = new ParticipantProbe { EnlistASecondFallibleParticipant = true };
        await using var provider = BuildProvider(probe, FixtureUnitKind.Transactional);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DomainCommandFixture.Bus(provider)
                .SendAsync(new TestCommand(), CancellationToken.None));

        Assert.Contains("at most one", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(probe.Participants, participant => Assert.False(participant.Applied));
    }

    [Fact]
    public async Task DirectCommand_RunsWithNoTransactionOpen()
    {
        var probe = new ParticipantProbe();
        await using var provider = BuildProvider(probe, FixtureUnitKind.Direct);

        await DomainCommandFixture.Bus(provider)
            .SendAsync(new AmbientProbeCommand(), CancellationToken.None);

        Assert.False(probe.SawAmbientTransaction);
    }

    [Fact]
    public async Task TransactionalCommand_LeavesNoTransactionOpenAfterItReturns()
    {
        var probe = new ParticipantProbe();
        await using var provider = BuildProvider(probe, FixtureUnitKind.Transactional);
        var bus = DomainCommandFixture.Bus(provider);

        await bus.SendAsync(new TestCommand(), CancellationToken.None);

        Assert.Null(provider.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    [Fact]
    public async Task Business_refusal_returns_normally_but_rolls_back_every_participant()
    {
        var probe = new ParticipantProbe { RefuseWithoutThrowing = true };
        await using var provider = BuildProvider(probe, FixtureUnitKind.Transactional);
        Assert.NotNull(await DomainCommandFixture.Bus(provider).SendAsync(new TestCommand(), default));
        Assert.All(probe.Participants, p => { Assert.True(p.RolledBack); Assert.False(p.Applied); });
        Assert.Null(provider.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    private static ServiceProvider BuildProvider(
        ParticipantProbe probe,
        FixtureUnitKind executionKind)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCommandDispatching();
        services.AddSingleton(probe);
        services.AddFixtureCommand<TestCommand, TestResult, EnlistingHandler>(executionKind);
        services.AddFixtureCommand<AmbientProbeCommand, TestResult, AmbientProbeHandler>(executionKind);
        return services.AddSessionSafetyStandIns().BuildServiceProvider();
    }

    private sealed record TestResult;

    private sealed record TestCommand : ICommand<TestResult>;

    private sealed record AmbientProbeCommand : ICommand<TestResult>;

    private sealed class ParticipantProbe
    {
        public List<string> Log { get; } = [];

        public List<RecordingParticipant> Participants { get; } = [];

        public bool EnlistASecondFallibleParticipant { get; init; }

        public Exception? HandlerFailure { get; init; }
        public Exception? RollbackFailure { get; init; }

        public bool RefuseWithoutThrowing { get; init; }

        public bool SawAmbientTransaction { get; set; }
    }

    /// <summary>
    /// Deliberately written the way a real handler will be: it asks for the ambient transaction
    /// and joins it, and never opens, commits or rolls anything back.
    /// </summary>
    private sealed class EnlistingHandler(IDomainTransactionAccessor accessor, ParticipantProbe probe)
        : ICommandHandler<TestCommand, TestResult>
    {
        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            TestCommand command,
            CancellationToken cancellationToken)
        {
            var transaction = accessor.Current
                ?? throw new InvalidOperationException(
                    "A transactional command ran with no transaction open.");
            probe.Participants.Add(transaction.Enlist(
                "durable",
                () => new RecordingParticipant("durable", probe.Log, commitCanFail: true)));
            probe.Participants.Add(transaction.Enlist(
                "memory",
                () => new RecordingParticipant("memory", probe.Log, commitCanFail: false)));

            if (probe.EnlistASecondFallibleParticipant)
            {
                transaction.Enlist(
                    "second-durable",
                    () => new RecordingParticipant("second-durable", probe.Log, commitCanFail: true));
            }

            probe.Participants[0].RollbackFailure = probe.RollbackFailure;

            if (probe.RefuseWithoutThrowing) transaction.RequireRollback();
            return probe.HandlerFailure is null
                ? ValueTask.FromResult(new TestResult())
                : throw probe.HandlerFailure;
        }
    }

    private sealed class AmbientProbeHandler(IDomainTransactionAccessor accessor, ParticipantProbe probe)
        : ICommandHandler<AmbientProbeCommand, TestResult>
    {
        public ValueTask<TestResult> HandleAsync(
            CommandExecutionContext context,
            AmbientProbeCommand command,
            CancellationToken cancellationToken)
        {
            probe.SawAmbientTransaction = accessor.Current is not null;
            return ValueTask.FromResult(new TestResult());
        }
    }
}
